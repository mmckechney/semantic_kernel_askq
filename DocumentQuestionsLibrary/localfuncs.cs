using Azure;
using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Text.Json;
using System.Threading;
using System.Xml;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;

namespace DocumentQuestions.Library
{
   /// <summary>
   /// Utility class demonstrating local function tool usage with Persistent Agents.
   /// 
   /// Usage Example:
   /// <code>
   /// // Set up environment
   /// Environment.SetEnvironmentVariable("AIFOUNDRY_ENDPOINT", "https://your-project.region.models.ai.azure.com");
   /// 
   /// // Create the tools helper
   /// var tools = new LocalFunctionTools(
   ///     projectEndpoint: Environment.GetEnvironmentVariable("AIFOUNDRY_ENDPOINT")!
   /// );
   /// 
   /// // Run a quick test
   /// var response = await tools.QuickTestAsync("gpt-4o-mini");
   /// Console.WriteLine($"Response: {response}");
   /// 
   /// // Or test with a custom question
   /// var customResponse = await tools.TestWeatherAgentAsync(
   ///     model: "gpt-4o-mini",
   ///     question: "What's the weather in New York in metric units?"
   /// );
   /// </code>
   /// </summary>
   public class LocalFunctionTools
   {
      private readonly AIProjectClient _projectClient;
      private readonly PersistentAgentsClient _agentsClient;
      private readonly LocalToolsUtility localToolUtility;

      public LocalFunctionTools(string projectEndpoint, LocalToolsUtility toolsUtility, Azure.Core.TokenCredential? credential = null)
      {
         credential ??= new DefaultAzureCredential();
         _projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);
         _agentsClient = _projectClient.GetPersistentAgentsClient();
         this.localToolUtility = toolsUtility;
      }


      /// <summary>
      /// Creates an agent with function calling capabilities
      /// </summary>
      public async Task<AIAgent> CreateAgentWithToolsAsync(string model, string name, string instructions, params FunctionToolDefinition[] tools)
      {
         var agent = await _agentsClient.CreateAIAgentAsync(
             model: model,
             name: name,
             description: $"Agent: {name}",
             instructions: instructions,
             tools: tools);

         return agent;
      }


      /// <summary>
      /// Deletes a thread
      /// </summary>
      public async Task DeleteThreadAsync(string threadId)
      {
         await _agentsClient.Threads.DeleteThreadAsync(threadId);
      }

      /// <summary>
      /// Deletes an agent
      /// </summary>
      public async Task DeleteAgentAsync(string agentId)
      {
         await _agentsClient.Administration.DeleteAgentAsync(agentId);
      }

