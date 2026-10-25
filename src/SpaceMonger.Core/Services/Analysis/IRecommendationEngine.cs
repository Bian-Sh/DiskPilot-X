using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Analysis;

public interface IRecommendationEngine
{
    Task<List<CleanupRecommendation>> AnalyzeAsync(ScanSession session, ModelEndpointConfig endpoint, string? responseLanguage, CancellationToken cancellationToken, FileEntry? focusEntry = null);
    Task<AnalysisResult> AnalyzeWithDiagnosticsAsync(ScanSession session, ModelEndpointConfig endpoint, string? responseLanguage, CancellationToken cancellationToken, FileEntry? focusEntry = null);
}
