using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Company.Function
{
    static class Common
    {
        private static OpenAIClient _client = null;
        public static OpenAIClient Client
        {
            get
            {
                if (_client == null)
                {
                    var openAIEndpoint = Environment.GetEnvironmentVariable("OpenAIEndpoint");
                    var openAIKey = "";
                    bool useOpenAIKey = false;
                    try
                    {
                        openAIKey = Environment.GetEnvironmentVariable("OpenAIKey");
                        bool.TryParse(Environment.GetEnvironmentVariable("UseOpenAIKey"), out useOpenAIKey);
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

        private static string chatModel = Environment.GetEnvironmentVariable("OpenAIChatModel");
        public static string ChatModel
        {
            get
            {
                return chatModel;
            }
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

        public static async Task<(string filename, string question)>GetFilenameAndQuery(HttpRequest req, ILogger log)
        {
            string filename = req.Query["filename"];
            string question = req.Query["question"];
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            log.LogInformation(requestBody);
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            filename = filename ?? data?.filename;
            question = question ?? data?.question;

            if (string.IsNullOrWhiteSpace(filename))
            {
                filename = "general";
            }
            else
            {
                filename = Path.GetFileNameWithoutExtension(filename);
            }


            log.LogInformation("filename = " + filename);
            log.LogInformation("question = " + question);

            return (filename, question);
        }
    }
}
