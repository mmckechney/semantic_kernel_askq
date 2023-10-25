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

namespace Company.Function
{
    public static class httpTriggerAskAboutADoc
    {
        //function you can call to ask a question about a document.
        [FunctionName("httpTriggerAskAboutADoc")]
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
                string nameWithoutExtension = Path.GetFileNameWithoutExtension(filename); 

                var responseMessage =  await AskOpenAIAsync(nameWithoutExtension, question, log);


                return new OkObjectResult(responseMessage);

                
            }
            catch (Exception ex)
            {
                return new OkObjectResult(ex.Message);
            }
            
            
        }
        static async Task<string> AskOpenAIAsync(string filename, string prompt, ILogger log)
        {
            log.LogInformation("Ask OpenAI Async A Question");
            var openAIEndpoint = Environment.GetEnvironmentVariable("OpenAIEndpoint");
            var chatModel = Environment.GetEnvironmentVariable("OpenAIChatModel");

            var client = new OpenAIClient(new Uri(openAIEndpoint), new DefaultAzureCredential());
          

            log.LogInformation("Ask OpenAI Async A Question 2");
            var content =  await GetBlobContentAsync(filename, log);

            //log.LogInformation(content);

            var chatCompletionsOptions = new ChatCompletionsOptions()
            {
                Messages =
                  {
                      new ChatMessage(ChatRole.System, @"You are a document answering bot.  You will be provided with information from a document, and you are to answer the question based on the content provided.  Your are not to make up answers. Use the content provided to answer the question."),
                      new ChatMessage(ChatRole.User, @"Content = " + content),
                      new ChatMessage(ChatRole.User, @"Question = " + prompt),
                  },
            };


            var completionsResponse = await client.GetChatCompletionsAsync(chatModel, chatCompletionsOptions);
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
                        content += await reader.ReadToEndAsync();
                        
                    }
                }

            }
            return content;
        }
}
}

