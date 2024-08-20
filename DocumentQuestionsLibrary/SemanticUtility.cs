using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureAISearch;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;
using System.Reflection;
using System.Text;

namespace DocumentQuestions.Library
{
#pragma warning disable SKEXP0052 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable SKEXP0021 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable SKEXP0011 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

   public class SemanticUtility
   {
      Kernel kernel;
      ISemanticTextMemory semanticMemory;
      ILogger<SemanticUtility> log;
      IConfiguration config;
      ILoggerFactory logFactory;
      bool usingVolatileMemory = false;
      Common common;
      public SemanticUtility(ILoggerFactory logFactory, IConfiguration config, Common common)
      {
         log = logFactory.CreateLogger<SemanticUtility>();
         this.config = config;
         this.logFactory = logFactory;
         this.common = common;
         InitMemoryAndKernel();
      }


      public void InitMemoryAndKernel()
      {
         var openAiChatDeploymentNAme = config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_DEPLOYMENT_NAME} in configuration.");
         var openAiChatModelName = config[Constants.OPENAI_CHAT_MODEL_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_MODEL_NAME} in configuration.");

         var openAIEndpoint = config[Constants.OPENAI_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.OPENAI_ENDPOINT} in configuration.");
         var embeddingModel = config[Constants.OPENAI_EMBEDDING_MODEL_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_EMBEDDING_MODEL_NAME} in configuration.");
         var embeddingDeploymentName = config[Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME} in configuration.");
         var apiKey = config[Constants.OPENAI_KEY] ?? throw new ArgumentException($"Missing {Constants.OPENAI_KEY} in configuration.");
         var cogSearchEndpoint = config[Constants.AISEARCH_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_ENDPOINT} in configuration.");
         var cogSearchAdminKey = config[Constants.AISEARCH_KEY] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_KEY} in configuration.");


         //Build and configure Memory Store
         IMemoryStore store = new AzureAISearchMemoryStore(cogSearchEndpoint, cogSearchAdminKey);
         


         var memBuilder = new MemoryBuilder()
             .WithMemoryStore(store)
             .WithTextEmbeddingGeneration(new AzureOpenAITextEmbeddingGenerationService(deploymentName: embeddingDeploymentName, modelId: embeddingModel, endpoint: openAIEndpoint, apiKey: apiKey))
             .WithLoggerFactory(logFactory);

         semanticMemory = memBuilder.Build();

         //Build and configure the kernel
         var kernelBuilder = Kernel.CreateBuilder();
         kernelBuilder.AddAzureOpenAIChatCompletion(deploymentName: openAiChatDeploymentNAme, modelId: openAiChatModelName, endpoint: openAIEndpoint, apiKey: apiKey);
         //kernelBuilder.Services.AddSingleton(logFactory);

         kernel = kernelBuilder.Build();

         var assembly = Assembly.GetExecutingAssembly();
         var resources = assembly.GetManifestResourceNames().ToList();
         Dictionary<string, KernelFunction> yamlPrompts = new();
         resources.ForEach(r =>
         {
            if (r.ToLower().EndsWith("yaml"))
            {
               var count = r.Split('.').Count();
               var key = count > 3 ? $"{r.Split('.')[count - 3]}_{r.Split('.')[count - 2]}" : r.Split('.')[count - 2];
               using StreamReader reader = new(Assembly.GetExecutingAssembly().GetManifestResourceStream(r)!);
               var content = reader.ReadToEnd();
               var func = kernel.CreateFunctionFromPromptYaml(content, promptTemplateFactory: new HandlebarsPromptTemplateFactory());
               yamlPrompts.Add(key, func);
            }
         });
         var plugin = KernelPluginFactory.CreateFromFunctions("YAMLPlugins", yamlPrompts.Select(y => y.Value).ToArray());
         kernel.Plugins.Add(plugin);

      }
      public async Task<string> AskQuestion(string question, string documentContent)
      {
         log.LogInformation("Asking question about document...");
         var result = await kernel.InvokeAsync("YAMLPlugins", "AskQuestions", new() { { "question", question }, { "content", documentContent } });
         return result.GetValue<string>();
      }

      public async IAsyncEnumerable<string> AskQuestionStreaming(string question, string documentContent)
      {
         log.LogDebug("Asking question about document...");
         var result = kernel.InvokeStreamingAsync("YAMLPlugins", "AskQuestions", new() { { "question", question }, { "content", documentContent } });
         await foreach (var item in result)
         {
            yield return item.ToString();
         }
      }

  
      public async Task StoreMemoryAsync(string collectionName, Dictionary<string, string> docFile)
      {
         log.LogInformation($"Storing memory to AI Search collection '{collectionName}'...");
         var i = 0;
         foreach (var entry in docFile)
         {
            await semanticMemory.SaveReferenceAsync(
                collection: collectionName,
                externalSourceName: "BlobStorage",
                externalId: entry.Key,
                description: entry.Value,
                text: entry.Value);

            log.LogDebug($" #{++i} saved to {collectionName}.");
         }
      }
      public async Task<IAsyncEnumerable<MemoryQueryResult>> SearchMemoryAsync(string collectionName, string query)
      {
         //If using Volatile Memory, first need to re-populate memory from Blob storage
         if (usingVolatileMemory)
         {
            var docFile = await common.GetBlobContentDictionaryAsync(collectionName);
            await StoreMemoryAsync(collectionName, docFile);
         }

         log.LogDebug("\nQuery: " + query + "\n");

         var memoryResults = semanticMemory.SearchAsync(collectionName, query, limit: 30, minRelevanceScore: 0.5, withEmbeddings: true);

         int i = 0;
         await foreach (MemoryQueryResult memoryResult in memoryResults)
         {
            log.LogDebug($"Result {++i}:");
            log.LogDebug("  URL:     : " + memoryResult.Metadata.Id);
            log.LogDebug("  Text    : " + memoryResult.Metadata.Description);
            log.LogDebug("  Relevance: " + memoryResult.Relevance);


         }

         log.LogDebug("----------------------");

         return memoryResults;
      }

      public async Task<string> SearchForReleventContent(string collectionName, string query)
      {
         StringBuilder sb = new();
         var mems = await SearchMemoryAsync(collectionName, query);
         await foreach(var mem in mems)
         {
            sb.AppendLine(mem.Metadata.Description);
         }

         return sb.ToString();
      }


   }
}
