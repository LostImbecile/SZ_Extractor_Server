namespace SZ_Extractor_Server.Models
{
    public class ExtractRequest
    {
        public string ContentPath { get; set; } = "";
        public string? OutputPath { get; set; }
    }
}