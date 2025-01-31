using CUE4Parse.UE4.Versions;
using System.Text.Json.Serialization;

namespace SZ_Extractor.Models
{
    public class ApiOptions
    {
        [JsonPropertyName("engineVersion")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public EGame EngineVersion { get; set; }

        [JsonPropertyName("aesKey")]
        public string AesKey { get; set; } = "";

        [JsonPropertyName("gameDir")]
        public string GameDir { get; set; } = "";

        [JsonPropertyName("outputPath")]
        public string OutputPath { get; set; } = "Output";

        public void Validate()
        {
            if (!Directory.Exists(GameDir))
                throw new ArgumentException($"Invalid game directory: {GameDir}");

            if (string.IsNullOrWhiteSpace(AesKey))
                throw new ArgumentException("AES key is required");
        }
    }
}