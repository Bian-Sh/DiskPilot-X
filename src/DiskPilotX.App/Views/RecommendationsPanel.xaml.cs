using System.Windows;
using System.Windows.Controls;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Views;

public partial class RecommendationsPanel : UserControl
{
    public event Action? AnalyzeRequested;
    public event Action? CleanupRequested;
    public event Action<CleanupRecommendation>? RecommendationActivated;
    public Func<Task>? ShowWaitingForAiMessageAsync { get; set; }

    public RecommendationsPanel()
    {
        InitializeComponent();
    }

    public void SetViewModel(RecommendationsViewModel vm)
    {
        DataContext = vm;
    }

    // 两个入口只改变建议集合；磁盘任务运行期间不允许撤销其批准清单。
    private bool CanRemoveRecommendations => DataContext is RecommendationsViewModel { IsAnalyzing: false, IsWaitingForExternalRecommendations: false }
        && Window.GetWindow(this)?.DataContext is not MainViewModel { IsCopilotBusy: true, };

    private void ClearRecommendations_Click(object sender, RoutedEventArgs e)
    {
        if (CanRemoveRecommendations && DataContext is RecommendationsViewModel vm) vm.ClearRecommendationsCommand.Execute(null);
    }

    private void RemoveRecommendation_Click(object sender, RoutedEventArgs e)
    {
        if (CanRemoveRecommendations && DataContext is RecommendationsViewModel vm
            && sender is MenuItem { DataContext: CleanupRecommendation item }) vm.DismissCommand.Execute(item);
    }
    private async void CleanUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ShowWaitingForAiMessageIfNeededAsync())
            return;

        CleanupRequested?.Invoke();
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ShowWaitingForAiMessageIfNeededAsync())
            return;

        if (DataContext is RecommendationsViewModel { IsAnalyzing: true })
        {
            return;
        }

        AnalyzeRequested?.Invoke();
    }

    private async Task<bool> ShowWaitingForAiMessageIfNeededAsync()
    {
        if (DataContext is not RecommendationsViewModel { IsWaitingForExternalRecommendations: true })
            return false;

        if (ShowWaitingForAiMessageAsync is not null)
            await ShowWaitingForAiMessageAsync();

        return true;
    }

    private void RecommendationItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    private void RecommendationSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecommendationsViewModel vm)
        {
            vm.UpdateTotals();
        }
    }

    private void RecommendationsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (((FrameworkElement)e.OriginalSource).DataContext is CleanupRecommendation recommendation)
        {
            RecommendationActivated?.Invoke(recommendation);
        }
    }

    private void RecommendationPath_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CleanupRecommendation recommendation)
        {
            RecommendationActivated?.Invoke(recommendation);
        }
    }
}
