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
using Company.Function.Models;

namespace Company.Function
{
    public static class HttpTriggerAskAboutADoc
    {


        //function you can call to ask a question about a document.
        [FunctionName("HttpTriggerOpenAiSdkAskQuestion")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request for HttpTriggerOpenAiSdkAskQuestion.");

            try
            {
                (string filename, string question) = await Common.GetFilenameAndQuery(req, log);
                var responseMessage = await AskOpenAIAsync(filename, question, log);

                return new OkObjectResult(responseMessage);
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex.Message);
            }


        }
        static async Task<string> AskOpenAIAsync(string filename, string prompt, ILogger log)
        {
            log.LogInformation("Ask OpenAI Async A Question");

            var content = await GetBlobContentAsync(filename, log);

            var chatCompletionsOptions = Common.GetChatCompletionsOptions(content, prompt);
            var completionsResponse = await Common.Client.GetChatCompletionsAsync(Common.ChatModel, chatCompletionsOptions);
            string completion = completionsResponse.Value.Choices[0].Message.Content;

            return completion;
        }

        public static async Task<string> GetBlobContentAsync(string blobName, ILogger log)
        {
            string connectionString = Environment.GetEnvironmentVariable("StorageConnectionString") ?? "DefaultConnection";
            string containerName = Environment.GetEnvironmentVariable("ExtractedContainerName") ?? "DefaultContainer";



            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            var blobs = containerClient.GetBlobs(prefix: blobName);
            log.LogInformation($"Number of blobs {blobs.Count()}");

            var content = "";
            foreach (var blob in blobs)
            {
                blobName = blob.Name;

                BlobClient blobClient = containerClient.GetBlobClient(blobName);

                // Open the blob and read its contents.  
                using (Stream stream = await blobClient.OpenReadAsync())
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        var processedFile = JsonConvert.DeserializeObject<ProcessedFile>(await reader.ReadToEndAsync());
                        content += processedFile.Content;


                    }
                }

            }
            return content;
        }
    }
}

