using Microsoft.Extensions.Hosting;
using s= System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocumentQuestions.Library;
using System.CommandLine.Parsing;
using Microsoft.SemanticKernel.Connectors.AzureAISearch;
using Json.Schema.Generation.Intents;

namespace DocumentQuestions.Console
{
   internal class Worker : BackgroundService
   {
      private static ILogger<Worker> log;
      private static ILoggerFactory logFactory;
      private static IConfiguration config;
      private static StartArgs startArgs;
      private static SemanticUtility semanticUtility;
      private static Common common;
      private static Parser rootParser;
      private static DocumentIntelligence documentIntelligence;
      private static string activeDocument = string.Empty;
      private static AiSearch aiSearch;
      public Worker(ILogger<Worker> logger, ILoggerFactory loggerFactory, IConfiguration configuration, StartArgs sArgs, SemanticUtility semanticUtil, Common cmn, DocumentIntelligence documentIntel, AiSearch aiSrch)
      {
         log = logger;
         logFactory = loggerFactory;
         config = configuration;
         startArgs = sArgs;
         common = cmn;
         semanticUtility = semanticUtil;
         documentIntelligence = documentIntel;
         aiSearch = aiSrch;
      }

      internal static async Task AskQuestion(string[] question)
      {
         if(question == null || question.Length == 0)
         {
            return;
         }
         if(string.IsNullOrWhiteSpace(activeDocument))
         {
            //log.LogInformation("Please use the 'doc' command to set an active document to start asking questions.", ConsoleColor.Yellow);
            return;
         }
         string quest = string.Join(" ", question);
         s.Console.WriteLine("----------------------");
         var docContent = await  semanticUtility.SearchForReleventContent(activeDocument, quest);
         if (string.IsNullOrWhiteSpace(docContent))
         {
            log.LogInformation("No relevant content found in the document for the question. Please verify your document name with the 'list' command or try another question.", ConsoleColor.Yellow);
         }
         else
         {
            await foreach (var bit in semanticUtility.AskQuestionStreaming(quest, docContent))
            {
               s.Console.Write(bit);
            }
         }

         s.Console.WriteLine();
         s.Console.WriteLine("----------------------");
         s.Console.WriteLine();
      }

      internal static async void AzureOpenAiSettings(string chatModel, string chatDeployment, string embedModel, string embedDeployment)
      {
         if(string.IsNullOrWhiteSpace(chatModel) && string.IsNullOrWhiteSpace(chatDeployment) && string.IsNullOrWhiteSpace(embedModel) && string.IsNullOrWhiteSpace(embedDeployment))
         {
            await rootParser.InvokeAsync("ai set -h");
            return;
         }
         bool changed = false;
         if(!string.IsNullOrWhiteSpace(chatModel))
         {
            config["OpenAIChatModel"] = chatModel;
            log.LogInformation(new() { { "Set chat model to", ConsoleColor.DarkYellow }, { chatModel, ConsoleColor.Yellow } });
            changed = true;
         }
         if(!string.IsNullOrWhiteSpace(chatDeployment))
         {
            config["OpenAIChatDeploymentName"] = chatDeployment;
            log.LogInformation(new() { { "Set chat deployment to", ConsoleColor.DarkYellow }, { chatDeployment, ConsoleColor.Yellow } });
            changed = true;
         }
         if(!string.IsNullOrWhiteSpace(embedModel))
         {
            config["OpenAIEmbeddingModel"] = embedModel;
            log.LogInformation(new() { { "Set embedding model to", ConsoleColor.DarkYellow }, { embedModel, ConsoleColor.Yellow } });
            changed = true;
         }
         if(!string.IsNullOrWhiteSpace(embedDeployment))
         {
            config["OpenAIEmbeddingDeploymentName"] = embedDeployment;
            log.LogInformation(new() { { "Set embedding deployment to", ConsoleColor.DarkYellow }, { embedDeployment, ConsoleColor.Yellow } });
            changed = true;
         }

         if(changed)
         {
            semanticUtility.InitMemoryAndKernel();
            ListAiSettings();
         }
      }

      internal static void ListAiSettings()
      {
         int pad = 21;
         log.LogInformation("-------------------------------------");
         log.LogInformation("Azure OpenAI settings", ConsoleColor.Gray);
         log.LogInformation(new() { { "Chat Model:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config["OpenAIChatModel"], ConsoleColor.Blue } });
         log.LogInformation(new() { { "Chat Deployment:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config["OpenAIChatDeploymentName"], ConsoleColor.Blue } });
         log.LogInformation(new() { { "Embedding Model:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config["OpenAIEmbeddingModel"], ConsoleColor.Blue } });
         log.LogInformation(new() { { "Embedding Deployment:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config["OpenAIEmbeddingDeploymentName"], ConsoleColor.Blue } });
         log.LogInformation("-------------------------------------");


      }

      internal async static Task<int> ListFiles(object t)
      {
         var names = await aiSearch.ListAvailableIndexes();
         if(names.Count > 0)
         {
            log.LogInformation("List of available documents:", ConsoleColor.Yellow);
         }
         foreach(var name in names)
         {
            log.LogInformation(name);
         }
         return names.Count;
      }

      internal static async Task ProcessFile(string[] file)
      {
         if(file.Length == 0)
         {
            log.LogInformation("Please enter a file name to process", ConsoleColor.Red);
            return;
         }
         string name = string.Join(" ", file);
         if(!File.Exists(name))
         {
            log.LogInformation($"The file {name} doesn't exist. Please enter a valid file name", ConsoleColor.Red);
            return;
         }
         await documentIntelligence.ProcessDocument(new FileInfo(name));
      }

      internal static void SetActiveDocument(string[] document)
      {
         var docName = string.Join(" ", document);
         activeDocument = docName;
      }

      protected async override Task ExecuteAsync(CancellationToken stoppingToken)
      {
         Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
         rootParser = CommandBuilder.BuildCommandLine();
         string[] args = startArgs.Args;
         if (args.Length == 0) args = new string[] { "-h" };
         int val = await rootParser.InvokeAsync(args);
         bool firstPass = true;
         int fileCount = 0;
         StringBuilder sb;
         while (true)
         {
            sb = new StringBuilder();
            s.Console.WriteLine();
            if (firstPass && string.IsNullOrWhiteSpace(activeDocument))
            {
               fileCount = await rootParser.InvokeAsync("list");
            }

            if (fileCount > 0)
            {
               if (!string.IsNullOrWhiteSpace(activeDocument))
               {
                  log.LogInformation(new() { { "Active Document: ", ConsoleColor.DarkGreen }, { activeDocument, ConsoleColor.Blue } });
                  //log.LogInformation("use '--doc' flag to change the active document.", ConsoleColor.Yellow);
               }
               else
               {
                  log.LogInformation("Please use the 'doc' command to set an active document to start asking questions. Use 'list' to show available documents or 'process' to index a new document", ConsoleColor.Yellow);
                  log.LogInformation("");
               }
            }
            else
            {
               log.LogInformation("Please use the 'process' command to process your first document.", ConsoleColor.Yellow);
               log.LogInformation("");
            }

           
            s.Console.Write("dq> ");
            var line = s.Console.ReadLine();
            if (line == null)
            {
               return;
            }
            val = await rootParser.InvokeAsync(line);
            firstPass = false;
         }
      }
   }
}
