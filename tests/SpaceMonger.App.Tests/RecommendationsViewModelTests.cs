using FluentAssertions;
using SpaceMonger.App.Localization;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Analysis;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.App.Tests;

public class RecommendationsViewModelTests
{
    [Fact]
    public void ExternalRecommendationWaitingTitle_RefreshesWhenLanguageChanges()
    {
        var originalLanguage = L.CurrentLanguageName;
        try
        {
            L.SetLanguage("en");
            var viewModel = new RecommendationsViewModel(new StubRecommendationEngine([]));

            viewModel.BeginExternalRecommendationLoad();
            viewModel.EmptyStateTitleText.Should().Be("AI is analyzing. Please wait...");

            L.SetLanguage("zh-CN");

            viewModel.EmptyStateTitleText.Should().Contain("AI正在分析");
        }
        finally
        {
            L.SetLanguage(originalLanguage);
        }
    }

    [Fact]
    public async Task AnalyzeCommand_AllFiltersShowAllRecommendationsAndSelectionTotalsUpdate()
    {
        var recommendations = new List<CleanupRecommendation>
        {
            new()
            {
                Id = "1",
                TargetPath = @"C:\Temp\a.tmp",
                Size = 1024,
                Category = RecommendationCategory.TemporaryFiles,
                SafetyRating = SafetyRating.Safe,
            },
            new()
            {
                Id = "2",
                TargetPath = @"C:\Logs\b.log",
                Size = 2048,
                Category = RecommendationCategory.LogFiles,
                SafetyRating = SafetyRating.ReviewFirst,
            },
        };
        var viewModel = new RecommendationsViewModel(new StubRecommendationEngine(recommendations));
        viewModel.SetContext(
            new ScanSession
            {
                TargetPath = @"C:\",
                RootEntry = new FileEntry { Path = @"C:\", Name = @"C:\", IsDirectory = true }
            },
            new ModelEndpointConfig { ApiKey = "api-key", Model = "stub-model" },
            "zh-CN");

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        viewModel.FilteredRecommendations.Should().HaveCount(2);

        viewModel.SelectedCategoryFilter = RecommendationCategory.TemporaryFiles;
        viewModel.FilteredRecommendations.Should().ContainSingle(r => r.Id == "1");

        viewModel.SelectedCategoryFilter = string.Empty;
        viewModel.SelectedSafetyFilter = string.Empty;
        viewModel.FilteredRecommendations.Should().HaveCount(2);

        recommendations[0].IsAccepted = true;
        viewModel.UpdateTotals();

        viewModel.TotalSelectedCount.Should().Be(1);
        viewModel.TotalRecoverableSpace.Should().Be("1.0 KB");
    }

    private sealed class StubRecommendationEngine(List<CleanupRecommendation> recommendations) : IRecommendationEngine
    {
        public Task<List<CleanupRecommendation>> AnalyzeAsync(
            ScanSession session,
            ModelEndpointConfig endpoint,
            string? responseLanguage,
            CancellationToken cancellationToken,
            FileEntry? focusEntry = null) => Task.FromResult(recommendations);

        public Task<AnalysisResult> AnalyzeWithDiagnosticsAsync(
            ScanSession session,
            ModelEndpointConfig endpoint,
            string? responseLanguage,
            CancellationToken cancellationToken,
            FileEntry? focusEntry = null) => Task.FromResult(new AnalysisResult { Recommendations = recommendations, Diagnostics = new AnalysisDiagnostics() });
    }
}
