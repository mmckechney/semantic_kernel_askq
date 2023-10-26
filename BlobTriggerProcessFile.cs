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
                
               string imgUrl = $"https://{Environment.GetEnvironmentVariable("StorageAccount")}.blob.core.windows.net/raw/{name}"; //?sp=racwdli&st=2023-10-26T17:24:43Z&se=2024-01-02T02:24:43Z&spr=https&sv=2022-11-02&sr=c&sig=2zJpV7Hy47fcYZCZW7pZhxMlp%2FGxY0U1pVE5DEsXL98%3D";

                log.LogInformation(imgUrl);

                Uri fileUri = new Uri(imgUrl);
                
                AnalyzeDocumentOperation operation = await client.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, "prebuilt-read", fileUri); 

                AnalyzeResult result = operation.Value;
                log.LogInformation("About to get data from document intelligence module.");


                var content = "";
                foreach (DocumentPage page in result.Pages)
                {
                    log.LogInformation("Checking out document data...");
                    for (int i = 0; i < page.Lines.Count; i++)
                    {
                        DocumentLine line = page.Lines[i];
                        log.LogInformation($"  Line {i} has content: '{line.Content}'.");
                        content += line.Content.ToString();

                    }

                    log.LogInformation("content = " + content);
                    
                    // Get the extension of the file  
                    string extension = Path.GetExtension(name); 
                    string nameWithoutExtension = Path.GetFileNameWithoutExtension(name); 
                    string newName = nameWithoutExtension.Replace(".", "_"); 
                    newName += "_" + page.PageNumber.ToString() + ".json";
                    string blobName = nameWithoutExtension + "/" + newName;

                    var jsonObj = new  
                    {  
                        FileName = name, 
                        blobName = blobName, 
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
                        blobClient.Upload(stream, overwrite: true);
                         
                    }  
        
                    log.LogInformation($"JSON file {newName} saved to Azure Blob Storage.");
                    content = "";
                }

            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
            }
           


        }

       

    }
}
