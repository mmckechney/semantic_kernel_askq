using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Company.Function.Models
{
    internal class ProcessedFile
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; }

        [JsonPropertyName("blobName")]
        public string BlobName { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }
    }
}
