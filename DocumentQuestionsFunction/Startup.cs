#nullable enable

using Azure.Monitor.OpenTelemetry.Exporter;
using DocumentQuestions.Library;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DocumentQuestions.Function;

internal static class Startup
{
   private const string ServiceName = "DocumentQuestions.Function";

   private static readonly string[] AiSources =
   [
      "Microsoft.Agents.AI*",
      "Microsoft.Extensions.AI*",
   ];

   static async Task Main(string[] args)
   {
      var basePath = ResolveBasePath();

      var config = new ConfigurationBuilder()
         .SetBasePath(basePath)
         .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
         .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
         .AddEnvironmentVariables()
         .Build();

      var connectionString = config.GetValue<string?>(Constants.APPLICATIONINSIGHTS_CONNECTION_STRING);
      ResourceBuilder? resourceBuilder = null;

      if (!string.IsNullOrWhiteSpace(connectionString))
      {
         resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(ServiceName);

         using var traceProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource(AiSources)
            .AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString)
            .Build();

         using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter(AiSources)
            .AddAzureMonitorMetricExporter(options => options.ConnectionString = connectionString)
            .Build();
      }

      var builder = new HostBuilder();

      builder.ConfigureLogging((_, logging) =>
      {
         logging.SetMinimumLevel(LogLevel.Debug);
         logging.AddFilter("System", LogLevel.Warning);
         logging.AddFilter("Microsoft", LogLevel.Warning);

         if (!string.IsNullOrWhiteSpace(connectionString))
         {
            logging.AddOpenTelemetry(options =>
            {
               if (resourceBuilder is not null)
               {
                  options.SetResourceBuilder(resourceBuilder);
               }

               options.AddAzureMonitorLogExporter(options => options.ConnectionString = connectionString);
               options.IncludeFormattedMessage = true;
               options.IncludeScopes = true;
            });
         }
      });

      builder.ConfigureFunctionsWorkerDefaults();

      builder.ConfigureAppConfiguration(appConfiguration =>
      {
         appConfiguration
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT")}.json", optional: true, reloadOnChange: false)
            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
      });

      builder.ConfigureServices(ConfigureServices);

      await builder.Build().RunAsync().ConfigureAwait(false);
   }

   private static void ConfigureServices(HostBuilderContext _, IServiceCollection services)
   {
      services.AddSingleton<Common>();
      services.AddSingleton<AgentUtility>();
      services.AddSingleton<Helper>();
      services.AddSingleton<DocumentIntelligence>();
      services.AddHttpClient();
   }

   private static string ResolveBasePath()
   {
      var scriptRoot = Environment.GetEnvironmentVariable("AzureWebJobsScriptRoot");
      if (!string.IsNullOrEmpty(scriptRoot))
      {
         return scriptRoot;
      }

      var home = Environment.GetEnvironmentVariable("HOME");
      if (!string.IsNullOrEmpty(home))
      {
         return Path.Combine(home, "site", "wwwroot");
      }

      return AppContext.BaseDirectory;
   }
}
