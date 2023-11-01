using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Memory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DocumentQuestions.Function
{

    public class HttpTriggerSemanticKernelAskQuestion
    {
        private SemanticMemory semanticMemory;
        ILogger<HttpTriggerAskAboutADoc> log;
        IConfiguration config;
        Common common;
        AzureOpenAiService aiService;
        public HttpTriggerSemanticKernelAskQuestion(ILogger<HttpTriggerAskAboutADoc> log, IConfiguration config, Common common, SemanticMemory semanticMemory, AzureOpenAiService aiService)
        {
            this.log = log;
            this.config = config;
            this.common = common;
            this.semanticMemory = semanticMemory;
            this.aiService = aiService;
        }


        //function you can call to ask a question about a document.
        [Function("HttpTriggerSemanticKernelAskQuestion")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestData req)
        {
            log.LogInformation("C# HTTP trigger function processed a request for HttpTriggerSemanticKernelAskQuestion.");

            semanticMemory.InitMemory();

            try
            {
                (string filename, string question) = await common.GetFilenameAndQuery(req);
                var memories = await semanticMemory.SearchMemoryAsync(filename, question);

                //pass relevant memories to OpenAI - this will reduce the tokens for a prompt.
                var responseMessage = await aiService.AskOpenAIAsync(question, memories);
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

