using Azure.AI.Agents.Persistent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Runtime.InteropServices;
namespace DocumentQuestions.Library
{
   public static class LocalToolsExtensions
   {
      private static ILogger log = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("LocalToolsExtensions");
      public static async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsyncWithLocalTools(this AIAgent agent, LocalToolsUtility localToolUtility, PersistentAgentsClient agentsClient, string message)
      {
         var agentId = agent.Id;
         //log.LogInformation($"✓ Created agent: {agent.Name} (ID: {agentId}) with {agent..Count()} tools\n");

         log.LogInformation($"Message: {message}");
         log.LogInformation($"--- Agent Response ---");

         // Create thread and run
         PersistentAgentThread? thread = await agentsClient.Threads.CreateThreadAsync();
         log.LogInformation($"Created thread, ID: {thread.Id}");

         var messageResponse = agentsClient.Messages.CreateMessage(threadId: thread.Id, role: MessageRole.User, content: message);
         ThreadRun threadRun = await agentsClient.Runs.CreateRunAsync(thread.Id, agent.Id);
         log.LogInformation($"ThreadRun Status: {threadRun.Status}");
         // Process with generic tool handling
         List<RunStatus> activeStatus = [RunStatus.Cancelled, RunStatus.Completed, RunStatus.Failed];
         while (!activeStatus.Contains(threadRun.Status))
         {
            await Task.Delay(100);
            threadRun = await agentsClient.Runs.GetRunAsync(threadRun.ThreadId, threadRun.Id);
            log.LogInformation($"ThreadRun Status: {threadRun.Status}");
            if (threadRun.Status == RunStatus.RequiresAction && threadRun.RequiredAction is SubmitToolOutputsAction submitToolOutputsAction)
            {
               log.LogInformation("Run requires action - processing function calls...");
               List<ToolOutput> toolOutputs = new List<ToolOutput>();

               foreach (RequiredToolCall toolCall in submitToolOutputsAction.ToolCalls)
               {
                  if (toolCall is RequiredFunctionToolCall functionToolCall)
                  {
                     log.LogInformation($"Processing tool call: {functionToolCall.Name}");
                     log.LogInformation($"Arguments: {functionToolCall.Arguments}");

                     try
                     {
                        // Use the generic tool execution method - works for ANY discovered tool
                        string toolResult = await localToolUtility.ExecuteToolCallAsync(functionToolCall.Name, functionToolCall.Arguments ?? "{}");
                        toolOutputs.Add(new ToolOutput(toolCall, toolResult));
                        log.LogInformation($"✓ Executed {functionToolCall.Name} successfully");
                        log.LogInformation($"Result: {toolResult}");
                     }
                     catch (Exception ex)
                     {
                        log.LogInformation($"❌ Error executing tool {functionToolCall.Name}: {ex.Message}");
                        string errorResult = $"Error: {ex.Message}";
                        toolOutputs.Add(new ToolOutput(toolCall, errorResult));
                     }
                  }
               }

               if (toolOutputs.Count > 0)
               {
                  threadRun = await agentsClient.Runs.SubmitToolOutputsToRunAsync(threadRun, toolOutputs);
                  log.LogInformation("Submitted tool outputs");
               }
            }
            else
            {

            }
         }
         log.LogInformation($"Final ThreadRun Status: {threadRun.Status}");
      }

   }
}
