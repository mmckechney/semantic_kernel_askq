using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using System.ComponentModel;
using aim = Azure.Search.Documents.Indexes.Models;
namespace DocumentQuestions.Library
{
   public class AiSearch
   {

      ILogger<AiSearch> log;
      IConfiguration config;

      private const string VectorFieldName = "contentVector";
      private const string ContentFieldName = "content";
      private const string FileNameFieldName = "fileName";
      private const string IdFieldName = "id";

      private IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator;
      private AiSearch aiSearchAdmin;
      private SearchIndexClient indexClient;
      private EmbeddingClient embeddingClient;
      private Uri searchEndpointUri;
      private AzureKeyCredential searchCredential;
      public AiSearch(ILogger<AiSearch> log, IConfiguration config)
      {
         this.log = log;
         this.config = config;
         string endpoint = config[Constants.AISEARCH_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_ENDPOINT} in configuration");
         this.searchEndpointUri = new Uri(endpoint);
         string key = config[Constants.AISEARCH_KEY] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_KEY} in configuration");
         // Create a client
         this.searchCredential = new AzureKeyCredential(key);
         indexClient = new SearchIndexClient(searchEndpointUri, searchCredential);

         var openAIEndpoint = config[Constants.OPENAI_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.OPENAI_ENDPOINT} in configuration.");
         var embeddingModel = config[Constants.OPENAI_EMBEDDING_MODEL_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_EMBEDDING_MODEL_NAME} in configuration.");
         var embeddingDeploymentName = config[Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME} in configuration.");
         var apiKey = config[Constants.OPENAI_KEY] ?? throw new ArgumentException($"Missing {Constants.OPENAI_KEY} in configuration.");

         var azureOpenAIClient = new AzureOpenAIClient(new Uri(openAIEndpoint), new DefaultAzureCredential());

         // Create embedding client for memory operations
         embeddingClient = azureOpenAIClient.GetEmbeddingClient(embeddingDeploymentName);
         embeddingGenerator = embeddingClient.AsIEmbeddingGenerator();


      }


