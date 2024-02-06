using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace DocumentQuestions.Library
{
   public class AiSearch
   {
      SearchIndexClient client;
      ILogger<AiSearch> log;
      IConfiguration config;
      public AiSearch(ILogger<AiSearch> log, IConfiguration config)
      {
         this.log = log;
         this.config = config;
         string endpoint = config["CognitiveSearchEndpoint"] ?? throw new ArgumentException("Missing CognitiveSearchEndpoint in configuration");
         string key = config["CognitiveSearchAdminKey"] ?? throw new ArgumentException("Missing CognitiveSearchAdminKey in configuration");

         // Create a client
         AzureKeyCredential credential = new AzureKeyCredential(key);
         client = new SearchIndexClient(new Uri(endpoint), credential);
      }
      public async Task<List<string>> ListAvailableIndexes()
      {
         List<string> names = new();
        await foreach(var page in client.GetIndexNamesAsync())
            {
            names.Add($"\"{page}\"");
         }
         return names;
      }
   }
}
