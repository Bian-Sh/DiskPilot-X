using FluentAssertions;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

public class RecommendationsViewModelTests
{
    [Fact]
    public void ExternalRecommendationWaitingTitle_RefreshesWhenLanguageChanges()
    {
        var originalLanguage = L.CurrentLanguageName;
        try
        {
            L.SetLanguage("en");
            var viewModel = new RecommendationsViewModel();

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
    public void ExternalRecommendations_AllFiltersShowAllRecommendationsAndSelectionTotalsUpdate()
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
        var viewModel = new RecommendationsViewModel();
        viewModel.SetExternalRecommendations(recommendations);

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
}
