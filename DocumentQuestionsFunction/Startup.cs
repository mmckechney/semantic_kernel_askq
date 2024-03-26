using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using DocumentQuestions.Library;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace DocumentQuestions.Function
{
   internal class Startup
   {
      static async Task Main(string[] args)
      {
         string basePath = IsDevelopmentEnvironment() ?
             Environment.GetEnvironmentVariable("AzureWebJobsScriptRoot") :
             $"{Environment.GetEnvironmentVariable("HOME")}\\site\\wwwroot";

         var builder = new HostBuilder();
         builder.ConfigureLogging((hostContext, logging) =>
         {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter("System", LogLevel.Warning);
            logging.AddFilter("Microsoft", LogLevel.Warning);

         });
         builder.ConfigureFunctionsWorkerDefaults();
         builder.ConfigureAppConfiguration(b =>
         {
            b.SetBasePath(basePath)
              .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)  // common settings go here.
              .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT")}.json", optional: true, reloadOnChange: false)  // environment specific settings go here
              .AddJsonFile("local.settings.json", optional: true, reloadOnChange: false)  // secrets go here. This file is excluded from source control.
              .AddEnvironmentVariables()
              .Build();

         });
         // builder.AddAzureStorage();

         builder.ConfigureServices(ConfigureServices);


         await builder.Build().RunAsync();
      }

      private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
      {
         services.AddSingleton<Common>();
         services.AddSingleton<SemanticUtility>();
         services.AddSingleton<Helper>();
         services.AddSingleton(sp =>
         {
            var config = sp.GetRequiredService<IConfiguration>();

            var endpoint = config.GetValue<Uri>("DocumentIntelligenceEndpoint");
            var apiKey = config.GetValue<string>("DocumentIntelligenceSubscriptionKey");

            var credentials = new AzureKeyCredential(apiKey);

            return new DocumentAnalysisClient(endpoint, credentials);
         });
         services.AddHttpClient();

      }

      public static bool IsDevelopmentEnvironment()
      {
         return "Development".Equals(Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT"), StringComparison.OrdinalIgnoreCase);
      }
   }
}
