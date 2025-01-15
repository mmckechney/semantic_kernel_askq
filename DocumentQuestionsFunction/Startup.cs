using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Monitor.OpenTelemetry.Exporter;
using DocumentQuestions.Library;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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


         // Build the configuration
         var config = new ConfigurationBuilder()
             .SetBasePath(basePath)
             .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
             .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
             .AddEnvironmentVariables()
             .Build();


         var connectionString = config.GetValue<string>(Constants.APPLICATIONINSIGHTS_CONNECTION_STRING);//?? throw new ArgumentException($"Missing {Constants.APPLICATIONINSIGHTS_CONNECTION_STRING} in configuration");
         ResourceBuilder resourceBuilder = null;
         if (!string.IsNullOrWhiteSpace(connectionString))
         {
            resourceBuilder = ResourceBuilder
                .CreateDefault()
                .AddService("DocumentQuestions.Function");

            // Enable model diagnostics with sensitive data.
            AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

            using var traceProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(resourceBuilder)
                .AddSource("Microsoft.SemanticKernel*")
                .AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString)
                .Build();

            using var meterProvider = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(resourceBuilder)
                .AddMeter("Microsoft.SemanticKernel*")
                .AddAzureMonitorMetricExporter(options => options.ConnectionString = connectionString)
                .Build();
         }

         var builder = new HostBuilder();
         builder.ConfigureLogging((hostContext, logging) =>
         {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter("System", LogLevel.Warning);
            logging.AddFilter("Microsoft", LogLevel.Warning);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
               logging.AddOpenTelemetry(options =>
               {
                  options.SetResourceBuilder(resourceBuilder);
                  options.AddAzureMonitorLogExporter(options => options.ConnectionString = connectionString);
                  // Format log messages. This is default to false.
                  options.IncludeFormattedMessage = true;
                  options.IncludeScopes = true;
               });
            }

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
         services.AddSingleton<IFunctionInvocationFilter, SkFunctionInvocationFilter>();
         services.AddSingleton(sp =>
         {
            var config = sp.GetRequiredService<IConfiguration>();
            var endpoint = config.GetValue<Uri>(Constants.DOCUMENTINTELLIGENCE_ENDPOINT) ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_ENDPOINT} in configuration");
            var key = config.GetValue<string>(Constants.DOCUMENTINTELLIGENCE_KEY) ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_KEY} in configuration");
            return new DocumentIntelligenceClient(endpoint, new AzureKeyCredential(key));
         });
         services.AddHttpClient();

      }

      public static bool IsDevelopmentEnvironment()
      {
         return "Development".Equals(Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT"), StringComparison.OrdinalIgnoreCase);
      }
   }
}
