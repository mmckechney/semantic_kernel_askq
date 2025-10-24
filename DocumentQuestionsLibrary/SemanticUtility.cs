using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DocumentQuestions.Library;

public partial class AgentUtility
{
   private const string VectorFieldName = "contentVector";
   private const string ContentFieldName = "content";
   private const string FileNameFieldName = "fileName";
   private const string IdFieldName = "id";

   private readonly ILogger<AgentUtility> log;
   private readonly IConfiguration config;

   private readonly object initLock = new();
   private bool initialized;

   private AzureOpenAIClient? openAiClient;
   private IChatClient? chatClient;
   private ChatClientAgent? agent;
   private AiSearch aiSearchAdmin;
   private IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator;
   private Uri? searchEndpoint;
   private string? searchKey;
   private string chatModelId = string.Empty;

   private readonly ConcurrentDictionary<string, PromptDefinition> prompts = new(StringComparer.OrdinalIgnoreCase);

   public AgentUtility(ILoggerFactory logFactory, IConfiguration config, Common common, AiSearch aiSearchAdmin)
   {
      log = logFactory.CreateLogger<AgentUtility>();
      this.config = config;
      _ = common ?? throw new ArgumentNullException(nameof(common));
      this.aiSearchAdmin = aiSearchAdmin ?? throw new ArgumentNullException(nameof(common));
      LoadPrompts();
      EnsureInitialized();
   }

   public void ReloadAgentResources()
   {
      lock (initLock)
      {
         initialized = false;
      }
      LoadPrompts();
      EnsureInitialized();
   }

   public async IAsyncEnumerable<string> ExtractContentFromXmlDoc(string name, string xmlDocContent, [EnumeratorCancellation] CancellationToken cancellationToken = default)
   {
      log.LogInformation("Creating Markdown from XML document...");
      var parameters = CreateTemplateParameters(("content", xmlDocContent));
      await foreach (var chunk in RunPromptStreamingAsync("XmltoMdExtraction", parameters, cancellationToken))
      {
         yield return chunk;
      }
   }

   public async Task<string> AskQuestion(string question, string documentContent, CancellationToken cancellationToken = default)
   {
      log.LogInformation("Asking question about document...");
      var parameters = CreateTemplateParameters(("question", question), ("content", documentContent));
      return await RunPromptAsync("AskQuestions", parameters, cancellationToken).ConfigureAwait(false);
   }

   public async IAsyncEnumerable<string> AskQuestionStreaming(string question, string documentContent, [EnumeratorCancellation] CancellationToken cancellationToken = default)
   {
      log.LogDebug("Asking question about document...");
      var parameters = CreateTemplateParameters(("question", question), ("content", documentContent));
      await foreach (var chunk in RunPromptStreamingAsync("AskQuestions", parameters, cancellationToken))
      {
         yield return chunk;
      }
   }

   public async Task StoreMemoryAsync(string collectionName, string filename, IEnumerable<string> contents, CancellationToken cancellationToken = default)
   {
      EnsureInitialized();

      if (string.IsNullOrWhiteSpace(collectionName))
      {
         throw new ArgumentException("Collection name cannot be empty.", nameof(collectionName));
      }

      collectionName = Common.ReplaceInvalidCharacters(collectionName);
      await aiSearchAdmin.AddIndex(collectionName);
      log.LogInformation("Storing memory to AI Search collection '{Collection}'...", collectionName);

      var credential = new AzureKeyCredential(searchKey!);
      var client = new SearchClient(searchEndpoint!, collectionName, credential);

      var documents = new List<SearchDocument>();
      var index = 0;

      foreach (var entry in contents)
      {
         if (string.IsNullOrWhiteSpace(entry))
         {
            log.LogWarning("The contents of {File} was empty. Unable to save to the index {Collection}", filename, collectionName);
            continue;
         }

         var embedding = await embeddingGenerator!.GenerateAsync(entry, cancellationToken: cancellationToken).ConfigureAwait(false);
         var docId = Common.ReplaceInvalidCharacters($"{filename}_{index++:D6}");
         var vector = embedding.Vector.ToArray();

         var document = new SearchDocument
         {
            [IdFieldName] = docId,
            [FileNameFieldName] = filename,
            [ContentFieldName] = entry,
            [VectorFieldName] = vector
         };

         documents.Add(document);
      }

      if (documents.Count == 0)
      {
         log.LogWarning("No documents generated for {File} in collection {Collection}", filename, collectionName);
         return;
      }

      await client.MergeOrUploadDocumentsAsync(documents, cancellationToken: cancellationToken).ConfigureAwait(false);
      log.LogInformation("{Count} entries saved to {Collection}.", documents.Count, collectionName);
   }

