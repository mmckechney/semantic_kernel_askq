using Azure.AI.Agents.Persistent;
using DocumentQuestions.Library;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using syS = System;

namespace DocumentQuestions.Console
{
   internal class Worker : BackgroundService
   {
      private static ILogger<Worker> log;
      private static ILoggerFactory logFactory;
      private static IConfiguration config;
      private static StartArgs? startArgs;
      private static AgentUtility agentUtility;
      private static Common common;
      private static Parser rootParser;
      private static DocumentIntelligence documentIntelligence;
      private static string activeDocument = string.Empty;
      private static AiSearch aiSearch;
      private static ThreadRun? currentThreadRun = null; // Thread for multi-turn conversations
      private static LocalToolsUtility localToolsUtility;
      private static LocalToolsLibrary localToolsLibrary;



      public Worker(ILogger<Worker> logger, ILoggerFactory loggerFactory, IConfiguration configuration, StartArgs sArgs, AgentUtility agentUtility, Common cmn, DocumentIntelligence documentIntel, AiSearch aiSrch, LocalToolsUtility localToolsUtility, LocalToolsLibrary localToolsLibrary)
      {
         log = logger;
         logFactory = loggerFactory;
         config = configuration;
         startArgs = sArgs;
         common = cmn;
         Worker.agentUtility = agentUtility;
         documentIntelligence = documentIntel;
         aiSearch = aiSrch;
         Worker.localToolsUtility = localToolsUtility;
         Worker.localToolsLibrary = localToolsLibrary;

      }

      internal static async Task AskQuestion(string[] question)
      {
         if (question == null || question.Length == 0)
         {
            return;
         }
         if (string.IsNullOrWhiteSpace(activeDocument))
         {
            //log.LogInformation("Please use the 'doc' command to set an active document to start asking questions.", ConsoleColor.Yellow);
            return;
         }
         string quest = string.Join(" ", question);
         syS.Console.WriteLine("----------------------");
         //var docContent = await agentUtility.SearchForReleventContent(activeDocument, quest);
         //if (string.IsNullOrWhiteSpace(docContent))
         //{
         //   log.LogInformation("No relevant content found in the document for the question. Please verify your document name with the 'list' command or try another question.", ConsoleColor.Yellow);
         //}
         //else
         //{
            // Use thread-based conversation for follow-up questions
            StringBuilder responseBuilder = new();
            await foreach (var (text,thread) in agentUtility.AskQuestionStreamingWithThread (quest, activeDocument, currentThreadRun))
            {
               syS.Console.Write(text);
               responseBuilder.Append(text);
               currentThreadRun = thread; // Update thread for next question
            }
            
            var messages = await agentUtility.GetThreadMessages(currentThreadRun);
            syS.Console.WriteLine();
            log.LogInformation($"{messages}", ConsoleColor.DarkGray);
            // Display turn count
            //int turnCount = currentThread.Serialize().GetProperty("messages").GetArrayLength();
            //if (turnCount > 2)
            //{
            //   syS.Console.WriteLine();
            //   log.LogInformation($"[Conversation turn: {turnCount / 2}]", ConsoleColor.DarkGray);
            //}
         //}

         syS.Console.WriteLine();
         var steps = await agentUtility.GetThreadSteps(currentThreadRun);
         syS.Console.WriteLine(steps);
         syS.Console.WriteLine();
         //syS.Console.WriteLine("PLEASE NOTE: This does not constitue legal advice or counsel.");
         syS.Console.WriteLine("----------------------");
         syS.Console.WriteLine();
      }

      internal static Task ResetConversation()
      {
         currentThreadRun = null;
         log.LogInformation("Conversation thread reset. Starting fresh conversation.", ConsoleColor.Green);
         return Task.CompletedTask;
      }

