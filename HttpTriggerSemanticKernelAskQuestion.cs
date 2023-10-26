using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;  
using System.Text;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Linq;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Memory;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;
using Microsoft.SemanticKernel.Memory;
using System.Collections.Generic;

namespace Company.Function
{
    public static class HttpTriggerSemanticKernelAskQuestion
    {
        //function you can call to ask a question about a document.
        [FunctionName("HttpTriggerSemanticKernelAskQuestion")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            try{

            
                string filename = req.Query["filename"];
                string question = req.Query["question"];
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                log.LogInformation(requestBody);
                dynamic data = JsonConvert.DeserializeObject(requestBody);
                filename = filename ?? data?.filename;
                question = question ?? data?.question;

                log.LogInformation("filename = " + filename);
                log.LogInformation("question = " + question);

                IMemoryStore store;
                store = new VolatileMemoryStore();


                var openAIEndpoint = Environment.GetEnvironmentVariable("OpenAIEndpoint");
                var chatModel = Environment.GetEnvironmentVariable("OpenAIChatModel");
                var embeddingModel = Environment.GetEnvironmentVariable("OpenAIEmbeddingModel");
                var apiKey = Environment.GetEnvironmentVariable("OpenAIKey");

                // var kernel = Kernel.Builder
                //     .WithAzureChatCompletionService(chatModel, openAIEndpoint, apiKey)
                //     .WithAzureTextEmbeddingGenerationService(embeddingModel, openAIEndpoint, apiKey)
                //     .Build();

                var memoryWithCustomDb = new MemoryBuilder()
                    .WithAzureTextEmbeddingGenerationService(embeddingModel, openAIEndpoint, apiKey)
                    .WithMemoryStore(store)
                    .Build();

                string nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
                var docFile = await GetBlobContentAsync(nameWithoutExtension, log);
                await MMSemanticMemory.StoreMemoryAsync(memoryWithCustomDb, docFile, log);
                var memories = await MMSemanticMemory.SearchMemoryAsync(memoryWithCustomDb, question, log);

                //pass relevant memories to OpenAI - this will reduce the tokens for a prompt.
                var responseMessage =  await AskOpenAIAsync(filename, question, memories, log);

                return new OkObjectResult(responseMessage);

            }
            catch (Exception ex)
            {
                return new OkObjectResult(ex.Message);
            }
            
            
        }
        static async Task<string> AskOpenAIAsync(string filename,
                                                 string prompt, IAsyncEnumerable<MemoryQueryResult> memories, ILogger log)
        {
            log.LogInformation("Ask OpenAI Async A Question");
            var openAIEndpoint = Environment.GetEnvironmentVariable("OpenAIEndpoint");
            var chatModel = Environment.GetEnvironmentVariable("OpenAIChatModel");

            var client = new OpenAIClient(new Uri(openAIEndpoint), new DefaultAzureCredential());
          
            log.LogInformation("Ask OpenAI Async A Question 2");
            //remove extension from file name if it is there.
            
            var content = "";
            await foreach (MemoryQueryResult memoryResult in memories)
            {
                log.LogInformation("Ask OpenAI Async A Question 3");
                log.LogInformation("Memory Result = " + memoryResult.Metadata.Description);
                content += memoryResult.Metadata.Description;
            };

            var chatCompletionsOptions = new ChatCompletionsOptions()
            {
                Messages =
                  {
                      new ChatMessage(ChatRole.System, @"You are a document answering bot.  You will be provided with information from a document, and you are to answer the question based on the content provided.  Your are not to make up answers. Use the content provided to answer the question."),
                      new ChatMessage(ChatRole.User, @"Document = " + content),
                      new ChatMessage(ChatRole.User, @"Question = " + prompt),
                  },
            };


            var completionsResponse = await client.GetChatCompletionsAsync(chatModel, chatCompletionsOptions);
            string completion = completionsResponse.Value.Choices[0].Message.Content;

            return completion;
        }


        public static async Task<Dictionary<string, string>> GetBlobContentAsync(string blobName, ILogger log)
        {
            string connectionString = Environment.GetEnvironmentVariable("StorageConnectionString") ?? "DefaultConnection";
            string containerName = Environment.GetEnvironmentVariable("ExtractedContainerName") ?? "DefaultContainer";



            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            var blobs = containerClient.GetBlobs(prefix: blobName);
            log.LogInformation($"Number of blobs {blobs.Count()}");

            var content = "";
            Dictionary<string, string> docFile = new();

            foreach (var blob in blobs)
            {
                
                blobName = blob.Name;

                BlobClient blobClient = containerClient.GetBlobClient(blobName);

                // Open the blob and read its contents.  
                using (Stream stream = await blobClient.OpenReadAsync())
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        content += await reader.ReadToEndAsync();
                        docFile.Add(blob.Name, content);
                    }
                }

            }
            return docFile;
        }
}

public static class MMSemanticMemory
{
    private const string MemoryCollectionName = "mmSemanticMemory";

    const string memoryCollectionName = "aboutADoc";

    public static async Task StoreMemoryAsync(ISemanticTextMemory  memory, Dictionary<string, string> docFile, ILogger log)
    {
        log.LogInformation("Storing memory...");
        var i = 0;
        foreach (var entry in docFile)
        {
            await memory.SaveReferenceAsync(
                collection: MemoryCollectionName,
                externalSourceName: "BlobStorage",
                externalId: entry.Key,
                description: entry.Value,
                text: entry.Value);

            log.LogInformation($" #{++i} saved.");
        }

        log.LogInformation("\n----------------------");


    }
    public static async Task<IAsyncEnumerable<MemoryQueryResult>> SearchMemoryAsync(ISemanticTextMemory memory, string query, ILogger log)
    {
        log.LogInformation("\nQuery: " + query + "\n");

        var memoryResults = memory.SearchAsync(MemoryCollectionName, query, limit: 2, minRelevanceScore: 0.5);

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

