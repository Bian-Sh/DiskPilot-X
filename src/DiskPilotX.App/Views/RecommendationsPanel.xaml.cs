using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Views;

public partial class RecommendationsPanel : UserControl
{
    private readonly DispatcherTimer pathPingTimer = new();
    private readonly RecommendationPathGesture pathGesture;
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    public event Action<CleanupRecommendation>? RecommendationPingRequested;
    public event Action? AnalyzeRequested;
    public event Action? CleanupRequested;
    public event Action<CleanupRecommendation>? RecommendationActivated;
    public Func<Task>? ShowWaitingForAiMessageAsync { get; set; }

    public RecommendationsPanel()
    {
        InitializeComponent();
        pathGesture = new(item => RecommendationPingRequested?.Invoke(item), item => RecommendationActivated?.Invoke(item));
        pathPingTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, GetDoubleClickTime()));
        pathPingTimer.Tick += (_, _) => { pathPingTimer.Stop(); pathGesture.Complete(); };
        Unloaded += (_, _) => { pathPingTimer.Stop(); pathGesture.Cancel(); };
    }

    public void SetViewModel(RecommendationsViewModel vm)
    {
        DataContext = vm;
    }

    // 两个入口只改变建议集合；磁盘任务运行期间不允许撤销其批准清单。
    private bool CanRemoveRecommendations => DataContext is RecommendationsViewModel { IsAnalyzing: false, IsWaitingForExternalRecommendations: false }
        && Window.GetWindow(this)?.DataContext is not MainViewModel { IsCopilotBusy: true, };

    private static readonly DependencyProperty MenuLocalizationReadyProperty = DependencyProperty.RegisterAttached(
        "MenuLocalizationReady", typeof(bool), typeof(RecommendationsPanel), new PropertyMetadata(false));

    private static void EnsureMenuLocalization(ContextMenu menu)
    {
        if ((bool)menu.GetValue(MenuLocalizationReadyProperty)) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            var keys = item.Name switch
            {
                "RemoveRecommendationMenu" => ("RemoveRecommendation", "RemoveRecommendationToolTip"),
                "ClearRecommendationsMenu" => ("ClearRecommendations", "ClearRecommendationsToolTip"),
                "CleanupRecommendationsMenu" => ("CleanUpButton", "CleanUpToolTip"),
                _ => default
            };
            if (keys.Item1 is null) continue;
            // 条目模板里的MarkupExtension可能只取初值；复用现有LocBinding保持菜单实时跟随语言。
            item.Header = new LocBinding(keys.Item1, item, HeaderedItemsControl.HeaderProperty).Value;
            item.ToolTip = new LocBinding(keys.Item2, item, ToolTipService.ToolTipProperty).Value;
            System.Windows.Automation.AutomationProperties.SetName(item,
                new LocBinding(keys.Item1, item, System.Windows.Automation.AutomationProperties.NameProperty).Value);
        }
        menu.SetValue(MenuLocalizationReadyProperty, true);
    }

    private void RecommendationContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || DataContext is not RecommendationsViewModel vm) return;
        EnsureMenuLocalization(menu);
        // Popup不在Window视觉树中，直接从所属面板核对忙碌状态，不能依赖失效的祖先绑定。
        foreach (var item in menu.Items.OfType<MenuItem>())
            item.IsEnabled = item.Name switch
            {
                "RemoveRecommendationMenu" => CanRemoveRecommendations,
                "ClearRecommendationsMenu" => CanRemoveRecommendations && vm.HasAnyRecommendations,
                "CleanupRecommendationsMenu" => CanRemoveRecommendations && vm.HasAcceptedRecommendations,
                _ => item.IsEnabled
            };
    }

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

        // 仍只请求原清理批准入口；菜单不直接删除磁盘，也不改变已选项目。
        if (CanRemoveRecommendations && DataContext is RecommendationsViewModel { HasAcceptedRecommendations: true })
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

    private void RecommendationPath_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || sender is not FrameworkElement { DataContext: CleanupRecommendation item }) return;
        e.Handled = true;
        pathPingTimer.Stop();
        pathGesture.Activate(item);
    }

    private void RecommendationPath_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: CleanupRecommendation item }) return;
        pathPingTimer.Stop();
        pathGesture.Queue(item);
        // 后台鼠标与辅助技术都可能不改变 IsMouseOver，统一等待原生双击间隔。
        pathPingTimer.Start();
    }
}
