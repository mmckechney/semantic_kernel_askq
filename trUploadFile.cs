using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Storage.Blobs;

//function to upload document into blob storage.

namespace Company.Function
{
    public static class trUploadFile
    {
        [FunctionName("HttpTriggerUploadFile")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            try
            {
                string connectionString = Environment.GetEnvironmentVariable("RawStorageConnectionString") ?? "DefaultConnection";
                string containerName = Environment.GetEnvironmentVariable("ContainerName") ?? "DefaultContainer";
                var serviceClient = new BlobServiceClient(connectionString);
                var containerClient = serviceClient.GetBlobContainerClient(containerName);

                var formData = await req.ReadFormAsync();
                var file = req.Form.Files["file"];

                Stream myBlob = new MemoryStream();
                myBlob = file.OpenReadStream();
               
                var blobClient = new BlobContainerClient(connectionString, containerName);
                var blob = blobClient.GetBlobClient(file.FileName);
                await blob.UploadAsync(myBlob);


                return new OkObjectResult(file.FileName + " - " + file.Length.ToString() + " bytes");
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex.Message);
            }
        }
    }
}
