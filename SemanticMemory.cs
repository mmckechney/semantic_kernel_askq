using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;
using Microsoft.SemanticKernel.Connectors.Memory.AzureCognitiveSearch;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocumentQuestions.Function
{
    public class SemanticMemory
    {
        ISemanticTextMemory semanticMemory;
        ILogger<SemanticMemory> log;
        IConfiguration config;
        ILoggerFactory logFactory;
        bool usingVolatileMemory = false;
        Common common;
        public SemanticMemory(ILoggerFactory logFactory, IConfiguration config, Common common)
        {
            log = logFactory.CreateLogger<SemanticMemory>();
            this.config = config;
            this.logFactory = logFactory;
            this.common = common;
        }


        public void InitMemory()
        {

            var openAIEndpoint = config["OpenAIEndpoint"] ?? throw new ArgumentException("Missing OpenAIEndpoint in configuration.");
            var embeddingModel = config["OpenAIEmbeddingModel"] ?? throw new ArgumentException("Missing OpenAIEmbeddingModel in configuration.");
            var apiKey = config["OpenAIKey"] ?? throw new ArgumentException("Missing OpenAIKey in configuration.");
            var cogSearchEndpoint = config["CognitiveSearchEndpoint"] ?? "";
            var cogSearchAdminKey = config["CognitiveSearchAdminKey"] ?? "";

            IMemoryStore store;
            if(string.IsNullOrWhiteSpace(cogSearchEndpoint) && string.IsNullOrWhiteSpace(cogSearchAdminKey))
            {
                log.LogInformation("Cognitive Search not configured. Using in-memory store.");
                store = new VolatileMemoryStore();
                usingVolatileMemory = true;
            }
            else
            {
                store = new AzureCognitiveSearchMemoryStore(cogSearchEndpoint, cogSearchAdminKey);
            }
           

            semanticMemory = new MemoryBuilder()
                .WithLoggerFactory(logFactory)
                .WithAzureTextEmbeddingGenerationService(embeddingModel, openAIEndpoint, apiKey)
                .WithMemoryStore(store)
                .Build();
        }

        public async Task StoreMemoryAsync(string collectionName, Dictionary<string, string> docFile)
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
        public async Task<IAsyncEnumerable<MemoryQueryResult>> SearchMemoryAsync(string collectionName, string query)
        {
            //If using Volatile Memory, first need to re-populate memory from Blob storage
            if(usingVolatileMemory)
            {
                var docFile = await common.GetBlobContentDictionaryAsync(collectionName);
                await StoreMemoryAsync(collectionName, docFile);
            }

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
