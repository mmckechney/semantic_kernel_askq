using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Memory;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DocumentQuestions.Function
{
#pragma warning disable SKEXP0003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

   public class HttpTriggerSemanticKernelAskQuestion
   {
      private SemanticUtility semanticUtility;
      ILogger<HttpTriggerAskAboutADoc> log;
      IConfiguration config;
      Common common;
      AzureOpenAiService aiService;
      public HttpTriggerSemanticKernelAskQuestion(ILogger<HttpTriggerAskAboutADoc> log, IConfiguration config, Common common, SemanticUtility semanticMemory, AzureOpenAiService aiService)
      {
         this.log = log;
         this.config = config;
         this.common = common;
         this.semanticUtility = semanticMemory;
         this.aiService = aiService;
      }


      //function you can call to ask a question about a document.
      [Function("HttpTriggerSemanticKernelAskQuestion")]
      public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestData req)
      {
         log.LogInformation("C# HTTP trigger function processed a request for HttpTriggerSemanticKernelAskQuestion.");

         semanticUtility.InitMemoryAndKernel();

         try
         {
            (string filename, string question) = await common.GetFilenameAndQuery(req);
            var memories = await semanticUtility.SearchMemoryAsync(filename, question);
            string content = "";
            await foreach (MemoryQueryResult memoryResult in memories)
            {
               log.LogDebug("Memory Result = " + memoryResult.Metadata.Description);
               if (filename != memoryResult.Metadata.Id.Substring(0, memoryResult.Metadata.Id.LastIndexOf('_')))
               {
                  filename = memoryResult.Metadata.Id.Substring(0, memoryResult.Metadata.Id.LastIndexOf('_'));
                  content += $"\nDocument Name: {filename}\n";
               }
               content += memoryResult.Metadata.Description;
            };
            //Invoke Semantic Kernel to get answer
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

