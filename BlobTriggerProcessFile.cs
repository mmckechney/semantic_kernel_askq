using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;  
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json; 
using System;
using System.IO;
using System.Net.Http;  
using System.Text;  
using System.Threading.Tasks;
using System.Transactions;
using Company.Function.Models;
using System.Collections.Generic;

namespace Company.Function
{
    public class BlobTriggerProcessFile
    {
        [FunctionName("BlobTriggerProcessFile")]
        public async Task RunAsync([BlobTrigger("raw/{name}", Connection = "StorageConnectionString")]Stream myBlob, string name, ILogger log)
        {
            try
            {
                log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");
                string subscriptionKey = Environment.GetEnvironmentVariable("DocumentIntelligenceSubscriptionKey") ?? "Default Sub";;
                string endpoint = Environment.GetEnvironmentVariable("DocumentIntelligenceEndpoint") ?? "Default End";

                log.LogInformation($"subkey =  {subscriptionKey}");
                log.LogInformation($"endpoint =  {endpoint}");

                AzureKeyCredential credential = new AzureKeyCredential(subscriptionKey);
                DocumentAnalysisClient client = new DocumentAnalysisClient(new Uri(endpoint), credential);
                
               string imgUrl = $"https://{Environment.GetEnvironmentVariable("StorageAccount")}.blob.core.windows.net/raw/{name}";

                log.LogInformation(imgUrl);

                Uri fileUri = new Uri(imgUrl);

                log.LogInformation("About to get data from document intelligence module.");
                AnalyzeDocumentOperation operation = await client.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, "prebuilt-read", fileUri); 
                AnalyzeResult result = operation.Value;

                var content = "";
                bool contentFound = false;
                var tasks = new List<Task>();

                //Split by page if there is content...
                foreach (DocumentPage page in result.Pages)
                {
                    log.LogInformation("Checking out document data...");
                    for (int i = 0; i < page.Lines.Count; i++)
                    {
                        DocumentLine line = page.Lines[i];
                        log.LogDebug($"  Line {i} has content: '{line.Content}'.");
                        content += line.Content.ToString();
                        contentFound = true;
                    }

                    log.LogInformation("content = " + content);
                    tasks.Add(WriteAnalysisContent(name, page.PageNumber, content, log));
                    content = "";
                }

                //Otherwise, split by paragraphs
                if (!contentFound && result.Paragraphs != null)
                {
                    var counter = 0;
                    foreach (DocumentParagraph paragraph in result.Paragraphs)
                    {
                        if (paragraph != null && !string.IsNullOrWhiteSpace(paragraph.Content))
                        {
                            tasks.Add(WriteAnalysisContent(name, counter, paragraph.Content, log));
                            counter++;
                        }
                    }
                }

                Task.WaitAll(tasks.ToArray());
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
            }
           


        }

        private async Task<bool> WriteAnalysisContent(string name, int counter, string content, ILogger log)
        {
            try
            {
                // Get the extension of the file  
                string extension = Path.GetExtension(name);
                string nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
                string newName = nameWithoutExtension.Replace(".", "_");
                newName += $"_{counter.ToString().PadLeft(4,'0')}.json";
                string blobName = nameWithoutExtension + "/" + newName;

                var jsonObj = new ProcessedFile
                {
                    FileName = name,
                    BlobName = blobName,
                    Content = content
                };
                string jsonStr = JsonConvert.SerializeObject(jsonObj);
                // Save the JSON string to Azure Blob Storage  
                string connectionString = Environment.GetEnvironmentVariable("StorageConnectionString") ?? "DefaultConnection";
                string containerName = Environment.GetEnvironmentVariable("ExtractedContainerName") ?? "DefaultContainer";

                BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                containerClient.CreateIfNotExists();

                BlobClient blobClient = containerClient.GetBlobClient(blobName);

                using (var stream = new MemoryStream())
                {
                    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonStr);
                    stream.Write(jsonBytes, 0, jsonBytes.Length);
                    stream.Seek(0, SeekOrigin.Begin);
                    await blobClient.UploadAsync(stream, overwrite: true);

                }

                log.LogInformation($"JSON file {newName} saved to Azure Blob Storage.");
                return true;

            }
            catch (Exception exe)
            {
                log.LogError("Unable to save file: " + exe.Message);
                return false;
            }
        }

    }
}
