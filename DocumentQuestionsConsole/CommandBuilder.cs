using DocumentQuestions.Library;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Linq;

namespace DocumentQuestions.Console
{
   internal class CommandBuilder
   {
      public static Parser BuildCommandLine()
      {
         
         var docOpt = new Option<string>(new string[] { "--doc", "-d" },() => "", "Name of the document to inquire about.") { IsRequired = true };
         var docCommand = new Command("doc", "Set the active document to start asking questions");
         var documentArg = new Argument<string[]>("document", "Document to set as active") { Arity = ArgumentArity.ZeroOrMore };
         docCommand.Add(documentArg);
         docCommand.Handler = CommandHandler.Create<string[]>(Worker.SetActiveDocument);

         var questionArg = new Argument<string[]>("question", "Question to ask about the document") { Arity = ArgumentArity.ZeroOrMore };
         var askQuestionCommand = new Command("ask", "Ask a question on the document(s)");
         askQuestionCommand.Add(questionArg);
         askQuestionCommand.Handler = CommandHandler.Create<string[]>(Worker.AskQuestion);

         var fileOpt = new Option<string>(new string[]{ "--file", "-f" }, "Path to the file to process and index (surround with quotes if there are spaces in the name)") { IsRequired = true };
         var modelOpt = new Option<string>(new string[] { "--model", "-m" }, () => "prebuilt-read", $"Model to use for processing the document: {string.Join(", ", DocumentIntelligence.ModelList)}");
         var indexNameOpt = new Option<string>(new string[] { "--index", "-i" }, $"Custom index name, otherwise it will default to the file name");
         var processFileCommand = new Command("process", "Process the file contents against Document Intelligence and add to Azure AI Search index")
         {
            fileOpt,
            modelOpt,
            indexNameOpt
         };
         processFileCommand.Handler = CommandHandler.Create<string, string, string>(Worker.ProcessFile);


         var listCommand = new Command("list", "List the available files to ask questions about");
         listCommand.Handler = CommandHandler.Create(Worker.ListFiles);

         var clearIndexCommand = new Command("clear-index", "Clears the index for the specified files or for all files if \"all\" is used");
         var clearIndexArg = new Argument<string[]>("indexes", "Names of indexes (files) to clear. User \"all\" to delete all indexes") { Arity = ArgumentArity.ZeroOrMore };
         clearIndexCommand.Add(clearIndexArg);
         clearIndexCommand.Handler = CommandHandler.Create<string[]>(Worker.ClearIndex);


         RootCommand rootCommand = new RootCommand(description: $"Utility to ask questions on documents that have been indexed in Azure AI Search");
         rootCommand.Add(questionArg);
         rootCommand.Handler = CommandHandler.Create<string[]>(Worker.AskQuestion);
         rootCommand.Add(docCommand);
         rootCommand.Add(askQuestionCommand);
         rootCommand.Add(processFileCommand);
         rootCommand.Add(listCommand);
         rootCommand.Add(clearIndexCommand);
         rootCommand.Add(AIRuntimeSetCommand());

         var parser = new CommandLineBuilder(rootCommand)
              .UseDefaults()
              .UseHelp(ctx =>
              {
                 ctx.HelpBuilder.CustomizeLayout(_ => HelpBuilder.Default
                                    .GetLayout()
                                    .Prepend(
                                        _ => AnsiConsole.Write(new FigletText("Ask Document Questions"))
                                    ));

              })
              .Build();

         return parser;
      }


      private static Command AIRuntimeSetCommand()
      {
         var chatModelOpt = new Option<string>(new string[] { "--chat-model", "--cm" }, "Name of GPT chat model to use (must match model associated with chat deployment)");
         var chatDepoymentOpt = new Option<string>(new string[] { "--chat-deployment", "--cd" }, "Name of GPT chat deployment to use");

         var embedModelOpt = new Option<string>(new string[] { "--embed-model", "--em" }, "Name of model to use for text embedding (must match model associated with embedding deployment)");
         var embedDepoymentOpt = new Option<string>(new string[] { "--embed-deployment", "--ed" }, "Name of text embedding deployment to use");

         var listAICmd = new Command("list", "List the configured Azure OpenAI settings");
         listAICmd.Handler = CommandHandler.Create(Worker.ListAiSettings);


         var aiSetCmd = new Command("set", "Change the Azure OpenAI model and deployment runtime settings")
         {
            listAICmd,
            chatModelOpt,
            chatDepoymentOpt,
            embedModelOpt,
            embedDepoymentOpt
         };
         aiSetCmd.Handler = CommandHandler.Create<string, string, string, string>(Worker.AzureOpenAiSettings);

         var aiCmd = new Command("ai", "Change or List Azure OpenAI model and deployment runtime settings");
         aiCmd.Add(aiSetCmd);
         aiCmd.Add(listAICmd);
         return aiCmd;
      }

   }
}
