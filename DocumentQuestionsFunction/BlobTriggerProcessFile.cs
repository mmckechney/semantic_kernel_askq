using Azure;
using Azure.AI.DocumentIntelligence;
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
using Azure.Identity;
namespace DocumentQuestions.Function
{
   public class BlobTriggerProcessFile
   {
      private SemanticUtility semanticMemory;
      private ILoggerFactory logFactory;
      private ILogger<BlobTriggerProcessFile> log;
      private IConfiguration config;
      private DocumentIntelligenceClient docIntelClient;
      private Common common;
      public BlobTriggerProcessFile(ILoggerFactory logFactory, IConfiguration config, SemanticUtility semanticMemory, DocumentIntelligenceClient docIntelClient, Common common)
      {
         this.semanticMemory = semanticMemory;
         this.logFactory = logFactory;
         log = logFactory.CreateLogger<BlobTriggerProcessFile>();
         this.config = config;
         this.docIntelClient = docIntelClient;
         this.common = common;
      }

      [Function("BlobTriggerProcessFile")]
      public async Task RunAsync([BlobTrigger("raw/{name}", Connection = "STORAGE_ACCOUNT_BLOB_URL")] Stream myBlob, string name)
      {
         try
         {
            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name}");
            string subscriptionKey = config[Constants.DOCUMENTINTELLIGENCE_KEY] ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_KEY} in configuration.");
            string endpoint = config[Constants.DOCUMENTINTELLIGENCE_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_ENDPOINT} in configuration.");
            string storageAccountName = config[Constants.STORAGE_ACCOUNT_NAME] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_NAME} in configuration.");
            string memoryCollectionName = Path.GetFileNameWithoutExtension(name);

            log.LogInformation($"subkey =  {subscriptionKey}");
            log.LogInformation($"endpoint =  {endpoint}");

            semanticMemory.InitMemoryAndKernel();

            string imgUrl = $"https://{storageAccountName}.blob.core.windows.net/raw/{name}";

            log.LogInformation(imgUrl);

            Uri fileUri = new Uri(imgUrl);

            log.LogInformation("About to get data from document intelligence module.");
            Operation<AnalyzeResult> operation = await docIntelClient.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", fileUri);
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
                  taskList.Add(common.WriteAnalysisContentToBlob(name, page.PageNumber, content, log));
                  docContent.Add(common.GetFileName(name, page.PageNumber), content);
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
                        taskList.Add(common.WriteAnalysisContentToBlob(name, counter, content, log));
                        docContent.Add(common.GetFileName(name, counter), content);
                        counter++;

                        content = paragraph.Content;
                     }
                  }

               }

               //Add the last paragraph
               taskList.Add(common.WriteAnalysisContentToBlob(name, counter, content, log));
               docContent.Add(common.GetFileName(name, counter), content);
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
     

   }
}
