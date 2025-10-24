using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

#nullable enable

namespace DocumentQuestions.Library;

public class Common
{
   private static TokenCredential? tokenCredential;

   private readonly ILogger<Common> log;
   private readonly IConfiguration config;

   public Common(ILogger<Common> log, IConfiguration config)
   {
      this.log = log;
      this.config = config;
   }

   public static TokenCredential EntraTokenCredential => tokenCredential ??= new ChainedTokenCredential(new ManagedIdentityCredential(), new AzureCliCredential());

   public static string SafeIndexName(string fileName, string customIndexName)
   {
      if (!string.IsNullOrWhiteSpace(customIndexName))
      {
         return ReplaceInvalidCharacters(customIndexName);
      }

      if (Uri.TryCreate(fileName, UriKind.RelativeOrAbsolute, out var uri) && uri.IsAbsoluteUri && uri.Scheme != Uri.UriSchemeFile)
      {
         fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
      }
      else
      {
         fileName = Path.GetFileNameWithoutExtension(fileName);
      }

      return ReplaceInvalidCharacters(fileName.ToLowerInvariant());
   }

   public static string BaseFileName(string filePathOrUrl)
   {
      if (Uri.TryCreate(filePathOrUrl, UriKind.RelativeOrAbsolute, out var uri) && uri.IsAbsoluteUri && uri.Scheme != Uri.UriSchemeFile)
      {
         return Path.GetFileNameWithoutExtension(uri.AbsolutePath);
      }

      return Path.GetFileNameWithoutExtension(filePathOrUrl);
   }

   public static string ReplaceInvalidCharacters(string input)
   {
      var sanitized = Path.GetFileNameWithoutExtension(input).ToLowerInvariant();
      sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9-]", "-");
      sanitized = Regex.Replace(sanitized, @"-+$", string.Empty);
      return sanitized.Length > 128 ? sanitized[..128] : sanitized;
   }

   public async Task<string> GetBlobContentAsync(string blobName)
   {
      var storageUrl = config[Constants.STORAGE_ACCOUNT_BLOB_URL] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_BLOB_URL} in configuration.");
      var containerName = config[Constants.EXTRACTED_CONTAINER_NAME] ?? throw new ArgumentException($"Missing {Constants.EXTRACTED_CONTAINER_NAME} in configuration.");

      var blobServiceClient = new BlobServiceClient(new Uri(storageUrl), new DefaultAzureCredential());
      var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

      var blobs = containerClient.GetBlobs(prefix: blobName);
      log.LogInformation("Number of blobs {Count}", blobs.Count());

      var content = string.Empty;
      foreach (var blob in blobs)
      {
         var blobClient = containerClient.GetBlobClient(blob.Name);
         using var stream = await blobClient.OpenReadAsync().ConfigureAwait(false);
         using var reader = new StreamReader(stream);
         var processedFileJson = await reader.ReadToEndAsync().ConfigureAwait(false);
         var chunk = ExtractContent(processedFileJson);
         if (!string.IsNullOrEmpty(chunk))
         {
            content += chunk;
         }
      }

      return content;
   }

   public async Task<Dictionary<string, string>> GetBlobContentDictionaryAsync(string blobName)
   {
      var storageUrl = config[Constants.STORAGE_ACCOUNT_BLOB_URL] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_BLOB_URL} in configuration.");
      var containerName = config[Constants.EXTRACTED_CONTAINER_NAME] ?? throw new ArgumentException($"Missing {Constants.EXTRACTED_CONTAINER_NAME} in configuration.");

      var blobServiceClient = new BlobServiceClient(new Uri(storageUrl), new DefaultAzureCredential());
      var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

      var blobs = containerClient.GetBlobs(prefix: blobName);
      log.LogInformation("Number of blobs {Count}", blobs.Count());

      var aggregatedContent = string.Empty;
      var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      foreach (var blob in blobs)
      {
         var blobClient = containerClient.GetBlobClient(blob.Name);
         using var stream = await blobClient.OpenReadAsync().ConfigureAwait(false);
         using var reader = new StreamReader(stream);
         var blobContent = await reader.ReadToEndAsync().ConfigureAwait(false);
         var chunk = ExtractContent(blobContent);
         if (!string.IsNullOrEmpty(chunk))
         {
            aggregatedContent += chunk;
         }
         results[blob.Name] = aggregatedContent;
      }

      return results;
   }

   public string GetFileName(string name)
   {
      var nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
      return nameWithoutExtension.Replace('.', '_') + ".md";
   }

   public async Task<bool> WriteAnalysisContentToBlob(string name, string content, ILogger logger)
   {
      try
      {
         var newName = GetFileName(name);
         var blobName = Path.GetFileNameWithoutExtension(name) + "/" + newName;

         var storageUrl = config[Constants.STORAGE_ACCOUNT_BLOB_URL] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_BLOB_URL} in configuration.");
         var containerName = config[Constants.EXTRACTED_CONTAINER_NAME] ?? throw new ArgumentException($"Missing {Constants.EXTRACTED_CONTAINER_NAME} in configuration.");

         var blobServiceClient = new BlobServiceClient(new Uri(storageUrl), new DefaultAzureCredential());
         var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
         await containerClient.CreateIfNotExistsAsync().ConfigureAwait(false);

         var blobClient = containerClient.GetBlobClient(blobName);

         await using var stream = new MemoryStream();
         var contentBytes = System.Text.Encoding.UTF8.GetBytes(content);
         await stream.WriteAsync(contentBytes, 0, contentBytes.Length, cancellationToken: default).ConfigureAwait(false);
         stream.Seek(0, SeekOrigin.Begin);
         await blobClient.UploadAsync(stream, overwrite: true).ConfigureAwait(false);

         logger.LogInformation("Markdown file {File} saved to Azure Blob Storage.", newName);
         return true;
      }
      catch (Exception ex)
      {
         logger.LogError(ex, "Unable to save file to blob storage.");
         return false;
      }
   }

   private static string ExtractContent(string? json)
   {
      if (string.IsNullOrEmpty(json))
      {
         return string.Empty;
      }

      try
      {
         using var document = JsonDocument.Parse(json);
         if (document.RootElement.TryGetProperty("content", out var contentProperty) && contentProperty.ValueKind == JsonValueKind.String)
         {
            return contentProperty.GetString() ?? string.Empty;
         }
      }
      catch (JsonException)
      {
         // Ignore malformed JSON payloads.
      }

      return string.Empty;
   }
}
