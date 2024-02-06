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
         var questionOpt = new Option<string[]>(new string[] { "--question", "-q" }, "Question to ask about the document") { IsRequired = true };
         var questionArg = new Argument<string[]>("question", "Question to ask about the document") { Arity = ArgumentArity.ZeroOrMore };
         var docOpt = new Option<string>(new string[] { "--doc", "-d" },() => "", "Name of the document to inquire about.") { IsRequired = true };
        
         var askQuestionCommand = new Command("ask", "Ask a question on the document(s)");
         askQuestionCommand.Add(questionArg);
         askQuestionCommand.Add(docOpt);
         askQuestionCommand.Handler = CommandHandler.Create<string[], string>(Worker.AskQuestion);

         var fileOpt = new Option<FileInfo>(new string[] { "--file", "-f" }, "File to process") { IsRequired = true };
         var uploadCommand = new Command("process", "Process the file contents against Document Intelligence and add to Azure AI Search index");
         uploadCommand.Add(fileOpt);
         uploadCommand.Handler = CommandHandler.Create<FileInfo>(Worker.ProcessFile);


         var listCommand = new Command("list", "List the available files to ask questions about");
         listCommand.Handler = CommandHandler.Create(Worker.ListFiles);

         var generateCount = new Argument<int>("count", "Number of random addreses to generate and add to AI Search Index") { Arity = ArgumentArity.ExactlyOne };
         var generateCommand = new Command("generate", "Uses Azure OpenAI to generate random addresses to add to Azure AI Search");
         generateCommand.Add(generateCount);
         //generateCommand.Handler = CommandHandler.Create<int>(Worker.GenerateAddresses);


         RootCommand rootCommand = new RootCommand(description: $"Utility to ask questions on documents that have been indexed in Azure AI Search");
         rootCommand.Add(questionArg);
         rootCommand.Add(docOpt);
         rootCommand.Handler = CommandHandler.Create<string[], string>(Worker.AskQuestion);
         rootCommand.Add(askQuestionCommand);
         rootCommand.Add(uploadCommand);
         rootCommand.Add(listCommand);

         //rootCommand.Add(searchCommand);
         //rootCommand.Add(generateCommand);
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


   }
}
