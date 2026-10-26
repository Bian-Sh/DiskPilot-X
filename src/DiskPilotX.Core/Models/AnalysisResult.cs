namespace DiskPilotX.Core.Models;

public class AnalysisResult
{
    public List<CleanupRecommendation> Recommendations { get; set; } = new();
    public AnalysisDiagnostics Diagnostics { get; set; } = new();
}
