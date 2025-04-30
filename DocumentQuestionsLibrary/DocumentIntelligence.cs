using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel.Text;
using YamlDotNet.Serialization;

namespace DocumentQuestions.Library
{

   public class DocumentIntelligence
   {
      public static List<string> ModelList =
            [ "prebuild-layout", 
               "prebuilt-read",
               "prebuilt-mortgage.us.1003",
               "prebuilt-mortgage.us.1004",
               "prebuilt-mortgage.us.1005",
               "prebuilt-mortgage.us.1008",
               "prebuilt-mortgage.us.closingDisclosure",
               "prebuilt-tax.us",
               "prebuilt-idDocument"
           ];

      private DocumentIntelligenceClient docIntelClient;
      //private ILoggerFactory logFactory;
      private ILogger<DocumentIntelligence> log;
      private IConfiguration config;
      private SemanticUtility semanticUtility;
      private Common common;

      public DocumentIntelligence(ILogger<DocumentIntelligence> log, IConfiguration config, SemanticUtility semanticUtility, Common common)
      {
         this.log = log;
         this.config = config;
         this.semanticUtility = semanticUtility;
         this.common = common;

         try
         {
            var endpoint = config.GetValue<Uri>(Constants.DOCUMENTINTELLIGENCE_ENDPOINT) ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_ENDPOINT} in configuration");
            var key = config.GetValue<string>(Constants.DOCUMENTINTELLIGENCE_KEY) ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_KEY} in configuration");
            this.docIntelClient = new DocumentIntelligenceClient(endpoint, new AzureKeyCredential(key));
         }catch(Exception exe)
         {
            log.LogError(exe.ToString() );
         }
      }


      public async Task ProcessDocument(Uri fileUri, string modelId = "prebuilt-layout", string indexName = "")
      {
         //log.LogInformation($"Processing file {file.FullName} with Document Intelligence Service...");
         Operation<AnalyzeResult> operation;

            log.LogInformation($"Analyzing document with model ID: {modelId} ");
            AnalyzeDocumentOptions opts = new AnalyzeDocumentOptions(modelId: modelId, uriSource: fileUri)
            {
               OutputContentFormat = DocumentContentFormat.Markdown
            };
            operation = await docIntelClient.AnalyzeDocumentAsync(Azure.WaitUntil.Completed, opts);
         AnalyzeResult result = operation.Value;
         await ProcessDocumentResults(result, fileUri.AbsoluteUri, indexName);
      }

      public async Task ProcessDocument(FileInfo file, string modelId = "prebuilt-layout", string indexName = "")
      {
         //log.LogInformation($"Processing file {file.FullName} with Document Intelligence Service...");
         Operation<AnalyzeResult> operation;

         using (FileStream stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read))
         {
            log.LogInformation($"Analyzing document with model ID: {modelId} ");
            BinaryData binaryDoc = BinaryData.FromStream(stream);
            AnalyzeDocumentOptions opts = new AnalyzeDocumentOptions(modelId: modelId, bytesSource: binaryDoc)
            {
               OutputContentFormat = DocumentContentFormat.Markdown
            };
            operation = await docIntelClient.AnalyzeDocumentAsync(Azure.WaitUntil.Completed, opts);
         }
         AnalyzeResult result = operation.Value;

         await ProcessDocumentResults(result, file.FullName, indexName);
      }

      public async Task ProcessDocumentResults(AnalyzeResult result, string filePathOrUrl, string indexName)
      {

         indexName = Common.SafeIndexName(filePathOrUrl, indexName);

         if (result != null)
         {
            string content = result.Content;
            var contentLines = content.Split(Environment.NewLine).ToList();
           

            log.LogInformation($"Writing document Markdown to bloc...");
            await common.WriteAnalysisContentToBlob(indexName,result.Content, log);
            log.LogInformation($"Parsing Document Intelligence results...");
            var chunked = TextChunker.SplitPlainTextParagraphs(contentLines, 8191);
            var taskList = new List<Task>();

            log.LogInformation($"Saving Document Intelligence results to Azure AI Search Index...");
            taskList.Add(semanticUtility.StoreMemoryAsync(indexName, Common.BaseFileName(filePathOrUrl), chunked));
            taskList.Add(semanticUtility.StoreMemoryAsync("general", Common.BaseFileName(filePathOrUrl), chunked));
            Task.WaitAll(taskList.ToArray());
         }
         log.LogInformation("Document Processed and Indexed");

      }
   
      //private Dictionary<string, string> SplitDocumentIntoPagesAndParagraphs(AnalyzeResult result, string fileName)
      //{
      //   var content = "";
      //   bool contentFound = false;
      //   var taskList = new List<Task>();
      //   var docContent = new Dictionary<string, string>();

      //   //Split by page if there is content...
      //   log.LogInformation("Checking document data...");
      //   foreach (DocumentPage page in result.Pages)
      //   {

      //      for (int i = 0; i < page.Lines.Count; i++)
      //      {
      //         DocumentLine line = page.Lines[i];
      //         log.LogDebug($"  Line {i} has content: '{line.Content}'.");
      //         content += line.Content.ToString();
      //         contentFound = true;
      //      }

      //      if (!string.IsNullOrEmpty(content))
      //      {
      //         log.LogDebug("content = " + content);
      //         taskList.Add(common.WriteAnalysisContentToBlob(fileName, page.PageNumber, content, log));
      //         docContent.Add(GetFileName(fileName, page.PageNumber), content);
      //      }
      //      content = "";
      //   }

      //   //Otherwise, split by collected paragraphs
      //   content = "";
      //   if (!contentFound && result.Paragraphs != null)
      //   {
      //      var counter = 0;
      //      foreach (DocumentParagraph paragraph in result.Paragraphs)
      //      {

      //         if (paragraph != null && !string.IsNullOrWhiteSpace(paragraph.Content))
      //         {
      //            if (content.Length + paragraph.Content.Length < 4000)
      //            {
      //               content += paragraph.Content + Environment.NewLine;
      //            }
      //            else
      //            {
      //               taskList.Add(common.WriteAnalysisContentToBlob(fileName, counter, content, log));
      //               docContent.Add(GetFileName(fileName, counter), content);
      //               counter++;

      //               content = paragraph.Content + Environment.NewLine;
      //            }
      //         }

      //      }

      //      //Add the last paragraph
      //      taskList.Add(common.WriteAnalysisContentToBlob(fileName, counter, content, log));
      //      docContent.Add(GetFileName(fileName, counter), content);
      //   }

      //   return docContent;
      //}

      private string GetFileName(string name, int counter)
      {
         string nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
         string newName = nameWithoutExtension.Replace(".", "_");
         newName += $"_{counter.ToString().PadLeft(4, '0')}.json";
         return newName;
      }
   }
}
