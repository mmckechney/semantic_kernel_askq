using Azure.AI.Agents.Persistent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Runtime.InteropServices;
using System.Text;

namespace DocumentQuestions.Library
{
   public static class LocalToolsExtensions
   {
      private static ILogger log = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("LocalToolsExtensions");

      public static async IAsyncEnumerable<(string text, ThreadRun threadRun)> RunStreamingAsyncWithLocalTools(
         this AIAgent agent,
         LocalToolsUtility localToolUtility,
         PersistentAgentsClient agentsClient,
         string userMessage,
         ThreadRun existingThreadRun = null)
      {
         var agentId = agent.Id;
         log.LogDebug($"Message: {userMessage}");
         log.LogDebug($"--- Agent Response (Streaming) ---");

         // Create thread and add user message
         PersistentAgentThread thread;
         ThreadRun currentRun;

         if (existingThreadRun != null)
         {
            // Use existing thread
            thread = agentsClient.Threads.GetThread(existingThreadRun.ThreadId).Value;
            // existingThreadRun = await agentsClient.Runs.GetRunAsync(existingThreadRun.ThreadId, existingThreadRun.Id);
            currentRun = existingThreadRun;
         }
         else
         {
            // Create new thread and run
            var threadAndRun = agentsClient.CreateThreadAndRun(agent.Id, new ThreadAndRunOptions());
            currentRun = threadAndRun.Value;
            thread = agentsClient.Threads.GetThread(currentRun.ThreadId).Value;
            // Add the user message to the new thread
            agentsClient.Messages.CreateMessage(threadId: thread.Id, role: MessageRole.User, content: userMessage);
            // Create a new run for this message
            currentRun = await agentsClient.Runs.CreateRunAsync(thread.Id, agent.Id);
         }

         log.LogDebug($"Thread ID: {thread.Id}, Run ID: {currentRun.Id}");


         List<RunStatus> terminalStatuses = [RunStatus.Cancelled, RunStatus.Completed, RunStatus.Failed, RunStatus.Expired];

         do
         {
            await Task.Delay(500); // Poll interval
            currentRun = await agentsClient.Runs.GetRunAsync(currentRun.ThreadId, currentRun.Id);
            log.LogDebug($"Run Status: {currentRun.Status}");

            // Check if we need to process tool calls
            if (currentRun.Status == RunStatus.RequiresAction &&
                currentRun.RequiredAction is SubmitToolOutputsAction submitToolOutputsAction)
            {
               log.LogDebug("Run requires action - processing function calls...");
               List<ToolOutput> toolOutputs = new List<ToolOutput>();

               foreach (RequiredToolCall toolCall in submitToolOutputsAction.ToolCalls)
               {
                  if (toolCall is RequiredFunctionToolCall functionToolCall)
                  {
                     log.LogDebug($"Processing tool call: {functionToolCall.Name}");
                     log.LogDebug($"Arguments: {functionToolCall.Arguments}");

                     try
                     {
                        // Execute local tool
                        string toolResult = await localToolUtility.ExecuteToolCallAsync(
                           functionToolCall.Name,
                           functionToolCall.Arguments ?? "{}");

                        toolOutputs.Add(new ToolOutput(toolCall, toolResult));
                        log.LogDebug($"✓ Executed {functionToolCall.Name} successfully");
                        log.LogDebug($"Result: {toolResult}");
                     }
                     catch (Exception ex)
                     {
                        log.LogError($"❌ Error executing tool {functionToolCall.Name}: {ex.Message}");
                        string errorResult = $"Error: {ex.Message}";
                        toolOutputs.Add(new ToolOutput(toolCall, errorResult));
                     }
                  }
               }

               if (toolOutputs.Count > 0)
               {
                  // Submit tool outputs and continue processing
                  currentRun = await agentsClient.Runs.SubmitToolOutputsToRunAsync(currentRun, toolOutputs);
                  log.LogDebug($"Submitted tool outputs, new status: {currentRun.Status}");
                  //continueProcessing = true; // Continue the loop to process the response
               }
            }


            // Get agent response from this loop
            await foreach (var msg in agentsClient.Messages.GetMessagesAsync(threadId: currentRun.ThreadId, order: ListSortOrder.Descending))
            {
               // Get the first assistant message (most recent)
               //if (msg.Role == MessageRole.Agent)
               //{
               foreach (var content in msg.ContentItems)
               {
                  if (content is MessageTextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                  {
                     // Yield the text to caller
                     yield return (textContent.Text, currentRun);
                  }
               }
               //break; // Only get the most recent assistant message
               //}
            }


         } while (!terminalStatuses.Contains(currentRun.Status));

         log.LogInformation($"{Environment.NewLine}Final Run Status: {currentRun.Status}");
      }
   }
}