      /// <summary>
      /// Example test method demonstrating the ProcessToolCall method.
      /// This simulates how tool calls would be processed manually by parsing arguments
      /// and executing the local tool function. Note: The Persistent Agents API handles
      /// tool execution automatically, so this demonstrates the ProcessToolCall utility method.
      /// </summary>
      /// <param name="model">The AI model to use (e.g., "gpt-4o")</param>
      /// <param name="question">The question to ask the agent</param>
      /// <returns>The agent's response as a string</returns>
      public async Task<string> TestAgentWithManualToolHandlingAsync(AIAgent agent, string question)
      {
         Console.WriteLine();
         Console.WriteLine();
         Console.WriteLine("=== Testing Generic Tool Execution ===\n");

         // Demonstrate the reflection-based tool execution
         Console.WriteLine("Available tools:");
         var toolDefinitions = localToolUtility.GetAllToolDefinitions();
         foreach (var tool in toolDefinitions)
         {
            Console.WriteLine($"  - {tool.Name}: {tool.Description}");
         }
         Console.WriteLine();

         string agentId = string.Empty;
         PersistentAgentThread? thread = null;

         try
         {


            agentId = agent.Id;
            Console.WriteLine($"✓ Created agent: {agent.Name} (ID: {agentId}) with {toolDefinitions.Count()} tools\n");

            Console.WriteLine($"Question: {question}");
            Console.WriteLine($"--- Agent Response ---");

            // Create thread and run
            thread = await _agentsClient.Threads.CreateThreadAsync();
            Console.WriteLine($"Created thread, ID: {thread.Id}");

            var messageResponse = _agentsClient.Messages.CreateMessage(threadId: thread.Id, role: MessageRole.User, content: question);
            ThreadRun threadRun = await _agentsClient.Runs.CreateRunAsync(thread.Id, agent.Id);
            Console.WriteLine($"ThreadRun Status: {threadRun.Status}");
            // Process with generic tool handling
            List<RunStatus> activeStatus = [RunStatus.Cancelled, RunStatus.Completed, RunStatus.Failed];
            while (!activeStatus.Contains(threadRun.Status))
            {
               await Task.Delay(100);
               threadRun = await _agentsClient.Runs.GetRunAsync(threadRun.ThreadId, threadRun.Id);
               Console.WriteLine($"ThreadRun Status: {threadRun.Status}");
               if (threadRun.Status == RunStatus.RequiresAction
                   && threadRun.RequiredAction is SubmitToolOutputsAction submitToolOutputsAction)
               {
                  Console.WriteLine("Run requires action - processing function calls...");
                  List<ToolOutput> toolOutputs = new List<ToolOutput>();

                  foreach (RequiredToolCall toolCall in submitToolOutputsAction.ToolCalls)
                  {
                     if (toolCall is RequiredFunctionToolCall functionToolCall)
                     {
                        Console.WriteLine($"Processing tool call: {functionToolCall.Name}");
                        Console.WriteLine($"Arguments: {functionToolCall.Arguments}");

                        try
                        {
                           // Use the generic tool execution method - works for ANY discovered tool
                           string toolResult = await localToolUtility.ExecuteToolCallAsync(functionToolCall.Name, functionToolCall.Arguments ?? "{}");
                           toolOutputs.Add(new ToolOutput(toolCall, toolResult));
                           Console.WriteLine($"✓ Executed {functionToolCall.Name} successfully");
                           Console.WriteLine($"Result: {toolResult}");
                        }
                        catch (Exception ex)
                        {
                           Console.WriteLine($"❌ Error executing tool {functionToolCall.Name}: {ex.Message}");
                           string errorResult = $"Error: {ex.Message}";
                           toolOutputs.Add(new ToolOutput(toolCall, errorResult));
                        }
                     }
                  }

                  if (toolOutputs.Count > 0)
                  {
                     threadRun = await _agentsClient.Runs.SubmitToolOutputsToRunAsync(threadRun, toolOutputs);
                     Console.WriteLine("Submitted tool outputs");
                  }
               }
               else
               {
                  
               }
            }
            Console.WriteLine($"Final ThreadRun Status: {threadRun.Status}");
            // Get final response
            Pageable<PersistentThreadMessage> messages = _agentsClient.Messages.GetMessages(
                threadId: thread.Id,
                order: ListSortOrder.Ascending
            );

            string? agentResponse = null;
            foreach (PersistentThreadMessage threadMessage in messages)
            {
               foreach (MessageContent content in threadMessage.ContentItems)
               {
                  if (content is MessageTextContent textItem)
                  {
                     Console.WriteLine($"Role: {threadMessage.Role}, Content: {textItem.Text}");

                     if (threadMessage.Role.ToString().ToLower() == "assistant")
                     {
                        agentResponse = textItem.Text;
                     }
                  }
               }
            }

            if (!string.IsNullOrEmpty(agentResponse))
            {
               Console.WriteLine("\n=== AGENT OUTPUT ===");
               Console.WriteLine(agentResponse);
               Console.WriteLine("==================");
            }

            return agentResponse ?? "No response received";
         }
         catch (Exception ex)
         {
            Console.WriteLine($"❌ Error: {ex}");
            return $"Error: {ex.Message}";
         }
         finally
         {
            //// Cleanup
            //if (!string.IsNullOrEmpty(agentId))
            //{
            //   try
            //   {
            //      await DeleteAgentAsync(agentId);
            //      Console.WriteLine($"\n✓ Deleted agent: {agentId}");
            //   }
            //   catch (Exception ex)
            //   {
            //      Console.WriteLine($"Warning: Failed to delete agent: {ex.Message}");
            //   }
            //}

            //if (thread != null)
            //{
            //   try
            //   {
            //      await DeleteThreadAsync(thread.Id);
            //      Console.WriteLine($"✓ Deleted thread: {thread.Id}");
            //   }
            //   catch (Exception ex)
            //   {
            //      Console.WriteLine($"Warning: Failed to delete thread: {ex.Message}");
            //   }
            //}
         }
      }




      /// <summary>
      /// Simplified test method that demonstrates manual tool call handling.
      /// This uses the ProcessToolCall method to handle tools locally.
      /// </summary>
      public async Task<string> QuickTestAsync(string model = "gpt-4o")
      {
         var tools = localToolUtility.GetAllToolDefinitions().ToArray();
         // Create agent with all discovered tools (not just weather)
         var agent = await CreateAgentWithToolsAsync(
             model: model,
             name: "GenericToolAgent",
             instructions: "You are a helpful assistant with access to various tools. Use the appropriate tools to answer user questions.",
             tools: tools
         );

         StringBuilder sb = new();
         sb.AppendLine(await TestAgentWithManualToolHandlingAsync(
             agent: agent,
             question: "What's the weather like in Seattle?"
         ));

         sb.AppendLine(await TestAgentWithManualToolHandlingAsync(
           agent: agent,
           question: "What is 145 * 2?"
         ));

         sb.AppendLine(await TestAgentWithManualToolHandlingAsync(
           agent: agent,
           question: "What time is it?"
         ));

         sb.AppendLine(await TestAgentWithManualToolHandlingAsync(
            agent: agent,
            question: "Can you tell me what's the weather in Seattle? Also, what is 15 + 27?"
         ));

         return sb.ToString();
      }
   }
}