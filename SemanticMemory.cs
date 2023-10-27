using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Connectors.Memory.AzureCognitiveSearch;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Plugins.Memory;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;

namespace Company.Function
{
    internal class SemanticMemory
    {
        ISemanticTextMemory semanticMemory;
        public SemanticMemory()
        {
        }

        public void InitMemory()
        {

            var openAIEndpoint = Environment.GetEnvironmentVariable("OpenAIEndpoint");
            var embeddingModel = Environment.GetEnvironmentVariable("OpenAIEmbeddingModel");
            var apiKey = Environment.GetEnvironmentVariable("OpenAIKey");
            var cogSearchEndpoint = Environment.GetEnvironmentVariable("CognitiveSearchEndpoint");
            var cogSearchAdminKey = Environment.GetEnvironmentVariable("CognitiveSearchAdminKey");

            IMemoryStore store;
            store = new AzureCognitiveSearchMemoryStore(cogSearchEndpoint, cogSearchAdminKey);

            semanticMemory = new MemoryBuilder()
                //.WithLoggerFactory(logFactory)
                .WithAzureTextEmbeddingGenerationService(embeddingModel, openAIEndpoint, apiKey)
                .WithMemoryStore(store)
                .Build();
        }

        public async Task StoreMemoryAsync(string collectionName, Dictionary<string, string> docFile, ILogger log)
        {
            log.LogInformation("Storing memory...");
            var i = 0;
            foreach (var entry in docFile)
            {
                await semanticMemory.SaveReferenceAsync(
                    collection: collectionName,
                    externalSourceName: "BlobStorage",
                    externalId: entry.Key,
                    description: entry.Value,
                    text: entry.Value);

                log.LogInformation($" #{++i} saved.");
            }

            log.LogInformation("\n----------------------");


        }
        public async Task<IAsyncEnumerable<MemoryQueryResult>> SearchMemoryAsync(string collectionName, string query, ILogger log)
        {
            log.LogInformation("\nQuery: " + query + "\n");

            var memoryResults = semanticMemory.SearchAsync(collectionName, query, limit: 30, minRelevanceScore: 0.5, withEmbeddings: true);

            int i = 0;
            await foreach (MemoryQueryResult memoryResult in memoryResults)
            {
                log.LogInformation($"Result {++i}:");
                log.LogInformation("  URL:     : " + memoryResult.Metadata.Id);
                log.LogInformation("  Text    : " + memoryResult.Metadata.Description);
                log.LogInformation("  Relevance: " + memoryResult.Relevance);


            }

            log.LogInformation("----------------------");

            return memoryResults;
        }


    }
}
