using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Storage.Blobs;
using DocumentQuestions.Library;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DocumentQuestions.Library.Models;
namespace DocumentQuestions.Function
{
   public class BlobTriggerProcessFile
   {
      private SemanticUtility semanticMemory;
      private ILoggerFactory logFactory;
      private ILogger<BlobTriggerProcessFile> log;
      private IConfiguration config;
      private DocumentAnalysisClient documentAnalysisClient;
      public BlobTriggerProcessFile(ILoggerFactory logFactory, IConfiguration config, SemanticUtility semanticMemory, DocumentAnalysisClient documentAnalysisClient)
      {
         this.semanticMemory = semanticMemory;
         this.logFactory = logFactory;
         log = logFactory.CreateLogger<BlobTriggerProcessFile>();
         this.config = config;
         this.documentAnalysisClient = documentAnalysisClient;
      }

      [Function("BlobTriggerProcessFile")]
      public async Task RunAsync([BlobTrigger("raw/{name}", Connection = "StorageConnectionString")] Stream myBlob, string name)
      {
         try
         {
            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name}");
            string subscriptionKey = config["DocumentIntelligenceSubscriptionKey"] ?? throw new ArgumentException("Missing DocumentIntelligenceSubscriptionKey in configuration.");
            string endpoint = config["DocumentIntelligenceEndpoint"] ?? throw new ArgumentException("Missing DocumentIntelligenceEndpoint in configuration.");
            string storageAccountName = config["StorageAccount"] ?? throw new ArgumentException("Missing StorageAccount in configuration.");
            string memoryCollectionName = Path.GetFileNameWithoutExtension(name);

            log.LogInformation($"subkey =  {subscriptionKey}");
            log.LogInformation($"endpoint =  {endpoint}");

            semanticMemory.InitMemoryAndKernel();

            string imgUrl = $"https://{storageAccountName}.blob.core.windows.net/raw/{name}";

            log.LogInformation(imgUrl);

            Uri fileUri = new Uri(imgUrl);

            log.LogInformation("About to get data from document intelligence module.");
            AnalyzeDocumentOperation operation = await documentAnalysisClient.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, "prebuilt-read", fileUri);
            AnalyzeResult result = operation.Value;

            var content = "";
            bool contentFound = false;
            var taskList = new List<Task>();
            var docContent = new Dictionary<string, string>();

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

               if (!string.IsNullOrEmpty(content))
               {
                  log.LogInformation("content = " + content);
                  taskList.Add(WriteAnalysisContentToBlob(name, page.PageNumber, content, log));
                  docContent.Add(GetFileName(name, page.PageNumber), content);
               }
               content = "";
            }

            //Otherwise, split by collected paragraphs
            content = "";
            if (!contentFound && result.Paragraphs != null)
            {
               var counter = 0;
               foreach (DocumentParagraph paragraph in result.Paragraphs)
               {

                  if (paragraph != null && !string.IsNullOrWhiteSpace(paragraph.Content))
                  {
                     if (content.Length + paragraph.Content.Length < 4000)
                     {
                        content += paragraph.Content;
                     }
                     else
                     {
                        taskList.Add(WriteAnalysisContentToBlob(name, counter, content, log));
                        docContent.Add(GetFileName(name, counter), content);
                        counter++;

                        content = paragraph.Content;
                     }
                  }

               }

               //Add the last paragraph
               taskList.Add(WriteAnalysisContentToBlob(name, counter, content, log));
               docContent.Add(GetFileName(name, counter), content);
            }
            taskList.Add(semanticMemory.StoreMemoryAsync(memoryCollectionName, docContent));
            taskList.Add(semanticMemory.StoreMemoryAsync("general", docContent));

            Task.WaitAll(taskList.ToArray());
         }
         catch (Exception ex)
         {
            log.LogError(ex.Message);
         }



      }
      private string GetFileName(string name, int counter)
      {
         string nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
         string newName = nameWithoutExtension.Replace(".", "_");
         newName += $"_{counter.ToString().PadLeft(4, '0')}.json";
         return newName;
      }

      private async Task<bool> WriteAnalysisContentToBlob(string name, int counter, string content, ILogger log)
      {
         try
         {
            string newName = GetFileName(name, counter);
            string blobName = Path.GetFileNameWithoutExtension(name) + "/" + newName;

            var jsonObj = new ProcessedFile
            {
               FileName = name,
               BlobName = blobName,
               Content = content
            };
            string jsonStr = JsonConvert.SerializeObject(jsonObj);
            // Save the JSON string to Azure Blob Storage  
            string connectionString = config["StorageConnectionString"] ?? throw new ArgumentException("Missing StorageConnectionString in configuration.");
            string containerName = config["ExtractedContainerName"] ?? throw new ArgumentException("Missing ExtractedContainerName in configuration.");

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
