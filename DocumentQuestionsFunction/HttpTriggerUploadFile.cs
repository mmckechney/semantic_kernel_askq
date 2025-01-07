using Azure.Identity;
using Azure.Storage.Blobs;
using DocumentQuestions.Library;
using HttpMultipartParser;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

//function to upload document into blob storage.

namespace DocumentQuestions.Function
{
   public class HttpTriggerUploadFile
   {
      ILogger<HttpTriggerUploadFile> log;
      IConfiguration config;
      public HttpTriggerUploadFile(ILogger<HttpTriggerUploadFile> log, IConfiguration config)
      {
         this.log = log;
         this.config = config;
      }
      [Function("HttpTriggerUploadFile")]
      public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestData req)
      {
         try
         {
            string storageURL = config[Constants.STORAGE_ACCOUNT_BLOB_URL] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_BLOB_URL} in configuration.");
            string containerName = config[Constants.RAW_CONTAINER_NAME] ?? throw new ArgumentException($"Missing {Constants.RAW_CONTAINER_NAME} in configuration.");
            var blobServiceClient = new BlobServiceClient(new Uri(storageURL), new DefaultAzureCredential());

            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            var multipart = await MultipartFormDataParser.ParseAsync(req.Body);

            if (multipart.Files.First() == null)
            {
               var badResp = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
               badResp.Body = new MemoryStream(Encoding.UTF8.GetBytes("Missing File Data"));
               return badResp;
            }
            var fileContent = multipart.Files.First().Data;
            var fileName = multipart.Files.First().FileName;

            var blob = containerClient.GetBlobClient(fileName);
            await blob.UploadAsync(fileContent);

            var resp = req.CreateResponse(System.Net.HttpStatusCode.OK);
            resp.Body = new MemoryStream(Encoding.UTF8.GetBytes(fileName + " - " + fileContent.Length.ToString() + " bytes"));
            return resp;
         }
         catch (Exception ex)
         {
            var resp = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            resp.Body = new MemoryStream(Encoding.UTF8.GetBytes(ex.Message));
            return resp;
         }
      }
   }
}
