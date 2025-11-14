using Azure;
using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace DocumentQuestions.Library
{

   public class LocalFunctionTools
   {
      private readonly AIProjectClient _projectClient;
      private readonly PersistentAgentsClient _agentsClient;
      private readonly LocalToolsUtility localToolUtility;

      public LocalFunctionTools(string projectEndpoint, LocalToolsUtility toolsUtility, LocalToolsLibrary toolsLibrary, Azure.Core.TokenCredential? credential = null)
      {
         credential ??= new DefaultAzureCredential();
         _projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);
         _agentsClient = _projectClient.GetPersistentAgentsClient();
         this.localToolUtility = toolsUtility;
         this.localToolUtility.RegisterLocalToolMethods(toolsLibrary);

      }


      /// <summary>
      /// Creates an agent with function calling capabilities
      /// </summary>
      public async Task<AIAgent> CreateAgentWithToolsAsync(string model, string name, string instructions, params ToolDefinition[] tools)
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
      /// Simplified test method that demonstrates manual tool call handling.
      /// This uses the ProcessToolCall method to handle tools locally.
      /// </summary>
      public async Task QuickTestAsync(string model = "gpt-4o")
      {
         var tools = localToolUtility.GetRegisterLocalToolDefinitions().ToArray();

         // Create agent with all discovered tools (not just weather)
         var agent = await CreateAgentWithToolsAsync(
             model: model,
             name: "GenericToolAgent",
             instructions: "You are a helpful assistant with access to various tools. Use the appropriate tools to answer one or more user questions.",
             tools: tools
         );


         PersistentAgentThread? activeThread = null;
         StringBuilder responseBuilder = new();
         var question = "What's the weather like in Seattle?";
         Console.WriteLine($"============={Environment.NewLine}");
         await foreach (var (text, thread) in agent.RunStreamingAsyncWithLocalTools(localToolUtility, _agentsClient, question, activeThread))
         {
            Console.WriteLine(text);
            activeThread = thread; // Update thread for next question
         }
         Console.WriteLine($"============={Environment.NewLine}");

         question = "What is 145 * 2?";
         Console.WriteLine($"============={Environment.NewLine}");
         await foreach (var (text, thread) in agent.RunStreamingAsyncWithLocalTools(localToolUtility, _agentsClient, question, activeThread))
         {
            Console.WriteLine(text);
            activeThread = thread; // Update thread for next question
         }
         Console.WriteLine($"============={Environment.NewLine}");

         question = "What time is it?";
         Console.WriteLine($"============={Environment.NewLine}");
         await foreach (var (text, thread) in agent.RunStreamingAsyncWithLocalTools(localToolUtility, _agentsClient, question, activeThread))
         {
            Console.WriteLine(text);
            activeThread = thread; // Update thread for next question
         }
         Console.WriteLine($"============={Environment.NewLine}");

         question = "Can you tell me what's the weather in Seattle? Also, what is 15 + 27?";
         Console.WriteLine($"============={Environment.NewLine}");
         await foreach (var (text, thread) in agent.RunStreamingAsyncWithLocalTools(localToolUtility, _agentsClient, question, activeThread))
         {
            Console.WriteLine(text);
            activeThread = thread; // Update thread for next question
         }
         Console.WriteLine($"============={Environment.NewLine}");

         await DeleteAgentAsync(agent.Id);
      }
   }
}