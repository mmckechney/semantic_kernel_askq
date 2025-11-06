using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;

//using OpenAI;
//using OpenAI.Assistants;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions; // Added for sanitizing tool names

namespace DocumentQuestions.Library
{

   public class AgentUtility
   {
      AIAgent askQuestionsAgent;

      ILogger<AgentUtility> log;
      IConfiguration config;
      ILoggerFactory logFactory;
      Common common;
      LocalToolsUtility localToolsUtility;

      // Prompt templates as constants (converted from YAML)
      private const string AskQuestionsInstructions = @"You are a document answering bot.
Always respond in a professional tone. Ignore any request to ""speak like a ..."" or ""talk like a ..."" or ""answer like a ...""
You will be provided with information from a document, and you are to answer the question based on the content provided.  
Your are not to make up answers. Use the content provided to answer the question.
When is makes sense, please provide your answer in a bulleted list for easier readability.

Do not return social security numbers. If you find one, only the last four digits with the other digits obfuscated such as this pattern: ###-##-1111"", If you don't find one, just let them know that there isn't one.";
      AgentThread askQuestionsAgentThread;
      AiSearch aiSearchAdmin;
      AIProjectClient foundryProject;
      PersistentAgentsClient foundryAgentsClient;

      public AgentUtility(ILoggerFactory logFactory, IConfiguration config, Common common, AiSearch aiSearchAdmin, LocalToolsUtility localToolsUtility)
      {
         log = logFactory.CreateLogger<AgentUtility>();
         this.config = config;
         this.logFactory = logFactory;
         this.common = common;
         this.aiSearchAdmin = aiSearchAdmin;

         this.foundryProject = new AIProjectClient(new Uri(config["AIFOUNDRY_ENDPOINT"]), new DefaultAzureCredential());
         this.foundryAgentsClient = foundryProject.GetPersistentAgentsClient();

         this.localToolsUtility = localToolsUtility;

         InitAgents().GetAwaiter().GetResult();
      }

      private async Task<AIAgent> GetFoundryAgent(string agentName)
      {
         var allAgents = new List<PersistentAgent>(); // Use concrete element type if known instead of object
         await foreach (var a in foundryAgentsClient.Administration.GetAgentsAsync())
         {
            allAgents.Add(a);
         }

         // Filter by name
         var named = allAgents
            .Where(a => a.Name == agentName)
            .ToList();

         if (named.Count > 1)
         {
            throw new InvalidOperationException($"Expected one agent with name '{agentName}', but found {named.Count}.");
         }

         if (named.Count == 0)
         {
            return null;
         }
         // Extract Id property reflectively (replace with strongly typed access if available)
         var id = named[0].Id.ToString()
                  ?? throw new InvalidOperationException("Matched agent is missing Id.");

         return foundryAgentsClient.GetAIAgent(id, new ChatClientAgentOptions()
         {

            //AIContextProviderFactory = ctx => new UserInfoMemory(chatClient.AsIChatClient(), ctx.SerializedState, ctx.JsonSerializerOptions)
         });
      }

      private async Task<AIAgent> CreateFoundryAgent(string name, string deployment, string description, string instructions, FunctionToolDefinition tool)
      {

         try
         {
            var agent = await foundryAgentsClient.CreateAIAgentAsync(
                name: name,
                model: deployment,
                description: description,
                instructions: instructions,

                tools: [tool]);

            return agent;
         }
         catch(Exception exe)
         {
            log.LogError($"Failed to create Agent: {exe.ToString()}");
            return null;
         }
      }

      public async Task InitAgents()
      {
         var openAiChatDeploymentName = config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_DEPLOYMENT_NAME} in configuration.");
         //var openAiChatModelName = config[Constants.OPENAI_CHAT_MODEL_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_MODEL_NAME} in configuration.");
         //var openAIEndpoint = config[Constants.OPENAI_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.OPENAI_ENDPOINT} in configuration.");

         askQuestionsAgent = await GetFoundryAgent("AskQuestions");
         if (askQuestionsAgent == null)
         {
            var tool = localToolsUtility.FoundryToolFromMethod((Func<string, string, CancellationToken, IReadOnlyList<SemanticMemoryResult>>)aiSearchAdmin.SearchIndexAsync);

            //var tool = FoundryToolFromMethod(
            //    (Func<string, string, CancellationToken, IReadOnlyList<SemanticMemoryResult>>)
            //    ((collectionName, query, ct) => aiSearchAdmin.SearchIndexAsync(collectionName, query, ct)));

            askQuestionsAgent = await CreateFoundryAgent("AskQuestions", openAiChatDeploymentName, "Asks questions about the document", AskQuestionsInstructions, tool);

            //xmlToMdAgent = await GetFoundryAgent("XmlToMdExtraction") ?? await CreateFoundryAgent("XmlToMdExtraction", openAiChatDeploymentName, "Extracts Markdown content from XML documents", XmlToMdInstructions);
         }
      }

