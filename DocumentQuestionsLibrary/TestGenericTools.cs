using DocumentQuestions.Library;
using System;
using System.Threading.Tasks;

namespace DocumentQuestions.Library
{
    /// <summary>
    /// Test program to demonstrate the generic tool calling system
    /// </summary>
    public class TestGenericTools
    {
        public static async Task Main(string[] args)
        {
            // Set up environment (replace with your actual endpoint)
            var endpoint = Environment.GetEnvironmentVariable("AIFOUNDRY_ENDPOINT") 
                ?? "https://your-project.region.models.ai.azure.com";

            Console.WriteLine("=== Testing Generic Tool System ===\n");

            try
            {
                // Create the tools helper
                var tools = new LocalFunctionTools(endpoint);

                Console.WriteLine("Discovered Tools:");
                var toolDefinitions = tools.GetAllToolDefinitions();
                foreach (var tool in toolDefinitions)
                {
                    Console.WriteLine($"  - {tool.Name}: {tool.Description}");
                }
                Console.WriteLine();

                // Test 1: Test with weather question
                Console.WriteLine("=== Test 1: Weather Query ===");
                var response1 = await tools.QuickTestAllToolsAsync("gpt-4o-mini", "What's the weather in New York?");
                Console.WriteLine($"Response: {response1}\n");

                // Test 2: Test with math question
                Console.WriteLine("=== Test 2: Math Query ===");
                var response2 = await tools.QuickTestAllToolsAsync("gpt-4o-mini", "What is 25 multiplied by 4?");
                Console.WriteLine($"Response: {response2}\n");

                // Test 3: Test with time question
                Console.WriteLine("=== Test 3: Time Query ===");
                var response3 = await tools.QuickTestAllToolsAsync("gpt-4o-mini", "What time is it now?");
                Console.WriteLine($"Response: {response3}\n");

                // Test 4: Test with combined question
                Console.WriteLine("=== Test 4: Combined Query ===");
                var response4 = await tools.QuickTestAllToolsAsync("gpt-4o-mini", 
                    "What's the weather in Seattle? Also calculate 15 + 27, and tell me what time it is.");
                Console.WriteLine($"Response: {response4}\n");

                Console.WriteLine("✓ All tests completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Example of how to register external tool methods
        /// </summary>
        public static void DemonstrateExternalToolRegistration(LocalFunctionTools tools)
        {
            // Example: Register a method from another class
            // tools.RegisterToolMethod("external_tool", someExternalMethod);
            
            // Example: Register a lambda function
            tools.RegisterToolMethod("string_length", 
                (string input) => $"The string '{input}' has {input.Length} characters.");
        }
    }
}