using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DocumentQuestions.Library
{
  
   public class DocumentIntelligence
   {
      private DocumentAnalysisClient documentAnalysisClient;
      private ILoggerFactory logFactory;
      private ILogger<DocumentIntelligence> log;
      private IConfiguration config;
      private SemanticUtility semanticUtility;

      public DocumentIntelligence(ILogger<DocumentIntelligence> log, IConfiguration config, SemanticUtility semanticUtility, DocumentAnalysisClient documentAnalysisClient)
      {
         this.log = log;
         this.config = config;
         this.documentAnalysisClient = documentAnalysisClient;
         this.semanticUtility = semanticUtility;
      }

      public async Task ProcessDocument(FileInfo file)
      {
         log.LogInformation($"Processing file {file.FullName} with Document Intelligence Service...");
         AnalyzeDocumentOperation operation;
         using (FileStream stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read))
         {
            operation = await documentAnalysisClient.AnalyzeDocumentAsync(Azure.WaitUntil.Completed, "prebuilt-read", stream);
         }
         AnalyzeResult result = operation.Value;
         if (result != null)
         {
            log.LogInformation($"Parsing Document Intelligence results...");
            var contents = SplitDocumentIntoPagesAndParagraphs(result, file.Name);
            var taskList = new List<Task>();
            string memoryCollectionName = Path.GetFileNameWithoutExtension(file.Name);
            log.LogInformation($"Saving Document Intelligence results to Azure AI Search Index...");
            taskList.Add(semanticUtility.StoreMemoryAsync(memoryCollectionName, contents));
            taskList.Add(semanticUtility.StoreMemoryAsync("general", contents));
            Task.WaitAll(taskList.ToArray());
         }
         log.LogInformation("Document Processed and Indexed");

      }
      private Dictionary<string,string> SplitDocumentIntoPagesAndParagraphs(AnalyzeResult result, string fileName)
      {
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
               log.LogDebug("content = " + content);
               //taskList.Add(WriteAnalysisContentToBlob(name, page.PageNumber, content, log));
               docContent.Add(GetFileName(fileName, page.PageNumber), content);
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
                     //taskList.Add(WriteAnalysisContentToBlob(name, counter, content, log));
                     docContent.Add(GetFileName(fileName, counter), content);
                     counter++;

                     content = paragraph.Content;
                  }
               }

            }

            //Add the last paragraph
            //taskList.Add(WriteAnalysisContentToBlob(name, counter, content, log));
            docContent.Add(GetFileName(fileName, counter), content);
         }

         return docContent;
      }

      private string GetFileName(string name, int counter)
      {
         string nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
         string newName = nameWithoutExtension.Replace(".", "_");
         newName += $"_{counter.ToString().PadLeft(4, '0')}.json";
         return newName;
      }
   }
}
