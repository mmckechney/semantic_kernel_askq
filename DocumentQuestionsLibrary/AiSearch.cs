using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using aim = Azure.Search.Documents.Indexes.Models;
namespace DocumentQuestions.Library
{
   public class AiSearch
   {
      SearchIndexClient client;
      ILogger<AiSearch> log;
      IConfiguration config;
      public AiSearch(ILogger<AiSearch> log, IConfiguration config)
      {
         this.log = log;
         this.config = config;
         string endpoint = config[Constants.AISEARCH_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_ENDPOINT} in configuration");
         string key = config[Constants.AISEARCH_KEY] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_KEY} in configuration");
         // Create a client
         client = new SearchIndexClient(new Uri(endpoint), new AzureKeyCredential(key));
      }
      public async Task<List<string>> ListAvailableIndexes(bool unquoted = false)
      {
         try
         {
            List<string> names = new();
            await foreach (var page in client.GetIndexNamesAsync())
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

      public async Task<string> AddIndex(string name)
      {
         try
         {
            // Sanitize name for Azure Search
            name = Common.ReplaceInvalidCharacters(name);

            // If index already exists, return without creating
            try
            {
               var existing = await client.GetIndexAsync(name);
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

            await client.CreateOrUpdateIndexAsync(index);
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
                  var result = await client.DeleteIndexAsync(index);
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
