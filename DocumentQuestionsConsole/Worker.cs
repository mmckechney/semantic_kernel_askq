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
      private static string lastDocument = string.Empty;
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

      internal static async Task AskQuestion(string[] question, string doc)
      {
         if(question == null || question.Length == 0)
         {
            return;
         }
         if(string.IsNullOrWhiteSpace(doc) && !string.IsNullOrWhiteSpace(lastDocument))
         {
            doc = lastDocument;
         }
         string quest = string.Join(" ", question);
         s.Console.WriteLine("----------------------");
         var docContent = await  semanticUtility.SearchForReleventContent(doc, quest);
         await foreach(var bit in semanticUtility.AskQuestionStreaming(quest, docContent))
         {
            s.Console.Write(bit);
         }

         s.Console.WriteLine();
         s.Console.WriteLine("----------------------");
         s.Console.WriteLine();
         lastDocument = doc;
      }

      internal async static Task ListFiles(object t)
      {
         var names = await aiSearch.ListAvailableIndexes();
         foreach(var name in names)
         {
            log.LogInformation(name);
         }
      }

      internal static async Task ProcessFile(string[] file)
      {
         string name = string.Join(" ", file);
         if(!File.Exists(name))
         {
            log.LogError($"The file {name} doesn't exist. Please enter a valid file name");
            return;
         }
         await documentIntelligence.ProcessDocument(new FileInfo(name));
      }

      protected async override Task ExecuteAsync(CancellationToken stoppingToken)
      {
         Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
         rootParser = CommandBuilder.BuildCommandLine();
         string[] args = startArgs.Args;
         if (args.Length == 0) args = new string[] { "-h" };
         int val = await rootParser.InvokeAsync(args);

         StringBuilder sb;
         while (true)
         {
            s.Console.ForegroundColor = ConsoleColor.White;
            sb = new StringBuilder();
            s.Console.WriteLine();
            if(!string.IsNullOrWhiteSpace(lastDocument))
            {
               s.Console.ForegroundColor = ConsoleColor.DarkGreen;
               s.Console.WriteLine("Active Document for questions: " + lastDocument);
               s.Console.ResetColor();
               s.Console.ForegroundColor = ConsoleColor.DarkYellow;
               s.Console.WriteLine("use '--doc' flag to change the active document for questions.");
            }
            s.Console.ForegroundColor = ConsoleColor.Cyan;
            s.Console.Write("dq> ");
            s.Console.ResetColor();
            var line = s.Console.ReadLine();
            if (line == null)
            {
               return;
            }
            val = await rootParser.InvokeAsync(line);
         }
      }
   }
}
