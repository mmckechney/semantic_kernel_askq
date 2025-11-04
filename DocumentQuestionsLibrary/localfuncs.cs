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
        private readonly Dictionary<string, MethodInfo> _toolMethods;
        private readonly Dictionary<string, object?> _toolInstances;

        public LocalFunctionTools(string projectEndpoint, Azure.Core.TokenCredential? credential = null)
        {
            credential ??= new DefaultAzureCredential();
            _projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);
            _agentsClient = _projectClient.GetPersistentAgentsClient();
            _toolMethods = new Dictionary<string, MethodInfo>();
            _toolInstances = new Dictionary<string, object?>();
            
            // Auto-discover tool methods in this class
            DiscoverToolMethods();
        }

        /// <summary>
        /// Discovers all methods marked with [Description] attributes as potential tool functions
        /// </summary>
        private void DiscoverToolMethods()
        {
            var type = this.GetType();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            foreach (var method in methods)
            {
                var descAttr = method.GetCustomAttribute<DescriptionAttribute>();
                if (descAttr != null)
                {
                    var toolName = GetSanitizedToolName(method.Name);
                    _toolMethods[toolName] = method;
                    _toolInstances[toolName] = method.IsStatic ? null : this;
                    
                    Console.WriteLine($"Discovered tool: {toolName} -> {method.Name}");
                }
            }
        }

        /// <summary>
        /// Registers an external tool method with an instance
        /// </summary>
        public void RegisterToolMethod(string toolName, MethodInfo method, object? instance = null)
        {
            var sanitizedName = GetSanitizedToolName(toolName);
            _toolMethods[sanitizedName] = method;
            _toolInstances[sanitizedName] = instance;
            
            Console.WriteLine($"Registered external tool: {sanitizedName} -> {method.DeclaringType?.Name}.{method.Name}");
        }

        /// <summary>
        /// Registers multiple tool methods from a delegate
        /// </summary>
        public void RegisterToolMethod(string toolName, Delegate toolDelegate)
        {
            RegisterToolMethod(toolName, toolDelegate.Method, toolDelegate.Target);
        }

        /// <summary>
        /// Gets all discovered tool definitions
        /// </summary>
        public IEnumerable<FunctionToolDefinition> GetAllToolDefinitions()
        {
            return _toolMethods.Select(kvp => CreateToolDefinitionFromMethod(kvp.Key, kvp.Value));
        }

        /// <summary>
        /// Creates a tool definition from a method using reflection
        /// </summary>
        private FunctionToolDefinition CreateToolDefinitionFromMethod(string toolName, MethodInfo method)
        {
            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? $"Executes {method.Name}";

            var props = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var param in method.GetParameters())
            {
                // Skip CancellationToken parameters
                if (param.ParameterType == typeof(CancellationToken))
                    continue;

                var paramDesc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? param.Name!;
                var paramType = GetJsonTypeForParameter(param.ParameterType);
                
                props[param.Name!] = new { type = paramType, description = paramDesc };
                
                if (!param.IsOptional && param.ParameterType != typeof(CancellationToken))
                    required.Add(param.Name!);
            }

            // Create schema object conditionally - only include 'required' if there are required parameters
            object schema;
            if (required.Count > 0)
            {
                schema = new
                {
                    type = "object",
                    properties = props,
                    required = required.ToArray()
                };
            }
            else
            {
                schema = new
                {
                    type = "object",
                    properties = props
                };
            }

            return new FunctionToolDefinition(
                name: toolName,
                description: description,
                parameters: BinaryData.FromObjectAsJson(schema, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            );
        }

        /// <summary>
        /// Sanitizes method names to comply with tool naming requirements
        /// </summary>
        private static string GetSanitizedToolName(string methodName)
        {
            // Remove angle brackets and other invalid characters from lambda/anonymous method names
            var sanitized = Regex.Replace(methodName, "[^a-zA-Z0-9_-]", "_");
            sanitized = Regex.Replace(sanitized, "_+", "_").Trim('_');
            
            return string.IsNullOrWhiteSpace(sanitized) ? "tool" : sanitized.ToLowerInvariant();
        }

        /// <summary>
        /// Maps .NET types to JSON schema types
        /// </summary>
        private static string GetJsonTypeForParameter(Type paramType)
        {
            if (paramType == typeof(int) || paramType == typeof(long) || paramType == typeof(short))
                return "integer";
            if (paramType == typeof(double) || paramType == typeof(float) || paramType == typeof(decimal))
                return "number";
            if (paramType == typeof(bool))
                return "boolean";
            if (paramType.IsArray || (paramType.IsGenericType && typeof(IEnumerable<>).IsAssignableFrom(paramType.GetGenericTypeDefinition())))
                return "array";
            
            return "string";
        }

        /// <summary>
        /// Executes a tool call by name using reflection
        /// </summary>
        public async Task<string> ExecuteToolCallAsync(string functionName, string argumentsJson)
        {
            var toolName = GetSanitizedToolName(functionName);
            
            if (!_toolMethods.TryGetValue(toolName, out var method))
            {
                return $"Unknown function: {functionName} (sanitized: {toolName})";
            }

            try
            {
                var arguments = ParseArgumentsForMethod(method, argumentsJson);
                var instance = _toolInstances[toolName];
                
                var result = method.Invoke(instance, arguments);
                
                // Handle async methods
                if (result is Task task)
                {
                    await task;
                    
                    // Get result from Task<T>
                    if (task.GetType().IsGenericType)
                    {
                        var resultProperty = task.GetType().GetProperty("Result");
                        result = resultProperty?.GetValue(task);
                    }
                    else
                    {
                        result = "Task completed successfully";
                    }
                }

                return SerializeResult(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing tool {functionName}: {ex.Message}");
                return $"Error executing {functionName}: {ex.Message}";
            }
        }

        /// <summary>
        /// Parses JSON arguments and maps them to method parameters
        /// </summary>
        private object[] ParseArgumentsForMethod(MethodInfo method, string argumentsJson)
        {
            var parameters = method.GetParameters();
            var arguments = new object[parameters.Length];

            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                // Fill with default values for optional parameters
                for (int i = 0; i < parameters.Length; i++)
                {
                    arguments[i] = parameters[i].ParameterType == typeof(CancellationToken) 
                        ? CancellationToken.None 
                        : parameters[i].DefaultValue ?? GetDefaultValue(parameters[i].ParameterType);
                }
                return arguments;
            }

            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                
                if (param.ParameterType == typeof(CancellationToken))
                {
                    arguments[i] = CancellationToken.None;
                    continue;
                }

                if (root.TryGetProperty(param.Name!, out var jsonValue))
                {
                    arguments[i] = ConvertJsonValueToType(jsonValue, param.ParameterType);
                }
                else if (param.IsOptional)
                {
                    arguments[i] = param.DefaultValue ?? GetDefaultValue(param.ParameterType);
                }
                else
                {
                    throw new ArgumentException($"Missing required parameter: {param.Name}");
                }
            }

            return arguments;
        }

        /// <summary>
        /// Converts JsonElement to the specified type
        /// </summary>
        private object? ConvertJsonValueToType(JsonElement jsonValue, Type targetType)
        {
            if (targetType == typeof(string))
                return jsonValue.GetString();
            if (targetType == typeof(int))
                return jsonValue.GetInt32();
            if (targetType == typeof(long))
                return jsonValue.GetInt64();
            if (targetType == typeof(bool))
                return jsonValue.GetBoolean();
            if (targetType == typeof(double))
                return jsonValue.GetDouble();
            if (targetType == typeof(decimal))
                return jsonValue.GetDecimal();
                
            // For complex types, try JSON deserialization
            try
            {
                return JsonSerializer.Deserialize(jsonValue.GetRawText(), targetType);
            }
            catch
            {
                return jsonValue.GetString(); // Fallback to string
            }
        }

        /// <summary>
        /// Gets default value for a type
        /// </summary>
        private static object? GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        /// <summary>
        /// Serializes the result to JSON string format
        /// </summary>
        private static string SerializeResult(object? result)
        {
            if (result == null)
                return "null";
            
            if (result is string str)
                return str;
                
            try
            {
                return JsonSerializer.Serialize(result, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false 
                });
            }
            catch
            {
                return result.ToString() ?? "null";
            }
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

        [Description("Calculates the sum of two numbers")]
        private static string Calculator([Description("First number")] double a, [Description("Second number")] double b, [Description("Operation to perform")] string operation = "add")
        {
            double result = operation.ToLower() switch
            {
                "add" => a + b,
                "subtract" => a - b,
                "multiply" => a * b,
                "divide" => b != 0 ? a / b : throw new ArgumentException("Cannot divide by zero"),
                _ => throw new ArgumentException($"Unknown operation: {operation}")
            };

            return JsonSerializer.Serialize(new { operation, a, b, result });
        }

        [Description("Gets the current date and time")]
        private static string GetCurrentDateTime([Description("Format for the date/time")] string format = "yyyy-MM-dd HH:mm:ss")
        {
            try
            {
                var now = DateTime.Now;
                return JsonSerializer.Serialize(new { 
                    formatted = now.ToString(format),
                    utc = now.ToUniversalTime().ToString("o"),
                    timestamp = ((DateTimeOffset)now).ToUnixTimeSeconds()
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message, defaultFormat = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
            }
        }
        /// <summary>
        /// Creates a function tool definition for weather lookup
        /// </summary>
        public FunctionToolDefinition CreateWeatherToolDefinition()
        {
            // Use the new reflection-based approach
            var method = typeof(LocalFunctionTools).GetMethod("FetchWeather", BindingFlags.NonPublic | BindingFlags.Static);
            if (method != null)
            {
                return CreateToolDefinitionFromMethod("fetchweather", method);
            }

            // Fallback to the old method if reflection fails
            return AgentUtility.FoundryToolFromMethod(FetchWeather);
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
        /// Creates an agent with multiple tools
        /// </summary>
        public async Task<AIAgent> CreateAgentWithMultipleToolsAsync(string model, string name, string instructions, params FunctionToolDefinition[] tools)
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
        /// Creates an agent with all discovered tools
        /// </summary>
        public async Task<AIAgent> CreateAgentWithAllToolsAsync(string model, string name, string instructions)
        {
            var tools = GetAllToolDefinitions().ToArray();
            Console.WriteLine($"Creating agent with {tools.Length} discovered tools:");
            foreach (var tool in tools)
            {
                Console.WriteLine($"  - {tool.Name}: {tool.Description}");
            }
            
            var agent = await _agentsClient.CreateAIAgentAsync(
                model: model,
                name: name,
                description: $"Agent: {name}",
                instructions: instructions,
                tools: tools);
            
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
            Console.WriteLine("=== Testing Generic Tool Execution ===\n");
            
            // Demonstrate the reflection-based tool execution
            Console.WriteLine("Available tools:");
            var toolDefinitions = GetAllToolDefinitions();
            foreach (var tool in toolDefinitions)
            {
                Console.WriteLine($"  - {tool.Name}: {tool.Description}");
            }
            Console.WriteLine();

            string agentId = string.Empty;
            PersistentAgentThread? thread = null;

            try
            {
                // Create agent with all discovered tools (not just weather)
                var agent = await CreateAgentWithAllToolsAsync(
                    model: model,
                    name: "GenericToolAgent",
                    instructions: "You are a helpful assistant with access to various tools. Use the appropriate tools to answer user questions."
                );

                agentId = agent.Id;
                Console.WriteLine($"✓ Created agent: {agent.Name} (ID: {agentId}) with {toolDefinitions.Count()} tools\n");

                Console.WriteLine($"Question: {question}");
                Console.WriteLine($"--- Agent Response ---");

                // Create thread and run
                thread = await _agentsClient.Threads.CreateThreadAsync();
                Console.WriteLine($"Created thread, ID: {thread.Id}");

                var messageResponse = _agentsClient.Messages.CreateMessage(threadId: thread.Id, role: MessageRole.User, content: question);
                ThreadRun threadRun = await _agentsClient.Runs.CreateRunAsync(thread.Id, agent.Id);

                // Process with generic tool handling
                List<RunStatus> activeStatus = [RunStatus.Queued, RunStatus.InProgress, RunStatus.RequiresAction];
                while (activeStatus.Contains(threadRun.Status))
                {
                    await Task.Delay(1000);
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
                                Console.WriteLine($"Processing tool call: {functionToolCall.Name}");
                                Console.WriteLine($"Arguments: {functionToolCall.Arguments}");
                                
                                try
                                {
                                    // Use the generic tool execution method - works for ANY discovered tool
                                    string toolResult = await ExecuteToolCallAsync(functionToolCall.Name, functionToolCall.Arguments ?? "{}");
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
                }

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
                // Cleanup
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

                if (thread != null)
                {
                    try
                    {
                        await DeleteThreadAsync(thread.Id);
                        Console.WriteLine($"✓ Deleted thread: {thread.Id}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to delete thread: {ex.Message}");
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

        /// <summary>
        /// Tests an agent with all discovered tools using the generic tool execution
        /// </summary>
        public async Task<string> TestAgentWithAllToolsAsync(string model, string question)
        {
            string agentId = string.Empty;
            PersistentAgentThread? thread = null;

            try
            {
                // Create agent with all discovered tools
                var agent = await CreateAgentWithAllToolsAsync(
                    model: model,
                    name: "GenericToolAgent",
                    instructions: "You are a helpful assistant with access to various tools. Use the appropriate tools to answer user questions."
                );

                agentId = agent.Id;
                Console.WriteLine($"✓ Created agent: {agent.Name} (ID: {agentId})\n");

                // Create thread and run
                thread = await _agentsClient.Threads.CreateThreadAsync();
                Console.WriteLine($"Created thread, ID: {thread.Id}");

                var messageResponse = _agentsClient.Messages.CreateMessage(threadId: thread.Id, role: MessageRole.User, content: question);
                ThreadRun threadRun = await _agentsClient.Runs.CreateRunAsync(thread.Id, agent.Id);

                // Process with generic tool handling
                List<RunStatus> activeStatus = [RunStatus.Queued, RunStatus.InProgress, RunStatus.RequiresAction];
                while (activeStatus.Contains(threadRun.Status))
                {
                    await Task.Delay(1000);
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
                                Console.WriteLine($"Processing tool call: {functionToolCall.Name}");
                                
                                try
                                {
                                    // Use the generic tool execution method
                                    string toolResult = await ExecuteToolCallAsync(functionToolCall.Name, functionToolCall.Arguments ?? "{}");
                                    toolOutputs.Add(new ToolOutput(toolCall, toolResult));
                                    Console.WriteLine($"Executed {functionToolCall.Name} successfully");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error executing tool {functionToolCall.Name}: {ex.Message}");
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
                }

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
                Console.WriteLine($"Error in TestAgentWithAllToolsAsync: {ex}");
                return $"Error: {ex.Message}";
            }
            finally
            {
                // Cleanup
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

                if (thread != null)
                {
                    try
                    {
                        await DeleteThreadAsync(thread.Id);
                        Console.WriteLine($"✓ Deleted thread: {thread.Id}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to delete thread: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Quick test with all tools
        /// </summary>
        public async Task<string> QuickTestAllToolsAsync(string model = "gpt-4o", string question = "What's the weather in Seattle? Also, what's 15 + 27? And what time is it?")
        {
            return await TestAgentWithAllToolsAsync(model, question);
        }
    }
}