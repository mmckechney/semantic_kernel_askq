#nullable enable

using DocumentQuestions.Library;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentQuestions.Function;

public sealed class BlobTriggerProcessFile
{
   private readonly SemanticUtility semanticUtility;
   private readonly ILogger<BlobTriggerProcessFile> log;
   private readonly IConfiguration config;
   private readonly DocumentIntelligence documentIntelligence;

   public BlobTriggerProcessFile(ILogger<BlobTriggerProcessFile> log, IConfiguration config, SemanticUtility semanticUtility, DocumentIntelligence documentIntelligence)
   {
      this.log = log ?? throw new ArgumentNullException(nameof(log));
      this.config = config ?? throw new ArgumentNullException(nameof(config));
      this.semanticUtility = semanticUtility ?? throw new ArgumentNullException(nameof(semanticUtility));
      this.documentIntelligence = documentIntelligence ?? throw new ArgumentNullException(nameof(documentIntelligence));
   }

   [Function("BlobTriggerProcessFile")]
   public async Task RunAsync(
      [BlobTrigger("raw/{name}", Connection = "STORAGE_ACCOUNT_BLOB_URL")] Stream blobStream,
      string name,
      CancellationToken cancellationToken)
   {
      ArgumentNullException.ThrowIfNull(blobStream);

      try
      {
         log.LogInformation("Processing blob {BlobName}", name);

         var storageAccountName = config[Constants.STORAGE_ACCOUNT_NAME] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_NAME} in configuration.");
         var collectionName = Path.GetFileNameWithoutExtension(name);

         semanticUtility.ReloadAgentResources();

         var fileUri = new Uri($"https://{storageAccountName}.blob.core.windows.net/raw/{name}");

         log.LogInformation("Submitting document {DocumentUri} for analysis.", fileUri);
         await documentIntelligence.ProcessDocument(fileUri, indexName: collectionName, cancellationToken: cancellationToken).ConfigureAwait(false);

         log.LogInformation("Document Intelligence processing completed for {BlobName}.", name);
      }
      catch (Exception ex)
      {
         log.LogError(ex, "Unhandled exception while processing blob {BlobName}.", name);
      }
   }
}
