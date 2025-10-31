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

        public LocalFunctionTools(string projectEndpoint, Azure.Core.TokenCredential? credential = null)
        {
            credential ??= new DefaultAzureCredential();
            _projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);
            _agentsClient = _projectClient.GetPersistentAgentsClient();
        }

      [Description("Fetches the weather information for the specified location")]
      private static string FetchWeather([Description("The location to fetch weather for.")] string location)
      {
         // Mock weather data for demonstration purposes
         var mockWeatherData = new Dictionary<string, string>
            {
                { "New York", "Sunny, 25°C" },
                { "London", "Cloudy, 18°C" },
                { "Tokyo", "Rainy, 22°C" },
               { "Seattle", "Rainy, 10°C" }
            };

         string weather = mockWeatherData.TryGetValue(location, out string? weatherInfo)
             ? weatherInfo
             : "Weather data not available for this location.";

         var result = new { weather = weather };
         return JsonSerializer.Serialize(result);
      }
      /// <summary>
      /// Creates a function tool definition for weather lookup
      /// </summary>
      public FunctionToolDefinition CreateWeatherToolDefinition()
        {
         return AgentUtility.FoundryToolFromMethod(FetchWeather);
            return new FunctionToolDefinition(
                name: "get_weather",
                description: "Returns current weather in a city.",
                parameters: BinaryData.FromString("""
                {
                  "type": "object",
                  "properties": {
                    "city":  { "type": "string", "description": "City name" },
                    "units": { "type": "string", "enum": ["imperial","metric"], "default": "imperial" }
                  },
                  "required": ["city"]
                }
                """));
        }

        /// <summary>
        /// Executes the weather tool function locally
        /// </summary>
        public string ExecuteWeatherTool(string arguments)
        {
            using var doc = JsonDocument.Parse(arguments);
            var city = doc.RootElement.GetProperty("city").GetString() ?? "Unknown";
            var units = doc.RootElement.TryGetProperty("units", out var u) ? u.GetString() : "imperial";

            // Simulated local logic (replace with actual DB/API call)
            var sample = units == "metric" ? "12°C, light rain" : "54°F, light rain";
            return $"Weather in {city}: {sample}";
        }

        /// <summary>
        /// Creates an agent with function calling capabilities
        /// </summary>
        public async Task<AIAgent> CreateAgentWithToolsAsync(string model, string name, string instructions, FunctionToolDefinition tool)
        {
            var agent = await _agentsClient.CreateAIAgentAsync(
                model: model,
                name: name,
                description: $"Agent: {name}",
                instructions: instructions,
                tools: new[] { tool });
            
            return agent;
        }

        /// <summary>
        /// Processes a tool call by executing the handler function
        /// </summary>
        public string ProcessToolCall(string functionName, string arguments, Func<string, string, string> toolHandler)
        {
            using var doc = JsonDocument.Parse(arguments);
            
            // Extract parameters based on function name
            switch (functionName)
            {
                case "get_weather":
                    var city = doc.RootElement.GetProperty("city").GetString() ?? "Unknown";
                    var units = doc.RootElement.TryGetProperty("units", out var u) ? u.GetString() : "imperial";
                    return toolHandler(city, units ?? "imperial");
                default:
                    return $"Unknown function: {functionName}";
            }
        }

        /// <summary>
        /// Retrieves all messages from a thread
        /// </summary>
        public async Task<string> GetThreadMessagesAsync(ThreadRun threadRun)
        {
            var sb = new System.Text.StringBuilder();
            await foreach (var message in _agentsClient.Messages.GetMessagesAsync(threadId: threadRun.ThreadId, order: ListSortOrder.Ascending))
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
        /// Creates a new thread
        /// </summary>
        public async Task<PersistentAgentThread> CreateThreadAsync()
        {
            var thread = await _agentsClient.Threads.CreateThreadAsync();
            return thread.Value;
        }

        /// <summary>
        /// Creates a message in a thread
        /// </summary>
        public async Task CreateMessageAsync(string threadId, MessageRole role, string content)
        {
            await _agentsClient.Messages.CreateMessageAsync(threadId, role, content);
        }

        /// <summary>
        /// Creates a thread and run for an agent
        /// </summary>
        public ThreadRun CreateThreadAndRun(string agentId, ThreadAndRunOptions? options = null)
        {
            var result = _agentsClient.CreateThreadAndRun(agentId, options ?? new ThreadAndRunOptions());
            return result.Value;
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
        public async Task<string> TestWeatherAgentWithManualToolHandlingAsync(string model, string question)
        {
            Console.WriteLine("=== Testing ProcessToolCall Method ===\n");
            
            // 1. Demonstrate the ProcessToolCall method with sample tool call data
            Console.WriteLine("Simulating tool call interception and processing...\n");
            
            // Example 1: Weather query in imperial units
            var toolCall1Args = """
                {
                  "city": "Seattle",
                  "units": "imperial"
                }
                """;
            
            Console.WriteLine($"Tool Call 1 - Function: get_weather");
            Console.WriteLine($"Arguments: {toolCall1Args}");
            
            var result1 = ProcessToolCall(
                functionName: "get_weather",
                arguments: toolCall1Args,
                toolHandler: (city, units) =>
                {
                    Console.WriteLine($"→ Executing weather lookup for {city} in {units} units");
                    return ExecuteWeatherTool($"{{\"city\":\"{city}\",\"units\":\"{units}\"}}");
                }
            );
            
            Console.WriteLine($"Result 1: {result1}\n");

            // Example 2: Weather query in metric units
            var toolCall2Args = """
                {
                  "city": "New York",
                  "units": "metric"
                }
                """;
            
            Console.WriteLine($"Tool Call 2 - Function: get_weather");
            Console.WriteLine($"Arguments: {toolCall2Args}");
            
            var result2 = ProcessToolCall(
                functionName: "get_weather",
                arguments: toolCall2Args,
                toolHandler: (city, units) =>
                {
                    Console.WriteLine($"→ Executing weather lookup for {city} in {units} units");
                    return ExecuteWeatherTool($"{{\"city\":\"{city}\",\"units\":\"{units}\"}}");
                }
            );
            
            Console.WriteLine($"Result 2: {result2}\n");

            // 2. Now test with actual agent (uses automatic tool handling)
            Console.WriteLine("=== Now testing with actual AIAgent (automatic tool handling) ===\n");
            
            string agentId = string.Empty;
            try
            {
                var weatherTool = CreateWeatherToolDefinition();
                var agent = await CreateAgentWithToolsAsync(
                    model: model,
                    name: "WeatherBotProcessToolDemo",
                    instructions: "You are a helpful weather assistant. Use the get_weather tool to answer weather questions.",
                    tool: weatherTool
                );

                agentId = agent.Id;
                Console.WriteLine($"✓ Created agent: {agent.Name} (ID: {agentId})\n");

                // Use RunStreamingAsync which handles tool calls automatically
                var fullResponse = new System.Text.StringBuilder();
                Console.WriteLine($"Question: {question}");
                Console.WriteLine($"--- Agent Response ---");


            //var threadRunResponse = _agentsClient.CreateThreadAndRun(agentId, new ThreadAndRunOptions());
            //var threadRun = threadRunResponse.Value;

            PersistentAgentThread thread = await _agentsClient.Threads.CreateThreadAsync();
            Console.WriteLine($"Created thread, ID: {thread.Id}");

            var messageResponse = _agentsClient.Messages.CreateMessage(threadId: thread.Id, role: MessageRole.User, content: question);
            var messageValue = messageResponse.Value;

            ThreadRun threadRun = await _agentsClient.Runs.CreateRunAsync(thread.Id, agent.Id);

            List<RunStatus> activeStatus = [RunStatus.Queued, RunStatus.InProgress, RunStatus.RequiresAction];
            while (activeStatus.Contains(threadRun.Status))
            {
               await Task.Delay(1000); // Wait 1 second before polling again
               threadRun = await _agentsClient.Runs.GetRunAsync(threadRun.ThreadId, threadRun.Id);

               if (threadRun.Status == RunStatus.RequiresAction
                   && threadRun.RequiredAction is SubmitToolOutputsAction submitToolOutputsAction)
               {
                  Console.WriteLine("Run requires action - processing function calls...");

                  List<ToolOutput> toolOutputs = new List<ToolOutput>();

                  foreach (RequiredToolCall toolCall in submitToolOutputsAction.ToolCalls)
                  {
                     if (toolCall is RequiredFunctionToolCall functionToolCall)
                     {
                        if (functionToolCall.Name.ToLower() == "fetchweather")
                        {
                           // Parse the arguments to get the location
                           string location = "New York"; // Default location
                           if (!string.IsNullOrEmpty(functionToolCall.Arguments))
                           {
                              try
                              {
                                 using JsonDocument argumentsJson = JsonDocument.Parse(functionToolCall.Arguments);
                                 if (argumentsJson.RootElement.TryGetProperty("location", out JsonElement locationElement))
                                 {
                                    location = locationElement.GetString() ?? "New York";
                                 }
                              }
                              catch (JsonException ex)
                              {
                                 Console.WriteLine($"Error parsing function arguments: {ex.Message}");
                              }
                           }

                           // Execute the fetch weather function
                           string weatherResult = FetchWeather(location);
                           toolOutputs.Add(new ToolOutput(toolCall, weatherResult));
                           Console.WriteLine($"Executed fetchWeather for {location}");
                        }
                     }
                  }

                  // Submit the tool outputs back to the run
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

                     // Capture the agent's response
                     if (threadMessage.Role.ToString().ToLower() == "assistant")
                     {
                        agentResponse = textItem.Text;
                     }
                  }
               }
            }

            // Now you can use the agent response
            if (!string.IsNullOrEmpty(agentResponse))
            {
               Console.WriteLine("\n=== AGENT OUTPUT ===");
               Console.WriteLine(agentResponse);
               Console.WriteLine("==================");
            }
            return agentResponse;
         }
         catch (Exception exe)
         {
            Console.WriteLine(exe.ToString());
            return "";
         }
            finally
            {
                if (!string.IsNullOrEmpty(agentId))
                {
                    try
                    {
                        await DeleteAgentAsync(agentId);
                        Console.WriteLine($"\n✓ Deleted agent: {agentId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to delete agent: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Example test method demonstrating AUTOMATIC tool execution using the AIAgent's RunStreamingAsync.
        /// The framework handles tool calls automatically - simpler but less control.
        /// </summary>
        /// <param name="model">The AI model to use (e.g., "gpt-4o")</param>
        /// <param name="question">The question to ask the agent</param>
        /// <returns>The agent's response as a string</returns>
        public async Task<string> TestWeatherAgentAsync(string model, string question)
        {
            AIAgent? agent = null;
            string agentId = string.Empty;

            try
            {
                // 1. Create the weather tool definition
                var weatherTool = CreateWeatherToolDefinition();

                // 2. Create an agent with the tool
                // Note: The agent framework automatically handles tool execution when using RunAsync
                agent = await CreateAgentWithToolsAsync(
                    model: model,
                    name: "WeatherBot",
                    instructions: "You are a helpful weather assistant. Use the get_weather tool to answer weather questions.",
                    tool: weatherTool
                );

                agentId = agent.Id;
                Console.WriteLine($"✓ Created agent: {agent.Name} (ID: {agent.Id})");

                // 3. Use the agent's RunStreamingAsync method which automatically handles tool calls
                // The framework will execute tools as needed during the conversation
                var fullResponse = new System.Text.StringBuilder();
                
                Console.WriteLine($"\n--- Response ---");
                await foreach (var update in agent.RunStreamingAsync(question))
                {
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        Console.Write(update.Text);
                        fullResponse.Append(update.Text);
                    }
                }
                Console.WriteLine($"\n--- End ---\n");

                return fullResponse.ToString();
            }
            finally
            {
                // Cleanup
                if (!string.IsNullOrEmpty(agentId))
                {
                    try
                    {
                        await DeleteAgentAsync(agentId);
                        Console.WriteLine($"✓ Deleted agent: {agentId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to delete agent: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Example test method using streaming to show real-time responses
        /// </summary>
        /// <param name="model">The AI model to use (e.g., "gpt-4o-mini")</param>
        /// <param name="question">The question to ask the agent</param>
        /// <returns>The complete agent response</returns>
        public async Task<string> TestWeatherAgentStreamingAsync(string model, string question)
        {
            AIAgent? agent = null;
            string agentId = string.Empty;

            try
            {
                // 1. Create the weather tool definition
                var weatherTool = CreateWeatherToolDefinition();

                // 2. Create an agent with the tool
                agent = await CreateAgentWithToolsAsync(
                    model: model,
                    name: "WeatherBotStreaming",
                    instructions: "You are a helpful weather assistant. Use the get_weather tool to answer weather questions.",
                    tool: weatherTool
                );

                agentId = agent.Id;
                Console.WriteLine($"✓ Created agent: {agent.Name} (ID: {agent.Id})");

                // 3. Use streaming to get real-time response
                var fullResponse = new System.Text.StringBuilder();
                Console.WriteLine($"\n--- Streaming Response ---");
                
                await foreach (var update in agent.RunStreamingAsync(question))
                {
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        Console.Write(update.Text);
                        fullResponse.Append(update.Text);
                    }
                }
                
                Console.WriteLine($"\n--- End ---\n");

                return fullResponse.ToString();
            }
            finally
            {
                // Cleanup
                if (!string.IsNullOrEmpty(agentId))
                {
                    try
                    {
                        await DeleteAgentAsync(agentId);
                        Console.WriteLine($"✓ Deleted agent: {agentId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to delete agent: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Simplified test method that demonstrates manual tool call handling.
        /// This uses the ProcessToolCall method to handle tools locally.
        /// </summary>
        public async Task<string> QuickTestAsync(string model = "gpt-4o")
        {
            return await TestWeatherAgentWithManualToolHandlingAsync(
                model: model,
                question: "What's the weather like in Seattle?"
            );
        }

        /// <summary>
        /// Quick test using automatic tool handling (simpler approach)
        /// </summary>
        public async Task<string> QuickTestAutoAsync(string model = "gpt-4o")
        {
            return await TestWeatherAgentAsync(
                model: model,
                question: "What's the weather like in Seattle?"
            );
        }
    }
}