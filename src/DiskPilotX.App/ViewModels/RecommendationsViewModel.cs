using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Converters;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.ViewModels;

/// <summary>清理建议的展示与选择；分析请求统一走聊天对话，建议由 publish_recommendations 外部写入。</summary>
public partial class RecommendationsViewModel : ObservableObject
{
    private readonly ILogger<RecommendationsViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<CleanupRecommendation> _recommendations = new();

    [ObservableProperty]
    private ObservableCollection<CleanupRecommendation> _filteredRecommendations = new();

    [ObservableProperty]
    private object? _selectedCategoryFilter = string.Empty;

    [ObservableProperty]
    private object? _selectedSafetyFilter = string.Empty;

    [ObservableProperty]
    private string _totalRecoverableSpace = FileSizeConverter.FormatSize(0);

    [ObservableProperty]
    private int _totalSelectedCount;

    [ObservableProperty]
    private int _totalItemCount;

    [ObservableProperty]
    private bool? _allRecommendationsSelectionState = false;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _isWaitingForExternalRecommendations;

    [ObservableProperty]
    private string? _analysisError;

    [ObservableProperty]
    private bool _hasRecommendations;

    [ObservableProperty]
    private AnalysisDiagnostics? _lastDiagnostics;

    /// <summary>
    /// Returns true if any recommendation has been accepted by the user.
    /// Used to warn before re-running analysis that would replace accepted items.
    /// </summary>
    public bool HasAcceptedRecommendations =>
        Recommendations.Any(r => r.IsAccepted);

    public bool HasAnyRecommendations => Recommendations.Count > 0;

    public string EmptyStateTitleText => IsWaitingForExternalRecommendations
        ? L.Text("RecommendationsWaitingForAiTitle")
        : L.Text("RecommendationsEmptyTitle");

    public string EmptyStateDescriptionText => IsWaitingForExternalRecommendations
        ? string.Empty
        : L.Text("RecommendationsEmptyDescription");

    public RecommendationsViewModel(ILogger<RecommendationsViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<RecommendationsViewModel>.Instance;
        _logger.LogInformation("RecommendationsViewModel created");
        L.LanguageChanged += OnLanguageChanged;
    }