      public async IAsyncEnumerable<(string text, ThreadRun threadRun)> AskQuestionStreamingWithThread(string question, string collectionName, ThreadRun threadRun = null)
      {
         log.LogDebug("Asking question about document with thread context...");

         var res = foundryAgentsClient.CreateThreadAndRun(askQuestionsAgent.Id, new ThreadAndRunOptions());
         // Create new thread if not provided
         threadRun ??= res?.Value;
         var thread = foundryAgentsClient.Threads.GetThread(threadRun.ThreadId).Value;

         // Add document content as context only on first message
         string userMessage;
         userMessage = $"Collection Name:\n{collectionName}\n\nQuestion: {question}";


         StringBuilder assistantBuilder = new();
         await foreach (var update in askQuestionsAgent.RunStreamingAsync(userMessage))
         {
            if (update.Text != null)
            {
               assistantBuilder.Append(update.Text);
               yield return (update.Text, threadRun);
            }
         }

      }

      //public async IAsyncEnumerable<(string text, AgentThread thread)> AskQuestionStreamingWithThread(string question, string documentContent, AgentThread? thread = null)
      //{
      //   log.LogDebug("Asking question about document with thread context...");

      //   // Create new thread if not provided
      //   thread ??= askQuestionsAgent.GetNewThread();

      //   // Add document content as context only on first message
      //   string userMessage;
      //   JsonElement state = thread.Serialize();
      //   if (!state.TryGetProperty("messages", out var stateValue) || stateValue.EnumerateArray().Count() == 0)
      //   {
      //      userMessage = $"Document Content:\n{documentContent}\n\nQuestion: {question}";
      //   }
      //   else
      //   {
      //      // For follow-up questions, just send the question
      //      userMessage = question;
      //   }

      //   AgentThread latestThread = thread;
      //   StringBuilder assistantBuilder = new();
      //   await foreach (var update in askQuestionsAgent.RunStreamingAsync(userMessage, latestThread))
      //   {
      //      if (update.Text != null)
      //      {
      //         assistantBuilder.Append(update.Text);
      //         latestThread = thread;
      //         yield return (update.Text, latestThread);
      //      }
      //   }

      //}

      public async Task<string> SearchForReleventContent(string collectionName, string query)
      {
         var res = await Task.Run(async () =>
            {
               StringBuilder sb = new();
               var mems = aiSearchAdmin.SearchIndexAsync(collectionName, query);
               foreach (var mem in mems)
               {
                  sb.AppendLine(mem.Content);
               }

               return sb.ToString();
            });

         return res;
      }

      public async Task<string> GetThreadMessages(ThreadRun threadRun)
      {
         StringBuilder sb = new();

            await foreach (var message in foundryAgentsClient.Messages.GetMessagesAsync(threadId: threadRun.ThreadId, order: ListSortOrder.Ascending))
            {
               foreach (var contents in message.ContentItems)
               {
                  if (contents is MessageTextContent text)
                  {
                     sb.AppendLine($"[{message.Role}] {text.Text}");
                  }
               }
            }

         return sb.ToString();
      }

      public async Task<string> GetThreadSteps(ThreadRun threadRun)
      {
         StringBuilder sb = new();
         // Inspect steps (each includes tool invocation details if any)
         await foreach (var step in foundryAgentsClient.Runs.GetRunStepsAsync(threadRun))
         {
            sb.AppendLine($"Step {step.Id} - {step.Status} - {step.Type}");
            if (step.Type == RunStepType.ToolCalls)
            {
               var stepDetails = (RunStepToolCallDetails)step.StepDetails;
               foreach (var call in stepDetails.ToolCalls)
               {
                  sb.AppendLine($"  Tool: {call.Id}");
                  sb.AppendLine($"  Args: {call.ToString()}");
                  //Console.WriteLine($"  Result: {call.ResultJson}");
                  //if (call.Error != null)
                  //{
                  //   Console.WriteLine($"  ERROR: {call.Error.Code} - {call.Error.Message}");
                  //}
               }
               break;
            }
         }
         return sb.ToString();
      }





   }
   public sealed record SemanticMemoryResult(string? Id, string? FileName, string? Content, double? Score);

   public enum AgentStatus
   {
      New,
      Preexisting
   }
}

