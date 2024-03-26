using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
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

         var fileArg = new Argument<string[]>("file", "Path to the file to process and index") { Arity = ArgumentArity.ZeroOrMore };
         var uploadCommand = new Command("process", "Process the file contents against Document Intelligence and add to Azure AI Search index");
         uploadCommand.Add(fileArg);
         uploadCommand.Handler = CommandHandler.Create<string[]>(Worker.ProcessFile);


         var listCommand = new Command("list", "List the available files to ask questions about");
         listCommand.Handler = CommandHandler.Create(Worker.ListFiles);


         RootCommand rootCommand = new RootCommand(description: $"Utility to ask questions on documents that have been indexed in Azure AI Search");
         rootCommand.Add(questionArg);
         rootCommand.Handler = CommandHandler.Create<string[]>(Worker.AskQuestion);
         rootCommand.Add(docCommand);
         rootCommand.Add(askQuestionCommand);
         rootCommand.Add(uploadCommand);
         rootCommand.Add(listCommand);
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