   public async Task<IReadOnlyList<SemanticMemoryResult>> SearchMemoryAsync(string collectionName, string query, CancellationToken cancellationToken = default)
   {
      EnsureInitialized();

      log.LogDebug("\nQuery: {Query}\n", query);

      collectionName = Common.ReplaceInvalidCharacters(collectionName);
      var credential = new AzureKeyCredential(searchKey!);
      var client = new SearchClient(searchEndpoint!, collectionName, credential);

      var embedding = await embeddingGenerator!.GenerateAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false);
      var vectorQuery = new VectorizedQuery(embedding.Vector.ToArray())
      {
         KNearestNeighborsCount = 30
      };
      vectorQuery.Fields.Add(VectorFieldName);

      var options = new SearchOptions
      {
         Size = 30,
         VectorSearch = new VectorSearchOptions()
      };
      options.VectorSearch.Queries.Add(vectorQuery);
      options.Select.Add(IdFieldName);
      options.Select.Add(ContentFieldName);
      options.Select.Add(FileNameFieldName);
      options.IncludeTotalCount = true;

      var response = await client.SearchAsync<SearchDocument>(null, options, cancellationToken).ConfigureAwait(false);

      var results = new List<SemanticMemoryResult>();
      await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
      {
         var content = result.Document.TryGetValue(ContentFieldName, out var textObj) ? textObj as string : null;
         var fileName = result.Document.TryGetValue(FileNameFieldName, out var fileObj) ? fileObj as string : null;
         var id = result.Document.TryGetValue(IdFieldName, out var idObj) ? idObj as string : null;
         results.Add(new SemanticMemoryResult(id, fileName, content, result.Score));

         log.LogDebug("Result {Index}:\n  Id: {Id}\n  File: {File}\n  Score: {Score}", results.Count, id, fileName, result.Score);
      }

