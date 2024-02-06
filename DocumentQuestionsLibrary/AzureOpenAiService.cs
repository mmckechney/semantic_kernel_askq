using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocumentQuestions.Library
{
   public class AzureOpenAiService
   {
      ILogger<AzureOpenAiService> log;
      IConfiguration config;
      Common common;
      public AzureOpenAiService(ILogger<AzureOpenAiService> log, IConfiguration config, Common common)
      {
         this.log = log;
         this.config = config;
         this.common = common;
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
         var completionsResponse = await Client.GetChatCompletionsAsync(chatCompletionsOptions);
         string completion = completionsResponse.Value.Choices[0].Message.Content;

         return completion;
      }
      public ChatCompletionsOptions GetChatCompletionsOptions(string content, string prompt)
      {
         var opts = new ChatCompletionsOptions()
         {
            Messages =
                  {
                      new ChatRequestSystemMessage(@"You are a document answering bot.  You will be provided with information from a document, and you are to answer the question based on the content provided.  Your are not to make up answers. Use the content provided to answer the question."),
                      new ChatRequestUserMessage(@"Content = " + content),
                      new ChatRequestUserMessage(@"Question = " + prompt),
                  },
         };
         opts.DeploymentName = config["OpenAIChatDeploymentName"];

         return opts;
      }
   }
}