    partial void OnIsWaitingForExternalRecommendationsChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyStateTitleText));
        OnPropertyChanged(nameof(EmptyStateDescriptionText));
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(EmptyStateTitleText));
        OnPropertyChanged(nameof(EmptyStateDescriptionText));
    }

    public void SetExternalRecommendations(IEnumerable<CleanupRecommendation> recommendations, AnalysisDiagnostics? diagnostics = null)
    {
        var indexedRecommendations = recommendations.ToList();
        for (var index = 0; index < indexedRecommendations.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(indexedRecommendations[index].Id))
            {
                indexedRecommendations[index].Id = (index + 1).ToString();
            }
        }

        _logger.LogInformation("External recommendations applied: count={Count}", indexedRecommendations.Count);
        AnalysisError = null;
        IsWaitingForExternalRecommendations = false;
        LastDiagnostics = diagnostics;
        Recommendations = new ObservableCollection<CleanupRecommendation>(indexedRecommendations);
        ApplyFilters();
        UpdateTotals();
        HasRecommendations = Recommendations.Count > 0;
    }

    public void BeginExternalRecommendationLoad()
    {
        _logger.LogInformation("External recommendation load started");
        AnalysisError = null;
        IsWaitingForExternalRecommendations = true;
        HasRecommendations = false;
        Recommendations = new ObservableCollection<CleanupRecommendation>();
        ApplyFilters();
        UpdateTotals();
    }

    public void EndExternalRecommendationLoad()
    {
        _logger.LogInformation("External recommendation load ended");
        IsWaitingForExternalRecommendations = false;
    }

    [RelayCommand]
    private void Accept(CleanupRecommendation rec)
    {
        _logger.LogInformation("Recommendation accepted: id={Id}, path={TargetPath}", rec.Id, rec.TargetPath);
        rec.IsAccepted = true;
        UpdateTotals();
    }

    [RelayCommand]
    private void Dismiss(CleanupRecommendation rec)
    {
        _logger.LogInformation("Recommendation dismissed: id={Id}, path={TargetPath}", rec.Id, rec.TargetPath);
        rec.IsDismissed = true;
        UpdateTotals();
    }

    [RelayCommand]
    private void SelectAllSafe()
    {
        foreach (var rec in Recommendations.Where(r => r.SafetyRating == SafetyRating.Safe))
        {
            _logger.LogInformation("Recommendation accepted: id={Id}, path={TargetPath}", rec.Id, rec.TargetPath);
        rec.IsAccepted = true;
        }

        UpdateTotals();
    }

    [RelayCommand]
    private void DeselectAllCaution()
    {
        foreach (var rec in Recommendations.Where(r => r.SafetyRating == SafetyRating.Caution))
        {
            _logger.LogInformation("Recommendation dismissed: id={Id}, path={TargetPath}", rec.Id, rec.TargetPath);
        rec.IsDismissed = true;
        }

        UpdateTotals();
    }

    [RelayCommand]
    private void ToggleAllRecommendations()
    {
        var shouldSelectAll = TotalItemCount == 0 || TotalSelectedCount < TotalItemCount;

        foreach (var rec in Recommendations)
        {
            rec.IsAccepted = shouldSelectAll;
        }

        UpdateTotals();
    }

    [RelayCommand]
    private void SelectAllInCategory(RecommendationCategory category)
    {
        foreach (var rec in Recommendations.Where(r => r.Category == category))
        {
            _logger.LogInformation("Recommendation accepted: id={Id}, path={TargetPath}", rec.Id, rec.TargetPath);
        rec.IsAccepted = true;
        }

        UpdateTotals();
    }

    [RelayCommand]
    private void DeselectAllInCategory(RecommendationCategory category)
    {
        foreach (var rec in Recommendations.Where(r => r.Category == category))
        {
            _logger.LogInformation("Recommendation dismissed: id={Id}, path={TargetPath}", rec.Id, rec.TargetPath);
        rec.IsDismissed = true;
        }

        UpdateTotals();
    }

    partial void OnSelectedCategoryFilterChanged(object? value)
    {
        ApplyFilters();
    }

    partial void OnSelectedSafetyFilterChanged(object? value)
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = Recommendations.AsEnumerable();

        if (SelectedCategoryFilter is RecommendationCategory category)
        {
            filtered = filtered.Where(r => r.Category == category);
        }

        if (SelectedSafetyFilter is SafetyRating safetyRating)
        {
            filtered = filtered.Where(r => r.SafetyRating == safetyRating);
        }

        FilteredRecommendations = new ObservableCollection<CleanupRecommendation>(GroupByScope(filtered));
    }

    /// <summary>
    /// 多范围分析整合进同一份清单后，按来源范围归拢（组内保持原顺序），
    /// 并给每组第一条挂上分组标题，让「这些候选来自哪个游戏/目录」一眼可见。
    /// 单一来源或没有来源标记时，原样返回，不显示表头。
    /// </summary>
    private static IEnumerable<CleanupRecommendation> GroupByScope(IEnumerable<CleanupRecommendation> items)
    {
        var list = items.ToList();
        foreach (var item in list) item.ScopeHeader = string.Empty;
        var groups = list
            .Select((item, index) => (Item: item, Index: index))
            .GroupBy(entry => entry.Item.SourceScope, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Min(entry => entry.Index))
            .ToList();
        if (groups.Count < 2) return list;
        var result = new List<CleanupRecommendation>(list.Count);
        foreach (var group in groups)
        {
            var members = group.Select(entry => entry.Item).ToList();
            members[0].ScopeHeader = group.Key;
            result.AddRange(members);
        }
        return result;
    }

    public void RefreshAfterCleanup()
    {
        ApplyFilters();
        UpdateTotals();
    }

    public void RefreshFilteredRecommendations()
    {
        ApplyFilters();
        UpdateTotals();
    }

    public void UpdateTotals()
    {
        TotalSelectedCount = Recommendations.Count(r => r.IsAccepted);
        TotalItemCount = Recommendations.Count;
        TotalRecoverableSpace = FileSizeConverter.FormatSize(
            Recommendations.Where(r => r.IsAccepted).Sum(r => r.Size));
        AllRecommendationsSelectionState = TotalSelectedCount == 0
            ? false
            : TotalSelectedCount == TotalItemCount
                ? true
                : null;
    }
}
