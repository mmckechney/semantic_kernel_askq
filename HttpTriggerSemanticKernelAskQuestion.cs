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
using Microsoft.SemanticKernel.Connectors.Memory.AzureCognitiveSearch;

namespace Company.Function
{
    
    public static class HttpTriggerSemanticKernelAskQuestion
    {
        private static SemanticMemory semanticMemory;
        static HttpTriggerSemanticKernelAskQuestion()
        {
            semanticMemory = new SemanticMemory();
        }

        //function you can call to ask a question about a document.
        [FunctionName("HttpTriggerSemanticKernelAskQuestion")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request for HttpTriggerSemanticKernelAskQuestion.");

            semanticMemory.InitMemory();

            try
            {
                (string filename, string question) = await Common.GetFilenameAndQuery(req, log);
                var memories = await semanticMemory.SearchMemoryAsync(filename, question, log);
                
                //pass relevant memories to OpenAI - this will reduce the tokens for a prompt.
                var responseMessage =  await AskOpenAIAsync(question, memories, log);
                return new OkObjectResult(responseMessage);
            }
            catch (Exception ex)
            {
                return new OkObjectResult(ex.Message);
            }
            
            
        }
        static async Task<string> AskOpenAIAsync(string prompt, IAsyncEnumerable<MemoryQueryResult> memories, ILogger log)
        {
            log.LogInformation("Ask OpenAI Async A Question");
            
            
            var content = "";
            var docName = "";
            await foreach (MemoryQueryResult memoryResult in memories)
            {
                log.LogInformation("Memory Result = " + memoryResult.Metadata.Description);
                if(docName != memoryResult.Metadata.Id.Substring(0, memoryResult.Metadata.Id.LastIndexOf('_')))
                {
                    docName = memoryResult.Metadata.Id.Substring(0, memoryResult.Metadata.Id.LastIndexOf('_'));
                    content += $"\nDocument Name: {docName}\n";
                }
                content += memoryResult.Metadata.Description;
            };

            var chatCompletionsOptions = Common.GetChatCompletionsOptions(content, prompt);
            var completionsResponse = await Common.Client.GetChatCompletionsAsync(Common.ChatModel, chatCompletionsOptions);
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
}