      log.LogDebug("----------------------");
      return results;
   }

   public async Task<string> SearchForReleventContent(string collectionName, string query, CancellationToken cancellationToken = default)
   {
      var sb = new StringBuilder();
      var results = await SearchMemoryAsync(collectionName, query, cancellationToken).ConfigureAwait(false);
      foreach (var result in results)
      {
         if (!string.IsNullOrWhiteSpace(result.Content))
         {
            sb.AppendLine(result.Content);
         }
      }

      return sb.ToString();
   }

   private async Task<string> RunPromptAsync(string promptKey, IReadOnlyDictionary<string, string?> parameters, CancellationToken cancellationToken)
   {
      EnsureInitialized();
      var prompt = GetPrompt(promptKey);
      var rendered = prompt.Render(parameters);
      var messages = ParseMessages(rendered);
      var options = CreateAgentRunOptions(prompt);

      var response = await agent!.RunAsync(messages, null, options, cancellationToken).ConfigureAwait(false);
      return response.Text ?? string.Empty;
   }

   private async IAsyncEnumerable<string> RunPromptStreamingAsync(string promptKey, IReadOnlyDictionary<string, string?> parameters, [EnumeratorCancellation] CancellationToken cancellationToken)
   {
      EnsureInitialized();
      var prompt = GetPrompt(promptKey);
      var rendered = prompt.Render(parameters);
      var messages = ParseMessages(rendered);
      var options = CreateAgentRunOptions(prompt);

      await foreach (var update in agent!.RunStreamingAsync(messages, null, options, cancellationToken).ConfigureAwait(false))
      {
         if (!string.IsNullOrEmpty(update.Text))
         {
            yield return update.Text!;
         }
      }
   }

   private void EnsureInitialized()
   {
      if (initialized)
      {
         return;
      }

      lock (initLock)
      {
         if (initialized)
         {
            return;
         }

         var openAiChatDeploymentName = config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_DEPLOYMENT_NAME} in configuration.");
         chatModelId = config[Constants.OPENAI_CHAT_MODEL_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_MODEL_NAME} in configuration.");
         var openAiEndpoint = config[Constants.OPENAI_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.OPENAI_ENDPOINT} in configuration.");
         var embeddingDeployment = config[Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME} in configuration.");
         var apiKey = config[Constants.OPENAI_KEY] ?? throw new ArgumentException($"Missing {Constants.OPENAI_KEY} in configuration.");
         var aiSearchEndpoint = config[Constants.AISEARCH_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_ENDPOINT} in configuration.");
         var aiSearchKey = config[Constants.AISEARCH_KEY] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_KEY} in configuration.");

         openAiClient = new AzureOpenAIClient(new Uri(openAiEndpoint), new AzureKeyCredential(apiKey));

         var chatDeploymentClient = openAiClient.GetChatClient(openAiChatDeploymentName);
         chatClient = chatDeploymentClient.AsIChatClient();
         agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
         {
            Name = "DocumentQuestionsAgent",
            Instructions = "You are a document answering bot that uses provided context to respond."
         });

         var embeddingClient = openAiClient.GetEmbeddingClient(embeddingDeployment);
         embeddingGenerator = embeddingClient.AsIEmbeddingGenerator();

         searchEndpoint = new Uri(aiSearchEndpoint);
         searchKey = aiSearchKey;

         initialized = true;
      }
   }

   private PromptDefinition GetPrompt(string promptKey)
   {
      var prefixed = $"Prompts_{promptKey}";
      if (!prompts.TryGetValue(prefixed, out var prompt))
      {
         throw new InvalidOperationException($"Prompt '{promptKey}' was not found.");
      }

      return prompt;
   }

   private AgentRunOptions CreateAgentRunOptions(PromptDefinition prompt)
   {
      var chatOptions = new ChatOptions
      {
         Temperature = prompt.Temperature.HasValue ? (float?)prompt.Temperature : null,
         MaxOutputTokens = prompt.MaxTokens,
         ModelId = chatModelId
      };

      return new ChatClientAgentRunOptions
      {
         ChatOptions = chatOptions
      };
   }

   private static IReadOnlyList<ChatMessage> ParseMessages(string rendered)
   {
      var matches = MessageRegex().Matches(rendered);
      if (matches.Count == 0)
      {
         return [new ChatMessage(ChatRole.User, rendered.Trim())];
      }

      var messages = new List<ChatMessage>(matches.Count);
      foreach (Match match in matches)
      {
         var role = match.Groups["role"].Value.Trim().ToLowerInvariant();
         var content = match.Groups["content"].Value.Trim();
         var chatRole = role switch
         {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            _ => ChatRole.User
         };
         messages.Add(new ChatMessage(chatRole, content));
      }

      return messages;
   }

   private void LoadPrompts()
   {
      var assembly = Assembly.GetExecutingAssembly();
      foreach (var resourceName in assembly.GetManifestResourceNames())
      {
         if (!resourceName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
         {
            continue;
         }

         using var stream = assembly.GetManifestResourceStream(resourceName);
         if (stream == null)
         {
            continue;
         }

         using var reader = new StreamReader(stream);
         var yaml = reader.ReadToEnd();
         var promptConfig = DeserializePrompt(yaml);

         var keyParts = resourceName.Split('.');
         var key = keyParts.Length > 3 ? $"{keyParts[^3]}_{keyParts[^2]}" : keyParts[^2];

         var template = promptConfig.Template ?? string.Empty;
         var settings = promptConfig.ExecutionSettings != null && promptConfig.ExecutionSettings.TryGetValue("default", out var exec)
            ? exec
            : new PromptExecutionSettings();

         var promptDefinition = new PromptDefinition(template, settings.Temperature, settings.MaxTokens);
         prompts[key] = promptDefinition;
      }
   }

   private static IReadOnlyDictionary<string, string?> CreateTemplateParameters(params (string Key, string? Value)[] entries)
   {
      var dictionary = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
      foreach (var entry in entries)
      {
         dictionary[entry.Key] = entry.Value;
      }

      return dictionary;
   }

   private static PromptYaml DeserializePrompt(string yaml)
   {
      var deserializer = new DeserializerBuilder()
         .WithNamingConvention(CamelCaseNamingConvention.Instance)
         .IgnoreUnmatchedProperties()
         .Build();

      return deserializer.Deserialize<PromptYaml>(yaml) ?? new PromptYaml();
   }

   [GeneratedRegex("<message\\s+role=\"(?<role>[^\"]+)\"\\s*>\\s*(?<content>[\\s\\S]*?)\\s*</message>", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
   private static partial Regex MessageRegex();

   private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string?> parameters)
   {
      if (string.IsNullOrEmpty(template) || parameters is null)
      {
         return template;
      }

      var result = template;
      foreach (var kvp in parameters)
      {
         var value = kvp.Value ?? string.Empty;
         var placeholder = $"{{{{{kvp.Key}}}}}";
         result = result.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);
      }

      return result;
   }

   private sealed record PromptDefinition(string Template, double? Temperature, int? MaxTokens)
   {
      public double? Temperature { get; } = Temperature;
      public int? MaxTokens { get; } = MaxTokens;
      public string Render(IReadOnlyDictionary<string, string?> data) => ApplyTemplate(Template, data);
   }

   private sealed class PromptYaml
   {
      public string? Template { get; set; }

      [YamlMember(Alias = "execution_settings")]
      public Dictionary<string, PromptExecutionSettings>? ExecutionSettings { get; set; }
   }

   private sealed class PromptExecutionSettings
   {
      [YamlMember(Alias = "max_tokens")]
      public int? MaxTokens { get; set; }

      public double? Temperature { get; set; }
   }
}

public sealed record SemanticMemoryResult(string? Id, string? FileName, string? Content, double? Score);
