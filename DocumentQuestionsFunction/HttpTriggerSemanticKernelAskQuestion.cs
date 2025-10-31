using DocumentQuestions.Library;
using DocumentQuestions.Library.Models;
using Microsoft.Agents.AI;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DocumentQuestions.Function
{

   public class HttpTriggerSemanticKernelAskQuestion
   {
      private AgentUtility semanticUtility;
      ILogger<HttpTriggerSemanticKernelAskQuestion> log;
      IConfiguration config;
      Helper common;
      
      // Static dictionary to store threads per session (in production, use Redis/Cosmos DB)
      private static readonly ConcurrentDictionary<string, AgentThread> _sessionThreads = new();

      public HttpTriggerSemanticKernelAskQuestion(ILogger<HttpTriggerSemanticKernelAskQuestion> log, IConfiguration config, Helper common, AgentUtility semanticMemory)
      {
         this.log = log;
         this.config = config;
         this.common = common;
         semanticUtility = semanticMemory;
      }


      //function you can call to ask a question about a document.
      [Function("HttpTriggerSemanticKernelAskQuestion")]
      public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestData req)
      {
         log.LogInformation("C# HTTP trigger function processed a request for HttpTriggerSemanticKernelAskQuestion.");

         semanticUtility.InitMemoryAndAgents();

         try
         {
            (string filename, string question) = await common.GetFilenameAndQuery(req);

            // Get session ID from query string or header (for multi-turn conversations)
            req.Headers.TryGetValues("X-Session-Id", out var headerValues);
            string sessionId = req.Query["sessionId"] ?? headerValues.FirstOrDefault() ?? Guid.NewGuid().ToString();

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
            
            // Get or create thread for this session
            AgentThread? thread = _sessionThreads.GetValueOrDefault(sessionId);
            
            //Invoke Agent Framework to get answer with thread context
            var (responseMessage, updatedThread) = await semanticUtility.AskQuestionWithThread(question, content, thread);
            
            // Store updated thread
            _sessionThreads[sessionId] = updatedThread;
            
            // Create response with session ID
            var responseObj = new
            {
               answer = responseMessage,
               sessionId = sessionId,
               turnCount = updatedThread.Serialize().GetProperty("messages").GetArrayLength() / 2
            };
            
            var resp = req.CreateResponse(System.Net.HttpStatusCode.OK);
            resp.Headers.Add("Content-Type", "application/json");
            resp.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseObj)));

            return resp;
         }
         catch (Exception ex)
         {
            log.LogError(ex, "Error processing question");
            var resp = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            resp.Body = new MemoryStream(Encoding.UTF8.GetBytes(ex.Message));
            return resp;
         }
      }



   }
}