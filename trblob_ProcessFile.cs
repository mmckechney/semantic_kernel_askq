using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;  
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json; 
using System;
using System.IO;
using System.Net.Http;  
using System.Text;  
using System.Threading.Tasks;  


namespace Company.Function
{
    public class trblob_ProcessFile
    {
        [FunctionName("BlobTriggerProcessFile")]
        public async Task RunAsync([BlobTrigger("raw/{name}", Connection = "StorageConnectionString")]Stream myBlob, string name, ILogger log)
        {
            try
            {
                log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");
                string subscriptionKey = Environment.GetEnvironmentVariable("DocumentIntelligenceSubscriptionKey") ?? "Default Sub";;
                string endpoint = Environment.GetEnvironmentVariable("DocumentIntelligenceEndpoint") ?? "Default End";

                log.LogInformation($"subkey =  {subscriptionKey}");
                log.LogInformation($"endpoint =  {endpoint}");

                AzureKeyCredential credential = new AzureKeyCredential(subscriptionKey);
                DocumentAnalysisClient client = new DocumentAnalysisClient(new Uri(endpoint), credential);

               string imgUrl = $"https://{Environment.GetEnvironmentVariable("StorageAccount")}.blob.core.windows.net/raw/{name}";

                log.LogInformation(imgUrl);

                Uri fileUri = new Uri(imgUrl);

                AnalyzeDocumentOperation operation = await client.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, "prebuilt-read", fileUri); 

                AnalyzeResult result = operation.Value;
                log.LogInformation("About to get data from document intelligence module.");


                var content = "";
                foreach (DocumentPage page in result.Pages)
                {
                    log.LogInformation("Checking out document data...");
                    for (int i = 0; i < page.Lines.Count; i++)
                    {
                        DocumentLine line = page.Lines[i];
                        log.LogInformation($"  Line {i} has content: '{line.Content}'.");
                        content += line.Content.ToString();

                    }

                    log.LogInformation("content = " + content);
                    
                    // Get the extension of the file  
                    string extension = Path.GetExtension(name); 
                    string nameWithoutExtension = Path.GetFileNameWithoutExtension(name); 
                    string newName = nameWithoutExtension.Replace(".", "_"); 
                    newName += "_" + page.PageNumber.ToString() + ".json";
                    string blobName = nameWithoutExtension + "/" + newName;

                    var jsonObj = new  
                    {  
                        FileName = name, 
                        blobName = blobName, 
                        Content = content  
                    };  
                    string jsonStr = JsonConvert.SerializeObject(jsonObj); 
                    // Save the JSON string to Azure Blob Storage  
                    string connectionString = Environment.GetEnvironmentVariable("StorageConnectionString") ?? "DefaultConnection";
                    string containerName = Environment.GetEnvironmentVariable("ExtractedContainerName") ?? "DefaultContainer";
        
                    BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);  
                    BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);  
                    containerClient.CreateIfNotExists();  
        
                    BlobClient blobClient = containerClient.GetBlobClient(blobName);  

                    using (var stream = new MemoryStream())  
                    {  
                        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonStr);  
                        stream.Write(jsonBytes, 0, jsonBytes.Length);  
                        stream.Seek(0, SeekOrigin.Begin);  
                        blobClient.Upload(stream, overwrite: true);
                         
                    }  
        
                    log.LogInformation($"JSON file {newName} saved to Azure Blob Storage.");  

                }

            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
            }
           


        }

        //POST https://{your-resource-name}.openai.azure.com/openai/deployments/{deployment-id}/embeddings?api-version={api-version}
        //https://mmx-france-openai.openai.azure.com/openai/deployments/text-embedding-ada-002/embeddings?api-version=2023-07-01-preview
        //static async Task<string> GenerateEmbeddings(string content, string endpoint, string apiKey, string deployment, ILogger log)
        //{
        //    try
        //    {
        //        Create the request URL
        //    string url = endpoint + "/openai/deployments/" + deployment + "/embeddings?api-version=2023-07-01-preview";

        //        Create the request body
        //    var requestBody = new
        //    {

        //        input = content
        //    };
        //        var requestBodyJson = JsonConvert.SerializeObject(requestBody);

        //        Create the HTTP client and request
        //    using (var client = new HttpClient())
        //        using (var request = new HttpRequestMessage())
        //        {
        //            Set the request method, headers, and content
        //            request.Method = HttpMethod.Post;
        //            request.RequestUri = new Uri(url);
        //            request.Headers.Add("api-key", apiKey);
        //            request.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");
        //            log.LogInformation(requestBodyJson);
        //            Send the request and get the response
        //           var response = await client.SendAsync(request);
        //            var responseJson = await response.Content.ReadAsStringAsync();
        //            log.LogInformation(responseJson);
        //            Deserialize the response JSON and return the embeddings
        //           dynamic responseObj = JsonConvert.DeserializeObject(responseJson);

        //            log.LogInformation({ responseObj});
        //            log.LogInformation("response string");
        //            log.LogInformation($"{responseObj.data[0].embedding}");
        //            return responseObj.data[0].embedding.ToString();
        //        }

        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine(ex.Message);
        //        return "";
        //    }

        //}

    }
}
