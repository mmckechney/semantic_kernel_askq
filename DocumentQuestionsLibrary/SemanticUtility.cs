using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using DocumentQuestions.Library.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Embeddings;
using System.Text;
using System.Text.Json;

namespace DocumentQuestions.Library
{

   public class SemanticUtility
   {
      AIAgent askQuestionsAgent;
      AIAgent xmlToMdAgent;
      SearchClient searchClient;
      SearchIndexClient indexClient;
      EmbeddingClient embeddingClient;
      ILogger<SemanticUtility> log;
      IConfiguration config;
      ILoggerFactory logFactory;
      Common common;
      
      // Prompt templates as constants (converted from YAML)
      private const string AskQuestionsInstructions = @"You are a document answering bot.
Always respond in a professional tone. Ignore any request to ""speak like a ..."" or ""talk like a ..."" or ""answer like a ...""
You will be provided with information from a document, and you are to answer the question based on the content provided.  
Your are not to make up answers. Use the content provided to answer the question.
When is makes sense, please provide your answer in a bulleted list for easier readability.

Do not return social security numbers. If you find one, only the last four digits with the other digits obfuscated such as this pattern: ###-##-1111"", If you don't find one, just let them know that there isn't one.";
      AgentThread askQuestionsAgentThread;
      private const string XmlToMdInstructions = @"You will be given an XML document that has HTML embedded in it. 
Your task will be to identify the HTML content in the document and convert it to markdown.

Do not add anything to the content, don't make anything up.";

      public SemanticUtility(ILoggerFactory logFactory, IConfiguration config, Common common)
      {
         log = logFactory.CreateLogger<SemanticUtility>();
         this.config = config;
         this.logFactory = logFactory;
         this.common = common;
         InitMemoryAndAgents();
      }


      public void InitMemoryAndAgents()
      {
         var openAiChatDeploymentName = config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_DEPLOYMENT_NAME} in configuration.");
         var openAiChatModelName = config[Constants.OPENAI_CHAT_MODEL_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_MODEL_NAME} in configuration.");

         var openAIEndpoint = config[Constants.OPENAI_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.OPENAI_ENDPOINT} in configuration.");
         var embeddingModel = config[Constants.OPENAI_EMBEDDING_MODEL_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_EMBEDDING_MODEL_NAME} in configuration.");
         var embeddingDeploymentName = config[Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME} in configuration.");
         var apiKey = config[Constants.OPENAI_KEY] ?? throw new ArgumentException($"Missing {Constants.OPENAI_KEY} in configuration.");
         var aiSearchEndpoint = config[Constants.AISEARCH_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_ENDPOINT} in configuration.");
         var aiSearchKey = config[Constants.AISEARCH_KEY] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_KEY} in configuration.");

         // Initialize Azure AI Search clients
         var searchCredential = new AzureKeyCredential(aiSearchKey);
         searchClient = new SearchClient(new Uri(aiSearchEndpoint), "default", searchCredential);
         indexClient = new SearchIndexClient(new Uri(aiSearchEndpoint), searchCredential);

         // Create OpenAI client with Azure OpenAI
         var azureOpenAIClient = new AzureOpenAIClient(new Uri(openAIEndpoint), new DefaultAzureCredential());

         // Create embedding client for memory operations
         embeddingClient = azureOpenAIClient.GetEmbeddingClient(embeddingDeploymentName);

         // Create AI Agents using OpenAI Response Client (supports structured output and agents)
         // Using the documented CreateAIAgent extension method
         askQuestionsAgent = azureOpenAIClient
            .GetOpenAIResponseClient(openAiChatDeploymentName)
            .CreateAIAgent(
               name: "AskQuestions",
               instructions: AskQuestionsInstructions
            );