      public async Task StoreDataInIndex(string collectionName, string filename, IEnumerable<string> contents, CancellationToken cancellationToken = default)
      {
         if (string.IsNullOrWhiteSpace(collectionName))
         {
            throw new ArgumentException("Collection name cannot be empty.", nameof(collectionName));
         }
         await this.EnsureSearchIndexExistsAsync(collectionName);

         collectionName = Common.ReplaceInvalidCharacters(collectionName);
         await aiSearchAdmin.AddIndex(collectionName);
         log.LogInformation("Storing memory to AI Search collection '{Collection}'...", collectionName);

         var client = new SearchClient(searchEndpointUri, collectionName, this.searchCredential);
     
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

      [Description("Searches the specified AI Search index for relevant documents based on the provided query.")]
      public IReadOnlyList<SemanticMemoryResult> SearchIndexAsync([Description("The name of the collection to search.")] string collectionName,
         [Description("The search query.")] string query,
         CancellationToken cancellationToken = default)
      {
         var searchResult = Task.Run(async () =>
         {
            log.LogDebug("\nQuery: {Query}\n", query);

            collectionName = Common.ReplaceInvalidCharacters(collectionName);
            var client = new SearchClient(searchEndpointUri, collectionName, this.searchCredential);

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
         }).GetAwaiter().GetResult();

         return searchResult;
      }
      public async Task<List<string>> ListAvailableIndexes(bool unquoted = false)
      {
         try
         {
            List<string> names = new();
            await foreach (var page in indexClient.GetIndexNamesAsync())
            {
               if (unquoted)
               {
                  names.Add(page);
               }
               else
               {
                  names.Add($"\"{page}\"");
               }
            }
            return names;
         }
         catch (Exception exe)
         {
            log.LogError($"Problem retrieving AI Search Idexes:\r\n{exe.Message}");
            return new List<string>();
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
      public async Task<string> AddIndex(string name)
      {
         try
         {
            // Sanitize name for Azure Search
            name = Common.ReplaceInvalidCharacters(name);

            // If index already exists, return without creating
            try
            {
               var existing = await indexClient.GetIndexAsync(name);
               if (existing != null)
               {
                  log.LogInformation("Index {IndexName} already exists.", name);
                  return name;
               }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
               // Expected when index does not exist; proceed to create
            }

            const string vectorProfileName = "v1-hnsw"; // referenced by field
            const int embeddingDimensions = 1536; // adjust for different embedding models

            var hnsw = new HnswParameters
            {
               M = 30,                // graph degree (typical 16-48)
               EfConstruction = 400,  // construction search depth
               EfSearch = 100,        // query-time search depth
               Metric = VectorSearchAlgorithmMetric.Cosine
            };


            var vectorAlgo = new aim.HnswAlgorithmConfiguration("hnsw-algo")
            {

               Parameters = new HnswParameters
               {
                  M = 30,                // Number of bi-directional links per node (graph degree)
                  EfConstruction = 400,  // Size of dynamic candidate list during index construction
                  EfSearch = 100,        // Size of dynamic candidate list during search
                  Metric = VectorSearchAlgorithmMetric.Cosine // Similarity metric (Cosine, Euclidean, DotProduct)
               }
            };



            var vectorProfile = new VectorSearchProfile(
                      name: vectorProfileName,
                      algorithmConfigurationName: vectorAlgo.Name);


            var vectorSearch = new VectorSearch
            {
               Algorithms = { vectorAlgo },
               Profiles = { vectorProfile },
            };


            var semanticConfig = new SemanticConfiguration(
                name: "semantics",
                new SemanticPrioritizedFields
                {
                   TitleField = new SemanticField("fileName"),
                   ContentFields = { new SemanticField("content") }
                });

            var semanticSettings = new SemanticSearch();
            semanticSettings.Configurations.Add(semanticConfig);


            var index = new aim.SearchIndex(name)
            {
               Fields =
               {
                  new aim.SimpleField("id", aim.SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                  new aim.SimpleField("fileName", aim.SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                  new aim.SearchField("content", aim.SearchFieldDataType.String) { IsSearchable = true, AnalyzerName = aim.LexicalAnalyzerName.EnMicrosoft },
                  new aim.SearchField("contentVector", aim.SearchFieldDataType.Collection(aim.SearchFieldDataType.Single))
                  {
                     VectorSearchDimensions = embeddingDimensions,
                     VectorSearchProfileName = vectorProfileName,
                     IsFilterable = false,
                     IsFacetable = false,
                     IsSortable = false,
                     IsSearchable = true
                  }
               },
               VectorSearch = vectorSearch,
               SemanticSearch = semanticSettings
            };

            await indexClient.CreateOrUpdateIndexAsync(index);
            log.LogInformation("Created vector-enabled index {IndexName}.", name);
            return name;
         }
         catch (Exception exe)
         {
            log.LogError($"Problem creating AI Search Index {name}:\r\n{exe.Message}");
            return "";
         }
      }

      public async Task<List<string>> ClearIndexes(List<string> indexNames)
      {
         List<string> deleted = new();
         var available = await ListAvailableIndexes(true);
         if (indexNames.Contains("all", StringComparer.CurrentCultureIgnoreCase))
         {
            indexNames = await ListAvailableIndexes(true);
         }

         foreach (var index in indexNames)
         {
            if (available.Contains(index, StringComparer.CurrentCultureIgnoreCase))
            {
               try
               {
                  var result = await indexClient.DeleteIndexAsync(index);
                  if (result.Status < 300)
                  {
                     deleted.Add(index);
                  }
                  else
                  {
                     log.LogError($"Problem deleting index {index}:\r\n{result.ReasonPhrase}");
                  }
               }
               catch (Exception exe)
               {
                  log.LogError($"Problem deleting index {index}:\r\n{exe.Message}");
               }
            }
            else
            {
               log.LogWarning($"The file index '{index}' was not found.");
            }
         }
         return deleted;
      }
   }
}
