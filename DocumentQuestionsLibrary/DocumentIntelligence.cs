#nullable enable

using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DocumentQuestions.Library;

public class DocumentIntelligence
{
   public static IReadOnlyList<string> ModelList { get; } = new[]
   {
      "prebuilt-layout",
      "prebuilt-read",
      "prebuilt-mortgage.us.1003",
      "prebuilt-mortgage.us.1004",
      "prebuilt-mortgage.us.1005",
      "prebuilt-mortgage.us.1008",
      "prebuilt-mortgage.us.closingDisclosure",
      "prebuilt-tax.us",
      "prebuilt-idDocument",
   };

   private readonly DocumentIntelligenceClient docIntelClient;
   private readonly ILogger<DocumentIntelligence> log;
   private readonly AgentUtility semanticUtility;
   private readonly Common common;

   public DocumentIntelligence(ILogger<DocumentIntelligence> log, IConfiguration config, AgentUtility semanticUtility, Common common)
   {
      this.log = log ?? throw new ArgumentNullException(nameof(log));
      this.semanticUtility = semanticUtility ?? throw new ArgumentNullException(nameof(semanticUtility));
      this.common = common ?? throw new ArgumentNullException(nameof(common));

      if (config is null)
      {
         throw new ArgumentNullException(nameof(config));
      }

      var endpoint = config.GetValue<Uri>(Constants.DOCUMENTINTELLIGENCE_ENDPOINT) ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_ENDPOINT} in configuration");
      var key = config.GetValue<string>(Constants.DOCUMENTINTELLIGENCE_KEY) ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_KEY} in configuration");
      docIntelClient = new DocumentIntelligenceClient(endpoint, new AzureKeyCredential(key));
   }

   public async Task ProcessDocument(Uri fileUri, string modelId = "prebuilt-layout", string indexName = "", CancellationToken cancellationToken = default)
   {
      if (fileUri is null)
      {
         throw new ArgumentNullException(nameof(fileUri));
      }

      log.LogInformation("Analyzing document {Document} with model ID: {Model}", fileUri, modelId);
      var options = new AnalyzeDocumentOptions(modelId: modelId, uriSource: fileUri)
      {
         OutputContentFormat = DocumentContentFormat.Markdown,
      };

      var operation = await docIntelClient.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken).ConfigureAwait(false);
      await ProcessDocumentResults(operation.Value, fileUri.AbsoluteUri, indexName, cancellationToken).ConfigureAwait(false);
   }

   public async Task ProcessDocument(FileInfo file, string modelId = "prebuilt-layout", string indexName = "", CancellationToken cancellationToken = default)
   {
      if (file is null)
      {
         throw new ArgumentNullException(nameof(file));
      }

      using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
      log.LogInformation("Analyzing document {Document} with model ID: {Model}", file.FullName, modelId);
      var binaryDoc = BinaryData.FromStream(stream);
      var options = new AnalyzeDocumentOptions(modelId: modelId, bytesSource: binaryDoc)
      {
         OutputContentFormat = DocumentContentFormat.Markdown,
      };

      var operation = await docIntelClient.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken).ConfigureAwait(false);
      await ProcessDocumentResults(operation.Value, file.FullName, indexName, cancellationToken).ConfigureAwait(false);
   }

   public async Task ProcessDocumentResults(AnalyzeResult result, string filePathOrUrl, string indexName, CancellationToken cancellationToken = default)
   {
      if (result is null)
      {
         throw new ArgumentNullException(nameof(result));
      }

      indexName = Common.SafeIndexName(filePathOrUrl, indexName);
      var content = result.Content ?? string.Empty;
      var contentLines = content.Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

      log.LogInformation("Writing document markdown to blob storage...");
      await common.WriteAnalysisContentToBlob(indexName, content, log).ConfigureAwait(false);

      log.LogInformation("Parsing Document Intelligence results...");
      var chunked = SplitPlainTextParagraphs(contentLines, 8191);

      log.LogInformation("Saving Document Intelligence results to Azure AI Search indexes...");
      var fileName = Common.BaseFileName(filePathOrUrl);
      await Task.WhenAll(
         semanticUtility.StoreMemoryAsync(indexName, fileName, chunked, cancellationToken)
      // semanticUtility.StoreMemoryAsync("general", fileName, chunked, cancellationToken)
      ).ConfigureAwait(false);

      log.LogInformation("Document {Document} processed and indexed.", fileName);
   }

   private static IReadOnlyList<string> SplitPlainTextParagraphs(IEnumerable<string> lines, int maxLength)
   {
      var chunks = new List<string>();
      var current = new StringBuilder();

      foreach (var rawLine in lines)
      {
         var line = rawLine ?? string.Empty;

         if (current.Length + line.Length + Environment.NewLine.Length > maxLength && current.Length > 0)
         {
            chunks.Add(current.ToString());
            current.Clear();
         }

         if (line.Length > maxLength)
         {
            chunks.Add(line);
            continue;
         }

         current.AppendLine(line);
      }

      if (current.Length > 0)
      {
         chunks.Add(current.ToString());
      }

      return chunks;
   }
}
