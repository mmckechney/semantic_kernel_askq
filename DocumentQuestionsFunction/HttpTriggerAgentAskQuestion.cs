using DocumentQuestions.Library;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

#nullable enable

namespace DocumentQuestions.Function
{
   public class HttpTriggerAgentAskQuestion
   {
      private readonly AgentUtility semanticUtility;
      private readonly ILogger<HttpTriggerAgentAskQuestion> log;
      private readonly IConfiguration config;
      private readonly Helper common;

      public HttpTriggerAgentAskQuestion(ILogger<HttpTriggerAgentAskQuestion> log, IConfiguration config, Helper common, AgentUtility semanticMemory)
      {
         this.log = log;
         this.config = config;
         this.common = common;
         semanticUtility = semanticMemory;
      }


      //function you can call to ask a question about a document.
      [Function("HttpTriggerAgentAskQuestion")]
      public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestData req)
      {
         log.LogInformation("C# HTTP trigger function processed a request for HttpTriggerAgentAskQuestion.");

         semanticUtility.ReloadAgentResources();

         try
         {
            (string filename, string question) = await common.GetFilenameAndQuery(req);
            var memories = await semanticUtility.SearchMemoryAsync(filename, question);
            var contentBuilder = new StringBuilder();
            string? currentDocument = null;

            foreach (var memoryResult in memories)
            {
               if (string.IsNullOrWhiteSpace(memoryResult.Content))
               {
                  continue;
               }

               log.LogDebug("Memory Result = {Result}", memoryResult.Content);

               var resultDocument = memoryResult.FileName;
               if (!string.IsNullOrWhiteSpace(resultDocument) && !string.Equals(currentDocument, resultDocument, StringComparison.OrdinalIgnoreCase))
               {
                  currentDocument = resultDocument;
                  contentBuilder.AppendLine($"\nDocument Name: {currentDocument}\n");
               }

               contentBuilder.AppendLine(memoryResult.Content);
            }

            var content = contentBuilder.ToString();
            var responseMessage = await semanticUtility.AskQuestion(question, content);
            var resp = req.CreateResponse(System.Net.HttpStatusCode.OK);
            resp.Body = new MemoryStream(Encoding.UTF8.GetBytes(responseMessage));

            return resp;
         }
         catch (Exception ex)
         {
            var resp = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            resp.Body = new MemoryStream(Encoding.UTF8.GetBytes(ex.Message));
            return resp;
         }


      }



   }
}

