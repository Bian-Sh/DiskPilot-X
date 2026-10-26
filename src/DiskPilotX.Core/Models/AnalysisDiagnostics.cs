namespace DiskPilotX.Core.Models;

public class AnalysisDiagnostics
{
    public string TargetPath { get; set; } = string.Empty;
    public string ScopePath { get; set; } = string.Empty;
    public bool IsFocusedScope { get; set; }
    public int MetadataLength { get; set; }
    public int ResponseLength { get; set; }
    public int ExtractedJsonLength { get; set; }
    public int ParsedRecommendationCount { get; set; }
    public int MalformedRecommendationCount { get; set; }
    public int MissingFieldRecommendationCount { get; set; }
    public int MissingEntryCount { get; set; }
    public int ProtectedFilteredCount { get; set; }
    public string? ParseError { get; set; }
    public string ResponsePreview { get; set; } = string.Empty;
    public string ExtractedJsonPreview { get; set; } = string.Empty;
    public string? RawResponsePath { get; set; }
    public string? ResponseEnvelopePath { get; set; }
    public string? StopReason { get; set; }
    public string? ThinkingPath { get; set; }
}