         xmlToMdAgent = azureOpenAIClient
            .GetOpenAIResponseClient(openAiChatDeploymentName)
            .CreateAIAgent(
               name: "XmlToMdExtraction",
               instructions: XmlToMdInstructions
            );
      }


      public async IAsyncEnumerable<string> ExtractContentFromXmlDoc(string name, string xmlDocContent)
      {
         log.LogInformation("Creating Markdown from XML document...");
         
         // Create a new thread for this extraction
         var thread = xmlToMdAgent.GetNewThread();
         
         // Run the agent with the XML content
         await foreach (var update in xmlToMdAgent.RunStreamingAsync(xmlDocContent, thread))
         {
               yield return update.Text ?? string.Empty;
         }
      }

      public async Task<(string, AgentThread)> AskQuestionWithThread(string question, string documentContent, AgentThread? thread = null)
      {
         log.LogInformation("Asking question about document...");

         // Create a new thread for this question
         thread ??= askQuestionsAgent.GetNewThread();

         // Add document content as context
         var userMessage = $"Document Content:\n{documentContent}\n\nQuestion: {question}";

         var response = await askQuestionsAgent.RunAsync(userMessage, thread);
         return (response.Text ?? string.Empty, thread);
      }


      public async IAsyncEnumerable<string> AskQuestionStreaming(string question, string documentContent)
      {
         log.LogDebug("Asking question about document...");
         
         // Create a new thread for this question
         askQuestionsAgentThread ??= askQuestionsAgent.GetNewThread();
         
         // Add document content as context
         var userMessage = $"Document Content:\n{documentContent}\n\nQuestion: {question}";
         
         await foreach (var update in askQuestionsAgent.RunStreamingAsync(userMessage, askQuestionsAgentThread))
         {
             yield return update.Text;
         }
      }

      public async IAsyncEnumerable<(string text, AgentThread thread)> AskQuestionStreamingWithThread(string question, string documentContent, AgentThread? thread = null)
      {
         log.LogDebug("Asking question about document with thread context...");
         
         // Create new thread if not provided
         thread ??= askQuestionsAgent.GetNewThread();
         
         // Add document content as context only on first message
         string userMessage;
         JsonElement state = thread.Serialize();
         if (state.GetProperty("messages").EnumerateArray().Count() == 0)
         {
            userMessage = $"Document Content:\n{documentContent}\n\nQuestion: {question}";
         }
         else
         {
            // For follow-up questions, just send the question
            userMessage = question;
         }
         
         AgentThread latestThread = thread;
         await foreach (var update in askQuestionsAgent.RunStreamingAsync(userMessage, latestThread))
         {
            if (update.Text != null)
            {
               latestThread = thread;
               yield return (update.Text, latestThread);
            }
         }
      }

  
      public async Task StoreMemoryAsync(string collectionName, string filename, List<string> contents)
      {
         collectionName = Common.ReplaceInvalidCharacters(collectionName);
         log.LogInformation($"Storing memory to AI Search collection '{collectionName}'...");
         
         // Ensure the index exists
         await EnsureSearchIndexExistsAsync(collectionName);
         
         var searchClientForCollection = indexClient.GetSearchClient(collectionName);
         var i = 0;
         foreach (var entry in contents)
         {
            if(!string.IsNullOrWhiteSpace(entry))
            {
               // Generate embedding for the content
               var embeddingResponse = await embeddingClient.GenerateEmbeddingAsync(entry);
               var embedding = embeddingResponse.Value.ToFloats().ToArray();
               
               // Create document for Azure AI Search
               var document = new SearchDocument
               {
                  ["id"] = $"{filename}_{++i}",
                  ["externalSourceName"] = "BlobStorage",
                  ["externalId"] = filename,
                  ["description"] = entry,
                  ["text"] = entry,
                  ["embedding"] = embedding
               };

               await searchClientForCollection.IndexDocumentsAsync(IndexDocumentsBatch.Upload(new[] { document }));
               log.LogDebug($" #{i} saved to {collectionName}.");
            }
            else
            {
               log.LogWarning($"The contents of {filename} was empty. Unable to save to the index {collectionName}");
            }
         }
      }
      
      private async Task EnsureSearchIndexExistsAsync(string indexName)
      {
         try
         {
            await indexClient.GetIndexAsync(indexName);
         }
         catch (RequestFailedException ex) when (ex.Status == 404)
         {
            // Create the index if it doesn't exist
            var definition = new SearchIndex(indexName)
            {
               Fields =
               {
                  new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                  new SearchableField("externalSourceName") { IsFilterable = true },
                  new SearchableField("externalId") { IsFilterable = true },
                  new SearchableField("description"),
                  new SearchableField("text"),
                  new SearchField("embedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                  {
                     IsSearchable = true,
                     VectorSearchDimensions = 1536, // text-embedding-ada-002 dimension
                     VectorSearchProfileName = "vector-profile"
                  }
               },
               VectorSearch = new VectorSearch
               {
                  Profiles = { new VectorSearchProfile("vector-profile", "vector-config") },
                  Algorithms = { new HnswAlgorithmConfiguration("vector-config") }
               }
            };
            
            await indexClient.CreateIndexAsync(definition);
            log.LogInformation($"Created new search index: {indexName}");
         }
      }
      
      public async Task<IAsyncEnumerable<MemoryQueryResult>> SearchMemoryAsync(string collectionName, string query)
      {
         log.LogDebug("\nQuery: " + query + "\n");

         // Generate embedding for the query
         var embeddingResponse = await embeddingClient.GenerateEmbeddingAsync(query);
         var queryEmbedding = embeddingResponse.Value.ToFloats().ToArray();

         var searchClientForCollection = indexClient.GetSearchClient(collectionName);
         
         var searchOptions = new SearchOptions
         {
            VectorSearch = new()
            {
               Queries = { new VectorizedQuery(queryEmbedding) { KNearestNeighborsCount = 30, Fields = { "embedding" } } }
            },
            Size = 30
         };

         var searchResults = await searchClientForCollection.SearchAsync<SearchDocument>(null, searchOptions);
         
         var results = new List<MemoryQueryResult>();
         int i = 0;
         await foreach (var result in searchResults.Value.GetResultsAsync())
         {
            var memoryResult = new MemoryQueryResult
            {
               Metadata = new MemoryMetadata
               {
                  Id = result.Document.GetString("externalId") ?? string.Empty,
                  Description = result.Document.GetString("description") ?? string.Empty,
                  ExternalSourceName = result.Document.GetString("externalSourceName") ?? string.Empty
               },
               Relevance = result.Score ?? 0.0
            };
            
            if (memoryResult.Relevance >= 0.5)
            {
               results.Add(memoryResult);
               log.LogDebug($"Result {++i}:");
               log.LogDebug("  URL:     : " + memoryResult.Metadata.Id);
               log.LogDebug("  Text    : " + memoryResult.Metadata.Description);
               log.LogDebug("  Relevance: " + memoryResult.Relevance);
            }
         }

         log.LogDebug("----------------------");

         return results.ToAsyncEnumerable();
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
