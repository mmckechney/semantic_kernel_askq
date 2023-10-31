using Azure.AI.OpenAI;
using Azure.Identity;
using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Services;

namespace DocumentQuestions.Function
{
    public class AzureOpenAiService
    {
        private SemanticMemory semanticMemory;
        ILogger<HttpTriggerAskAboutADoc> log;
        IConfiguration config;
        Common common;
        public AzureOpenAiService(ILogger<HttpTriggerAskAboutADoc> log, IConfiguration config, Common common)
        {
            this.log = log;
            this.config = config;
            this.common = common;
            this.semanticMemory = semanticMemory;
        }

        private OpenAIClient _client = null;
        public OpenAIClient Client
        {
            get
            {
                if (_client == null)
                {
                    var openAIEndpoint = config["OpenAIEndpoint"] ?? throw new ArgumentException("Missing OpenAIEndpoint in configuration.");
                    var openAIKey = "";
                    bool useOpenAIKey = false;
                    try
                    {
                        openAIKey = config["OpenAIKey"];
                        bool.TryParse(config["UseOpenAIKey"], out useOpenAIKey);
                    }
                    catch { }

                    if (useOpenAIKey && !string.IsNullOrWhiteSpace(openAIKey))
                    {
                        _client = new OpenAIClient(new Uri(openAIEndpoint), new AzureKeyCredential(openAIKey));
                    }
                    else
                    {
                        _client = new OpenAIClient(new Uri(openAIEndpoint), new DefaultAzureCredential());
                    }
                }
                return _client;

            }
        }

        private string _chatModel = null;
        public string ChatModel
        {
            get
            {
                if (_chatModel == null)
                {
                    _chatModel = config["OpenAIChatModel"] ?? "gpt-4-32k";
                }
                return _chatModel;
            }
        }
        public async Task<string> AskOpenAIAsync(string filename, string prompt)
        {
            log.LogInformation("Ask OpenAI Async A Question");

            var content = await common.GetBlobContentAsync(filename);

            var chatCompletionsOptions = GetChatCompletionsOptions(content, prompt);
            var completionsResponse = await this.Client.GetChatCompletionsAsync(this.ChatModel, chatCompletionsOptions);
            string completion = completionsResponse.Value.Choices[0].Message.Content;

            return completion;
        }
        public async Task<string> AskOpenAIAsync(string prompt, IAsyncEnumerable<MemoryQueryResult> memories)
        {
            log.LogInformation("Ask OpenAI Async A Question");


            var content = "";
            var docName = "";
            await foreach (MemoryQueryResult memoryResult in memories)
            {
                log.LogInformation("Memory Result = " + memoryResult.Metadata.Description);
                if (docName != memoryResult.Metadata.Id.Substring(0, memoryResult.Metadata.Id.LastIndexOf('_')))
                {
                    docName = memoryResult.Metadata.Id.Substring(0, memoryResult.Metadata.Id.LastIndexOf('_'));
                    content += $"\nDocument Name: {docName}\n";
                }
                content += memoryResult.Metadata.Description;
            };

            var chatCompletionsOptions = GetChatCompletionsOptions(content, prompt);
            var completionsResponse = await this.Client.GetChatCompletionsAsync(this.ChatModel, chatCompletionsOptions);
            string completion = completionsResponse.Value.Choices[0].Message.Content;

            return completion;
        }

        public static ChatCompletionsOptions GetChatCompletionsOptions(string content, string prompt)
        {
            var opts = new ChatCompletionsOptions()
            {
                Messages =
                  {
                      new ChatMessage(ChatRole.System, @"You are a document answering bot.  You will be provided with information from a document, and you are to answer the question based on the content provided.  Your are not to make up answers. Use the content provided to answer the question."),
                      new ChatMessage(ChatRole.User, @"Content = " + content),
                      new ChatMessage(ChatRole.User, @"Question = " + prompt),
                  },
            };

            return opts;
        }
    }
}
