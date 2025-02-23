using System.Text.Json.Serialization;

namespace SZ_Extractor_Server.Models
{
    public class DumpRequest
    {
        [JsonPropertyName("filter")]
        public string Filter { get; set; } = "";
    }
}