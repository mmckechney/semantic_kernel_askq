using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Storage.Blobs;
using DocumentQuestions.Function.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace DocumentQuestions.Function
{
    public class Common
    {
        ILogger<Common> log;
        IConfiguration config;
        public Common(ILogger<Common> log, IConfiguration config)
        {
            this.log = log;
            this.config = config;
        }
        
        public async Task<(string filename, string question)> GetFilenameAndQuery(HttpRequestData req)
        {
            string filename = req.Query["filename"];
            string question = req.Query["question"];
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            log.LogInformation(requestBody);
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            filename = filename ?? data?.filename;
            question = question ?? data?.question;

            if (string.IsNullOrWhiteSpace(filename))
            {
                filename = "general";
            }
            else
            {
                filename = Path.GetFileNameWithoutExtension(filename);
            }


            log.LogInformation("filename = " + filename);
            log.LogInformation("question = " + question);

            return (filename, question);
        }

        public async Task<string> GetBlobContentAsync(string blobName)
        {
            string connectionString = config["StorageConnectionString"] ?? throw new ArgumentException("Missing StorageConnectionString in configuration.");
            string containerName = config["ExtractedContainerName"] ?? throw new ArgumentException("Missing ExtractedContainerName in configuration.");



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

        public async Task<Dictionary<string, string>> GetBlobContentDictionaryAsync(string blobName)
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
