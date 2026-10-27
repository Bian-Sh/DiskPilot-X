using System.Xml.Linq;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Analysis;
using FluentAssertions;

namespace DiskPilotX.App.Tests;

public class AnalysisPresentationTests
{
    [Fact]
    public void InitialEmptyState_IsNotStartedRatherThanNoRecommendations()
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();

            viewModel.EmptyStateTitleText.Should().Be("Analysis not started");
            viewModel.EmptyStateDescriptionText.Should().Be("Start an analysis to assess the selected scope.");
            viewModel.AnalysisStatusText.Should().Be("Analysis not started");
            viewModel.AnalysisTask.Should().BeNull();
        });
    }

    [Fact]
    public void BeginAnalysis_PreservesPreviousRecommendationsSelectionFiltersAndDiagnostics()
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();
            var previous = Recommendation();
            previous.IsAccepted = true;
            var diagnostics = new AnalysisDiagnostics();
            viewModel.SetExternalRecommendations([previous], diagnostics);
            viewModel.SelectedCategoryFilter = RecommendationCategory.TemporaryFiles;
            viewModel.SelectedSafetyFilter = SafetyRating.Safe;
            var recommendations = viewModel.Recommendations;
            var filtered = viewModel.FilteredRecommendations;
            viewModel.AnalysisError = "上次请求异常";

            viewModel.BeginAnalysis();

            viewModel.Recommendations.Should().BeSameAs(recommendations);
            viewModel.FilteredRecommendations.Should().BeSameAs(filtered);
            viewModel.FilteredRecommendations.Should().ContainSingle().Which.Should().BeSameAs(previous);
            viewModel.HasRecommendations.Should().BeTrue();
            viewModel.HasAcceptedRecommendations.Should().BeTrue();
            viewModel.TotalSelectedCount.Should().Be(1);
            viewModel.TotalRecoverableSpace.Should().Be("1.0 KB");
            viewModel.LastDiagnostics.Should().BeSameAs(diagnostics);
            viewModel.AnalysisError.Should().BeNull();
            viewModel.IsAnalyzing.Should().BeTrue();
            viewModel.AnalysisStatusText.Should().Contain("previous analysis").And.NotContain("completed");
        });
    }

    [Theory]
    [InlineData(AnalysisTaskStatus.NotStarted, AnalysisOutcome.Unknown, "Analysis not started", "Start an analysis")]
    [InlineData(AnalysisTaskStatus.Running, AnalysisOutcome.Unknown, "AI is analyzing. Please wait...", "Assessing the current scope")]
    [InlineData(AnalysisTaskStatus.Running, AnalysisOutcome.Recommendations, "AI is analyzing. Please wait...", "Assessing the current scope")]
    [InlineData(AnalysisTaskStatus.WaitingForUser, AnalysisOutcome.Unknown, "Waiting for your input", "Answer the assistant")]
    [InlineData(AnalysisTaskStatus.Completed, AnalysisOutcome.NeedsInput, "Waiting for your input", "Answer the assistant")]
    [InlineData(AnalysisTaskStatus.Completed, AnalysisOutcome.Unknown, "Analysis result not confirmed", "No complete assessment")]
    [InlineData(AnalysisTaskStatus.Completed, AnalysisOutcome.Recommendations, "Analysis completed", "recommendations are available")]
    [InlineData(AnalysisTaskStatus.Completed, AnalysisOutcome.NoRecommendations, "No cleanup recommendations", "fully assessed")]
    [InlineData(AnalysisTaskStatus.Completed, AnalysisOutcome.Partial, "Analysis partially completed", "could not be assessed")]
    [InlineData(AnalysisTaskStatus.Completed, AnalysisOutcome.Restricted, "Analysis restricted", "Protection or exclusion")]
    [InlineData(AnalysisTaskStatus.Failed, AnalysisOutcome.NoRecommendations, "Analysis failed", "did not complete")]
    [InlineData(AnalysisTaskStatus.Cancelled, AnalysisOutcome.Recommendations, "Analysis cancelled", "cancelled before completion")]
    public void EmptyStates_RespectLifecycleBeforeOutcome(AnalysisTaskStatus status, AnalysisOutcome outcome, string title, string description)
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();
            viewModel.SetAnalysisTask(TaskResult(status, outcome));

            viewModel.EmptyStateTitleText.Should().Be(title);
            viewModel.EmptyStateDescriptionText.Should().Contain(description);
            viewModel.AnalysisStatusText.Should().Contain(title).And.Contain(@"D:\scope").And.Contain("Unassessed: 0");
            viewModel.IsAnalyzing.Should().Be(status == AnalysisTaskStatus.Running);
            viewModel.IsWaitingForExternalRecommendations.Should().Be(status == AnalysisTaskStatus.Running);
            viewModel.Recommendations.Should().BeEmpty();
        });
    }

    [Fact]
    public void SetAnalysisTask_PreservesSnapshotAndNotifiesPresentationBindings()
    {
        var viewModel = new RecommendationsViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var task = TaskResult(AnalysisTaskStatus.Running, AnalysisOutcome.Unknown);

        viewModel.SetAnalysisTask(task);

        viewModel.AnalysisTask.Should().BeSameAs(task);
        viewModel.AnalysisScopePath.Should().Be(@"D:\scope");
        changed.Should().Contain(new[] { nameof(viewModel.AnalysisTask), nameof(viewModel.AnalysisScopePath),
            nameof(viewModel.AnalysisStatusText), nameof(viewModel.EmptyStateTitleText), nameof(viewModel.EmptyStateDescriptionText) });
    }

    [Theory]
    [InlineData(AnalysisTaskStatus.Failed, "Analysis failed")]
    [InlineData(AnalysisTaskStatus.Cancelled, "Analysis cancelled")]
    public void UnsuccessfulAnalysis_DoesNotPresentPreviousResultsAsCurrentSuccess(AnalysisTaskStatus status, string title)
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();
            viewModel.SetExternalRecommendations([Recommendation()]);
            viewModel.SetAnalysisTask(TaskResult(AnalysisTaskStatus.Completed, AnalysisOutcome.Recommendations));
            viewModel.BeginAnalysis();
            viewModel.SetAnalysisTask(TaskResult(status, AnalysisOutcome.Recommendations));

            viewModel.Recommendations.Should().ContainSingle();
            viewModel.AnalysisStatusText.Should().Contain(title).And.Contain("previous analysis").And.NotContain("Analysis completed");
            viewModel.IsAnalyzing.Should().BeFalse();
        });
    }

    [Fact]
    public void DifferentTaskWithoutBegin_StillIdentifiesPreviousRecommendations()
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();
            viewModel.SetAnalysisTask(TaskResult(AnalysisTaskStatus.Completed, AnalysisOutcome.Recommendations));
            viewModel.SetExternalRecommendations([Recommendation()]);

            viewModel.SetAnalysisTask(TaskResult(AnalysisTaskStatus.Failed, AnalysisOutcome.Unknown));

            viewModel.AnalysisStatusText.Should().Contain("previous analysis");
        });
    }

    [Theory]
    [InlineData(AnalysisTaskStatus.Failed)]
    [InlineData(AnalysisTaskStatus.Cancelled)]
    public void UnsuccessfulTaskWithoutPriorSnapshot_DoesNotClaimUnattributedResults(AnalysisTaskStatus status)
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();
            viewModel.SetExternalRecommendations([Recommendation()]);

            viewModel.SetAnalysisTask(TaskResult(status, AnalysisOutcome.Unknown));

            viewModel.AnalysisStatusText.Should().Contain("previous analysis");
        });
    }

    [Fact]
    public void FailureAfterCurrentTaskPublished_DoesNotRelabelCurrentResultsAsPrevious()
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();
            var task = TaskResult(AnalysisTaskStatus.Running, AnalysisOutcome.Unknown);
            viewModel.SetAnalysisTask(task);
            viewModel.SetExternalRecommendations([Recommendation()]);

            viewModel.SetAnalysisTask(task with { Status = AnalysisTaskStatus.Failed });

            viewModel.AnalysisStatusText.Should().Contain("Analysis failed").And.NotContain("previous analysis");
        });
    }

    [Theory]
    [InlineData(AnalysisOutcome.Recommendations)]
    [InlineData(AnalysisOutcome.NoRecommendations)]
    public void UnassessedItems_PreventFalseCompleteAssessment(AnalysisOutcome outcome)
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();
            viewModel.SetExternalRecommendations([Recommendation()]);
            viewModel.SetAnalysisTask(TaskResult(AnalysisTaskStatus.Completed, outcome,
                ["需要确认保留周期", "权限不足"]));

            viewModel.EmptyStateTitleText.Should().Be("Analysis partially completed");
            viewModel.EmptyStateDescriptionText.Should().Contain("需要确认保留周期").And.Contain("权限不足");
            viewModel.AnalysisStatusText.Should().Contain("Unassessed: 2").And.Contain("权限不足");
        });
    }

    [Fact]
    public void ExternalResults_ReplacePreviousResultsWithoutCompletingRunningTaskOrChangingFilters()
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();
            viewModel.SetExternalRecommendations([Recommendation()]);
            viewModel.SelectedSafetyFilter = SafetyRating.Safe;
            viewModel.BeginAnalysis();
            var task = TaskResult(AnalysisTaskStatus.Running, AnalysisOutcome.Unknown);
            viewModel.SetAnalysisTask(task);
            var current = Recommendation();
            current.Id = string.Empty;
            current.IsAccepted = true;

            viewModel.SetExternalRecommendations([current]);

            current.Id.Should().Be("1");
            viewModel.Recommendations.Should().ContainSingle().Which.Should().BeSameAs(current);
            viewModel.FilteredRecommendations.Should().ContainSingle().Which.Should().BeSameAs(current);
            viewModel.SelectedSafetyFilter.Should().Be(SafetyRating.Safe);
            viewModel.TotalSelectedCount.Should().Be(1);
            viewModel.AnalysisTask.Should().BeSameAs(task);
            viewModel.IsAnalyzing.Should().BeTrue();
            viewModel.AnalysisStatusText.Should().NotContain("previous analysis").And.NotContain("Analysis completed");

            viewModel.SetAnalysisTask(task with { Status = AnalysisTaskStatus.Completed, Outcome = AnalysisOutcome.Recommendations });
            viewModel.AnalysisStatusText.Should().Contain("Analysis completed").And.NotContain("previous analysis");
        });
    }

    [Fact]
    public void EmptyExternalResults_AloneDoNotProveNoRecommendations()
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();
            viewModel.SetExternalRecommendations([]);

            viewModel.EmptyStateTitleText.Should().Be("Analysis result not confirmed");
        });
    }

    [Theory]
    [InlineData("zh-CN", "分析部分完成", "未评估：2")]
    [InlineData("zh-TW", "分析部分完成", "未評估：2")]
    public void LanguageChanges_RefreshTaskStatusAndEmptyPresentation(string language, string title, string count)
    {
        WithLanguage("en", () =>
        {
            var viewModel = new RecommendationsViewModel();
            viewModel.SetAnalysisTask(TaskResult(AnalysisTaskStatus.Completed, AnalysisOutcome.Partial, ["待确认", "待评估"]));
            var changed = new List<string?>();
            viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            L.SetLanguage(language);

            viewModel.EmptyStateTitleText.Should().Be(title);
            viewModel.EmptyStateDescriptionText.Should().NotContain("Some items");
            viewModel.AnalysisStatusText.Should().Contain(title).And.Contain(count).And.Contain(@"D:\scope");
            changed.Should().Contain(new[] { nameof(viewModel.AnalysisStatusText), nameof(viewModel.EmptyStateTitleText), nameof(viewModel.EmptyStateDescriptionText) });
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Panel_StatusRemainsVisibleWithOrWithoutRecommendations(bool hasRecommendations) => RunSta(() =>
    {
        WithLanguage("en", () =>
        {
            var panel = LoadPanel();
            var host = panel;
            var viewModel = new RecommendationsViewModel();
            if (hasRecommendations) viewModel.SetExternalRecommendations([Recommendation()]);
            panel.DataContext = viewModel;
            viewModel.BeginAnalysis();
            viewModel.SetAnalysisTask(TaskResult(AnalysisTaskStatus.Running, AnalysisOutcome.Unknown, ["等待确认"]));
            Layout(host);

            var status = panel.FindName("AnalysisStatus") as TextBlock;
            status.Should().NotBeNull();
            status!.Visibility.Should().Be(Visibility.Visible);
            status.ActualHeight.Should().BeGreaterThan(0);
            status.Text.Should().Contain("Unassessed: 1");
            var list = (ListView)panel.FindName("RecommendationsList");
            list.Items.Count.Should().Be(hasRecommendations ? 1 : 0);
            var overlay = (Border)panel.FindName("AnalyzingOverlay");
            overlay.Visibility.Should().Be(hasRecommendations ? Visibility.Collapsed : Visibility.Visible);

            viewModel.SetAnalysisTask(TaskResult(AnalysisTaskStatus.Cancelled, AnalysisOutcome.Unknown));
            Layout(host);
            status.Text.Should().Contain("Analysis cancelled");
            if (hasRecommendations) status.Text.Should().Contain("previous analysis");
        });
    });

    [Theory]
    [InlineData("en", "Draft rule request")]
    [InlineData("zh-CN", "填写规则创建请求")]
    [InlineData("zh-TW", "填寫規則建立請求")]
    public void RuleRequestButton_HasLocalizedLabel(string language, string expected)
    {
        WithLanguage(language, () => L.Text("ChatRuleCreationDraftButton").Should().Be(expected));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public void AnalysisTools_HaveDisplayLabelsInsteadOfResourceKeys(string language)
    {
        WithLanguage(language, () =>
        {
            foreach (var tool in new[]
            {
                "list_analysis_rules", "read_analysis_rule", "preview_analysis_rule", "get_analysis_context",
                "complete_analysis", "request_rule_creation", "manage_analysis_memory", "read_analysis_history"
            })
            {
                var label = L.Text($"CopilotTool_{tool}");
                label.Should().NotBeNullOrWhiteSpace();
                label.Should().NotStartWith("!");
                label.Should().NotContain(tool);
            }
        });
    }

    private static CleanupRecommendation Recommendation() => new()
    {
        Id = "old", TargetPath = @"D:\scope\cache.tmp", Size = 1024,
        Category = RecommendationCategory.TemporaryFiles, SafetyRating = SafetyRating.Safe
    };

    private static AnalysisTaskResult TaskResult(AnalysisTaskStatus status, AnalysisOutcome outcome, IReadOnlyList<string>? unassessed = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), @"D:\scope", status, outcome, 0, unassessed ?? [], new Dictionary<string, string> { ["cleanup"] = "v1" });

    private static UserControl LoadPanel()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src/DiskPilotX.App/Views/RecommendationsPanel.xaml")))
            directory = directory.Parent;
        if (directory is null) throw new FileNotFoundException("未找到当前工作树的推荐面板。");
        var document = XDocument.Load(Path.Combine(directory.FullName, "src/DiskPilotX.App/Views/RecommendationsPanel.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        document.Root!.Attribute(xaml + "Class")!.Remove();
        foreach (var attribute in document.Root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration &&
                     attribute.Value.StartsWith("clr-namespace:DiskPilotX.App.")).ToList())
            attribute.Value += ";assembly=DiskPilot-X";
        // 只移除依赖代码隐藏的事件；实际控件、模板、触发器和绑定均由 WPF 加载执行。
        foreach (var attribute in document.Descendants().Attributes().Where(attribute =>
                     attribute.Name.LocalName is "Click" or "MouseDoubleClick" or "Checked" or "Unchecked").ToList()) attribute.Remove();
        document.Descendants(presentation + "EventSetter").Remove();
        foreach (var element in document.Descendants().Where(element => element.Name.NamespaceName.StartsWith("clr-namespace:DiskPilotX.App.")).ToList())
            element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=DiskPilot-X");
        var resources = document.Root.Element(presentation + "UserControl.Resources")!;
        var dictionary = new XElement(presentation + "ResourceDictionary",
            new XElement(presentation + "ResourceDictionary.MergedDictionaries",
                new XElement(presentation + "ResourceDictionary", new XAttribute("Source", "/DiskPilot-X;component/Themes/VisionProTheme.xaml"))),
            resources.Elements().ToArray());
        resources.ReplaceNodes(dictionary);
        return (UserControl)XamlReader.Parse(document.ToString());
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(800, 600));
        element.Arrange(new Rect(0, 0, 800, 600));
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "推荐状态控件测试未在时限内完成。");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void WithLanguage(string language, Action action)
    {
        var originalLanguage = L.CurrentLanguageName;
        try
        {
            L.SetLanguage(language);
            action();
        }
        finally
        {
            L.SetLanguage(originalLanguage);
        }
    }
}
