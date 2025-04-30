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
      //private DocumentIntelligenceClient docIntelClient;
      private Common common;
      private DocumentIntelligence docIntel;
      public BlobTriggerProcessFile(ILoggerFactory logFactory, IConfiguration config, SemanticUtility semanticMemory, DocumentIntelligence docIntel, Common common)
      {
         this.semanticMemory = semanticMemory;
         this.logFactory = logFactory;
         log = logFactory.CreateLogger<BlobTriggerProcessFile>();
         this.config = config;
         this.docIntel = docIntel;
         this.common = common;
      }

      [Function("BlobTriggerProcessFile")]
      public async Task RunAsync([BlobTrigger("raw/{name}", Connection = "STORAGE_ACCOUNT_BLOB_URL")] Stream myBlob, string name)
      {
         try
         {
            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name}");
            string storageAccountName = config[Constants.STORAGE_ACCOUNT_NAME] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_NAME} in configuration.");
            string memoryCollectionName = Path.GetFileNameWithoutExtension(name);


            semanticMemory.InitMemoryAndKernel();

            string imgUrl = $"https://{storageAccountName}.blob.core.windows.net/raw/{name}";

            log.LogInformation(imgUrl);

            Uri fileUri = new Uri(imgUrl);

            log.LogInformation("About to get data from document intelligence module.");
            await docIntel.ProcessDocument(fileUri);
           log.LogInformation($"Document Intelligence processing completed for {name}");
         }
         catch (Exception ex)
         {
            log.LogError(ex.Message);
         }



      }
     

   }
}
