using DocumentQuestions.Library;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DocumentQuestions.Function
{
   public class HttpTriggerAskAboutADoc
   {
      ILogger<HttpTriggerAskAboutADoc> log;
      IConfiguration config;
      Helper common;
      AzureOpenAiService aiService;
      public HttpTriggerAskAboutADoc(ILogger<HttpTriggerAskAboutADoc> log, IConfiguration config, Helper common, AzureOpenAiService aiService)
      {
         this.log = log;
         this.config = config;
         this.common = common;
         this.aiService = aiService;
      }

      //function you can call to ask a question about a document.
      [Function("HttpTriggerOpenAiSdkAskQuestion")]
      public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestData req)
      {
         log.LogInformation("C# HTTP trigger function processed a request for HttpTriggerOpenAiSdkAskQuestion.");

         try
         {
            (string filename, string question) = await common.GetFilenameAndQuery(req);
            var responseMessage = await aiService.AskOpenAIAsync(filename, question);

            var resp = req.CreateResponse(System.Net.HttpStatusCode.OK);
            resp.Body = new MemoryStream(Encoding.UTF8.GetBytes(responseMessage));

            return resp;
         }
         catch (Exception ex)
         {
            var resp = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            resp.Body = new MemoryStream(Encoding.UTF8.GetBytes(ex.Message));
            return resp; ;
         }


      }



   }
}