      internal static async void AzureOpenAiSettings(string chatModel, string chatDeployment, string embedModel, string embedDeployment)
      {
         if (string.IsNullOrWhiteSpace(chatModel) && string.IsNullOrWhiteSpace(chatDeployment) && string.IsNullOrWhiteSpace(embedModel) && string.IsNullOrWhiteSpace(embedDeployment))
         {
            await rootParser.InvokeAsync("ai set -h");
            return;
         }
         bool changed = false;
         if (!string.IsNullOrWhiteSpace(chatModel))
         {
            config[Constants.OPENAI_CHAT_MODEL_NAME] = chatModel;
            log.LogInformation(new() { { "Set chat model to", ConsoleColor.DarkYellow }, { chatModel, ConsoleColor.Yellow } });
            changed = true;
         }
         if (!string.IsNullOrWhiteSpace(chatDeployment))
         {
            config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] = chatDeployment;
            log.LogInformation(new() { { "Set chat deployment to", ConsoleColor.DarkYellow }, { chatDeployment, ConsoleColor.Yellow } });
            changed = true;
         }
         if (!string.IsNullOrWhiteSpace(embedModel))
         {
            config[Constants.OPENAI_EMBEDDING_MODEL_NAME] = embedModel;
            log.LogInformation(new() { { "Set embedding model to", ConsoleColor.DarkYellow }, { embedModel, ConsoleColor.Yellow } });
            changed = true;
         }
         if (!string.IsNullOrWhiteSpace(embedDeployment))
         {
            config[Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME] = embedDeployment;
            log.LogInformation(new() { { "Set embedding deployment to", ConsoleColor.DarkYellow }, { embedDeployment, ConsoleColor.Yellow } });
            changed = true;
         }

         if (changed)
         {
            agentUtility.InitAgents();
            ListAiSettings();
         }
      }

      internal async static Task ClearIndex(string[] indexes)
      {
         if (indexes.Length > 0)
         {
            var deleted = await aiSearch.ClearIndexes(indexes.ToList());
            if (deleted.Count > 0)
            {
               log.LogInformation("The following indexes were deleted:", ConsoleColor.Yellow);
               foreach (var name in deleted)
               {
                  log.LogInformation($"\t{name}");
               }
            }
            else
            {
               log.LogInformation("No indexes were deleted.", ConsoleColor.Yellow);
            }
         }
         else
         {
            log.LogInformation("No indexes were deleted.", ConsoleColor.Yellow);
         }
      }

      internal static void ListAiSettings()
      {
         int pad = 21;
         log.LogInformation("-------------------------------------");
         log.LogInformation("Azure OpenAI settings", ConsoleColor.Gray);
         log.LogInformation(new() { { "Chat Model:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config[Constants.OPENAI_CHAT_MODEL_NAME], ConsoleColor.Blue } });
         log.LogInformation(new() { { "Chat Deployment:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME], ConsoleColor.Blue } });
         log.LogInformation(new() { { "Embedding Model:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config[Constants.OPENAI_EMBEDDING_MODEL_NAME], ConsoleColor.Blue } });
         log.LogInformation(new() { { "Embedding Deployment:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config[Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME], ConsoleColor.Blue } });
         log.LogInformation("-------------------------------------");


      }

      internal async static Task<int> ListFiles(object t)
      {
         var names = await aiSearch.ListAvailableIndexes();
         if (names.Count > 0)
         {
            log.LogInformation("List of available documents:", ConsoleColor.Yellow);
         }
         foreach (var name in names)
         {
            log.LogInformation(name);
         }
         return names.Count;
      }

      internal static async Task ProcessFile(string file, string model, string index)
      {
         if (string.IsNullOrWhiteSpace(model))
         {
            model = "prebuilt-layout";
         }
         if (file.Length == 0)
         {
            log.LogInformation("Please enter a file name to process", ConsoleColor.Red);
            return;
         }
         string name = string.Join(" ", file);
         if (!File.Exists(name))
         {
            log.LogInformation($"The file {name} doesn't exist. Please enter a valid file name", ConsoleColor.Red);
            return;
         }
       
         await documentIntelligence.ProcessDocument(new FileInfo(name), model, index);

      }

      internal static void SetActiveDocument(string[] document)
      {
         var docName = string.Join(" ", document);
         activeDocument = docName;
      }

      protected async override Task ExecuteAsync(CancellationToken stoppingToken)
      {
         var local = new LocalFunctionTools(config["AIFOUNDRY_ENDPOINT"], localToolsUtility);
         await local.QuickTestAsync();
         return;

         Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
         rootParser = CommandBuilder.BuildCommandLine();
         string[] args = startArgs.Args;
         if (args.Length == 0) args = new string[] { "-h" };
         int val = await rootParser.InvokeAsync(args);
         bool firstPass = true;
         int fileCount = 0;
         StringBuilder sb;


         return;
         while (true)
         {
            sb = new StringBuilder();
            syS.Console.WriteLine();
            if (firstPass || string.IsNullOrWhiteSpace(activeDocument))
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


            syS.Console.Write("dq> ");
            var line = syS.Console.ReadLine();
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
