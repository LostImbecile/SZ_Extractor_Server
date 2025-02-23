using System.Text.Json.Serialization;

namespace SZ_Extractor_Server.Models
{
    public class ExtractRequest
    {
        [JsonPropertyName("contentPath")]
        public string ContentPath { get; set; } = "";
        [JsonPropertyName("outputPath")]
        public string? OutputPath { get; set; }
        [JsonPropertyName("archiveName")]
        public string? ArchiveName { get; set; }
    }
}