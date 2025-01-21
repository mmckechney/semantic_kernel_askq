using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
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
         string endpoint = config[Constants.AISEARCH_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_ENDPOINT} in configuration");
         string key = config[Constants.AISEARCH_KEY] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_KEY} in configuration");
         // Create a client
         client = new SearchIndexClient(new Uri(endpoint), new AzureKeyCredential(key));
      }
      public async Task<List<string>> ListAvailableIndexes(bool unquoted = false)
      {
         try
         {
            List<string> names = new();
            await foreach (var page in client.GetIndexNamesAsync())
            {
               if (unquoted)
               {
                  names.Add(page);
               }
               else
               {
                  names.Add($"\"{page}\"");
               }
            }
            return names;
         }
         catch (Exception exe)
         {
            log.LogError($"Problem retrieving AI Search Idexes:\r\n{exe.Message}");
            return new List<string>();
         }
      }

      public async Task<List<string>> ClearIndexes(List<string> indexNames)
      {
         List<string> deleted = new();
         var available = await ListAvailableIndexes(true);
         if (indexNames.Contains("all", StringComparer.CurrentCultureIgnoreCase))
         {
            indexNames = await ListAvailableIndexes(true);
         }

         foreach (var index in indexNames)
         {
            if (available.Contains(index, StringComparer.CurrentCultureIgnoreCase))
            {
               try
               {
                  var result = await client.DeleteIndexAsync(index);
                  if (result.Status < 300)
                  {
                     deleted.Add(index);
                  }else
                  {
                     log.LogError($"Problem deleting index {index}:\r\n{result.ReasonPhrase}");
                  }
               }
               catch (Exception exe)
               {
                  log.LogError($"Problem deleting index {index}:\r\n{exe.Message}");
               }
            }
            else
            {
               log.LogWarning($"The file index '{index}' was not found.");
            }
         }
         return deleted;
      }
   }
}
