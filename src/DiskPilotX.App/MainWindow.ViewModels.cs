using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Helpers;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Converters;
using DiskPilotX.App.Logging;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Cleanup;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => MainViewModel_PropertyChanged(sender, e));
            return;
        }

        if (sender is not MainViewModel mainVm)
            return;

        if(e.PropertyName==nameof(MainViewModel.CurrentSession))
        {
            _scanSnapshots.ReleaseOwner("view");
            if(mainVm.CurrentSession is {} current) _scanSnapshots.Protect(current.Id,"view");
        }
        if(e.PropertyName==nameof(MainViewModel.IsScanning)) RefreshScanSnapshotEntries();

        if (e.PropertyName is nameof(MainViewModel.IsScanning) or nameof(MainViewModel.ScanTitleText) or nameof(MainViewModel.ScanProgressText))
        {
            if (e.PropertyName == nameof(MainViewModel.IsScanning) && mainVm.IsScanning)
            {
                foreach (var cached in _scanSnapshots.Sessions)
                    Services.DeletedEntryState.Clear(cached.RootEntry);
                Services.DeletedEntryState.Clear(mainVm.CurrentSession?.RootEntry);
                _treemapViewModel?.ClearDeletionMarks();
            }
            TreemapView.SetScanningState(mainVm.IsScanning, mainVm.ScanTitleText, mainVm.ScanProgressText);
        }
        else if (e.PropertyName is nameof(MainViewModel.SelectedPath))
        {
            if (string.IsNullOrWhiteSpace(mainVm.SelectedPath))
                return;

            if (_suppressSelectedPathNavigation)
            {
                RebuildBreadcrumbBar();
                return;
            }

            NavigateToPathOrSelect(mainVm.SelectedPath, updateSelectedPath: false);
        }
    }

    public void SetViewModels(RecommendationsViewModel recsVm, SettingsViewModel settingsVm)
    {
        _recommendationsViewModel = recsVm;
        recsVm.PropertyChanged += (_, e) =>
        {
            if(e.PropertyName is nameof(RecommendationsViewModel.Recommendations) or nameof(RecommendationsViewModel.HasAcceptedRecommendations)) _recommendationRevision++;
            if(e.PropertyName == nameof(RecommendationsViewModel.Recommendations) && recsVm.Recommendations.Count==0)
            {
                ResetAssistantBatch(); _assistantAnalysisSession=null; _publishedScopes.Clear(); _scanSnapshots.ReleaseOwner("recommendations");
            }
        };
        _settingsViewModel = settingsVm;
        if (DataContext is MainViewModel mainVm)
            mainVm.RecommendationsVM = recsVm;
        _settingsWindow?.SetViewModels(settingsVm, _updateViewModel);
        settingsVm.ShowModelDiscoveryFailureAsync = message => ShowAppMessageAsync(
            message, L.Text("SettingsModelDiscoveryFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        settingsVm.SettingsChanged += OnSettingsChanged;
        TitleBar.SettingsRequested += (_, _) => ShowSettingsPage();
        TitleBar.CollapseChatRequested += (_, _) => ToggleChatPanel();
        RecommendationsPanel.SetViewModel(recsVm);
        RecommendationsPanel.ShowWaitingForAiMessageAsync = () => ShowAppModalAsync(
            L.Text("AnalyzeButton"),
            L.Text("AiExternalAnalysisWaitMessage"),
            ModalMessageType.Info,
            ModalButtonFlags.Positive);
        RecommendationsPanel.AnalyzeRequested += OnAnalyzeRequested;
        RecommendationsPanel.CleanupRequested += OnCleanupRequested;
        RecommendationsPanel.RecommendationActivated += OnRecommendationActivated;
        TreemapView.DeleteEntryAsync = DeleteEntryAsync;
        TreeViewControl.DeleteEntryAsync = DeleteEntryAsync;
        TreemapView.ShowMessageAsync = ShowAppMessageAsync;
        TreemapView.ShowContentAsync = ShowAppContentAsync;
        TreemapView.CloseContentModal = ModalHost.CloseCurrent;
    }

    public void SetUpdateViewModel(UpdateViewModel updateVm)
    {
        _updateViewModel = updateVm;
        AboutPage.DataContext = updateVm;
        TitleBar.UpdateVM = updateVm;
        _settingsWindow?.SetViewModels(_settingsViewModel, updateVm);

        if (DataContext is MainViewModel mainVm)
            mainVm.UpdateVM = updateVm;
    }

    public void SetTreemapViewModel(TreemapViewModel treemapVm)
    {
        _treemapViewModel = treemapVm;
        _treemapViewModel.PropertyChanged += TreemapViewModel_NavigationChanged;
    }

    private void TreemapViewModel_NavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => TreemapViewModel_NavigationChanged(sender, e));
            return;
        }

        if (_treemapViewModel is null)
            return;

        switch (e.PropertyName)
        {
            case nameof(TreemapViewModel.CanGoBack):
                BackButton.IsEnabled = _treemapViewModel.CanGoBack;
                break;
            case nameof(TreemapViewModel.CanGoForward):
                ForwardButton.IsEnabled = _treemapViewModel.CanGoForward;
                break;
            case nameof(TreemapViewModel.CanGoUp):
                UpButton.IsEnabled = _treemapViewModel.CanGoUp;
                break;
            case nameof(TreemapViewModel.CurrentRoot):
                if (_treemapViewModel.CurrentRoot is not null)
                {
                    _displayPathOverride = null;
                    UpdateSelectedPathFromNavigation(_treemapViewModel.CurrentRoot.Path, updateSelectedPath: true);
                    RebuildBreadcrumbBar();
                    if (TreeViewControl.Visibility == Visibility.Visible)
                    {
                        SyncTreeViewWithTreemap();
                    }
                }
                break;
        }
    }

    public void SetChatViewModel(ChatViewModel chatVm)
    {
        if (_chatViewModel is not null) _chatViewModel.ConfirmConversationDeletion = null;
        _chatViewModel = chatVm;
        chatVm.ConfirmConversationDeletion = ConfirmConversationDeletionAsync;
        ChatPanel.SetViewModel(chatVm);
        ConnectAssistantWorkspace();
        chatVm.ClearConsoleRequested += AppLog.UiSink.Clear;

        ChatPanel.OpenSettingsRequested += () => OpenSettingsDialog();
        TreemapView.AskAiRequested += AskAiAboutEntry;
        TreemapView.MultiSelectionReferenced += ReferenceMultiSelectionToChat;
        TreeViewControl.AskAiRequested += AskAiAboutEntry;
        TreeViewControl.EntrySelected += OnTreeViewEntrySelected;

        // Track treemap navigation changes to update chat context
        if (_treemapViewModel is not null)
        {
            _treemapViewModel.PropertyChanged += TreemapViewModel_PropertyChanged;
        }
    }

    public void SetTreeViewModel(TreeViewModel treeVm)
    {
        TreeViewControl.DataContext = treeVm;
    }


    private void AskAiAboutEntry(FileEntry entry)
    {
        if (_chatViewModel is null)
            return;

        _chatViewModel.LinkedEntry = entry;
        _chatViewModel.InputText = entry.IsDirectory
            ? $"深入分析这个目录的空间占用：{entry.Path}"
            : $"分析这个文件的空间占用和清理风险：{entry.Path}";
        ChatPanel.FocusInput();
    }

    private void ReferenceMultiSelectionToChat(IReadOnlyList<FileEntry> entries)
    {
        if (_chatViewModel is null || entries.Count == 0)
            return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("以下是我在磁盘地图上多选的文件/文件夹，请帮我分析：");
        foreach (var e in entries.OrderByDescending(e => e.Size))
        {
            string type = e.IsDirectory ? "文件夹" : "文件";
            sb.AppendLine($"- [{type}] {e.Path}（{FileSizeConverter.FormatSize(e.Size)}）");
        }
        _chatViewModel.InputText = sb.ToString();
        ChatPanel.FocusInput();
    }
    private void TreemapViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => TreemapViewModel_PropertyChanged(sender, e));
            return;
        }

        if (_chatViewModel is null || _treemapViewModel is null)
            return;

        switch (e.PropertyName)
        {
            case nameof(TreemapViewModel.CurrentRoot):
                if (_treemapViewModel.CurrentRoot is not null)
                {
                    _chatViewModel.UpdateViewRoot(_treemapViewModel.CurrentRoot);
                }
                break;

            case nameof(TreemapViewModel.SelectedNode):
                _chatViewModel.LinkedEntry = _treemapViewModel.SelectedNode?.Entry;
                break;
        }
    }

    private void OnTreeViewEntrySelected(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            _treemapViewModel?.NavigateToEntry(entry);
        }
    }

    private void OnRecommendationActivated(CleanupRecommendation recommendation)
    {
        if(_cleanupRunning || _chatViewModel?.IsOperationRunning==true || (DataContext as MainViewModel)?.IsScanning==true)
        {if(_chatViewModel is not null)_chatViewModel.ErrorMessage="任务正在进行，请结束后再查看推荐的扫描版本。";return;}
        if (_treemapViewModel is null)
            return;

        if(!IsSkillScopeAllowed(recommendation.TargetPath)) {if(_chatViewModel is not null)_chatViewModel.ErrorMessage="当前设置不允许查看此推荐范围。";return;}
        var shown=TryShowCachedAiScanForPath(recommendation.TargetPath,snapshotId:recommendation.SourceScanId);
        if(_chatViewModel is not null)_chatViewModel.ErrorMessage=shown?null:"该推荐的扫描版本已不可用，请重新分析，不会改用另一个同路径快照。";
    }

    private void CacheAiScanSession(ScanSession session)
    {
        if (session.RootEntry is null || string.IsNullOrWhiteSpace(session.TargetPath))
            return;

        RegisterScanSnapshot(session);
    }

    private bool TryShowCachedAiScanForPath(string? path, bool navigateToPath = true, Guid? snapshotId = null)
    {
        if (string.IsNullOrWhiteSpace(path) || _treemapViewModel is null)
            return false;

        var session = snapshotId is {} id?_scanSnapshots.FindById(id):_scanSnapshots.FindByPath(path);
        if (session?.RootEntry is null || !IsPathUnder(path,session.RootEntry.Path))
            return false;

        if (DataContext is MainViewModel mainVm)
        {
            mainVm.CurrentSession = session;
            mainVm.UpdateStatusBar(session);
        }

        _scanSnapshots.ReleaseOwner("view"); _scanSnapshots.Protect(session.Id,"view");
        _treemapViewModel.SetRoot(session.RootEntry, session);
        _chatViewModel?.SetContext(session, session.RootEntry);
        if (TreeViewControl.DataContext is TreeViewModel treeViewModel)
        {
            treeViewModel.SetRoot(session.RootEntry, session);
        }

        if(!navigateToPath)return true;
        var entry=new DiskPilotX.Core.Services.FileTree.FileTreeQueryService().FindByPath(session,path);
        return entry is not null && _treemapViewModel.NavigateToEntry(entry);
    }

    private static bool IsPathUnder(string path, string rootPath)
    {
        var normalizedPath = NormalizePathKey(path);
        var normalizedRoot = NormalizePathKey(rootPath);
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathKey(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    // 鈹€鈹€鈹€ Tab switching (replaces TabControl) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    private void ShowRecommendationsPanel()
    {
        RecommendationsTabBtn.IsChecked = true;
        RecommendationsPanel.Visibility = Visibility.Visible;
        ConsoleFrame.Visibility = Visibility.Collapsed;
        ConsoleFilterButton.Visibility = Visibility.Collapsed;
    }

    private void ShowConsolePanel()
    {
        ConsoleTabBtn.IsChecked = true;
        RecommendationsPanel.Visibility = Visibility.Collapsed;
        ConsoleFrame.Visibility = Visibility.Visible;
        ConsoleFilterButton.Visibility = Visibility.Visible;
    }

    private void RecommendationsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (RecommendationsPanel == null || ConsoleItemsControl == null || ConsoleFilterButton == null)
            return;
        RecommendationsPanel.Visibility = Visibility.Visible;
        ConsoleFrame.Visibility = Visibility.Collapsed;
        ConsoleFilterButton.Visibility = Visibility.Collapsed;
    }

    private void ConsoleTab_Checked(object sender, RoutedEventArgs e)
    {
        if (RecommendationsPanel == null || ConsoleItemsControl == null || ConsoleFilterButton == null)
            return;
        RecommendationsPanel.Visibility = Visibility.Collapsed;
        ConsoleFrame.Visibility = Visibility.Visible;
        ConsoleFilterButton.Visibility = Visibility.Visible;
    }

    private void EnsureBottomPanelVisible()
    {
        if (RecommendationsPanelRow.ActualHeight <= 0)
        {
            RecommendationsPanelRow.Height = new GridLength(DefaultRecommendationsHeight);
        }
        RecommendationsSplitter.Visibility = Visibility.Visible;
    }

    // 鈹€鈹€鈹€ View Mode Tabs (Treemap / TreeView) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    private void TreemapTab_Checked(object sender, RoutedEventArgs e)
    {
        if (TreemapView == null || TreeViewControl == null || AboutPage == null)
            return;
        TreemapView.Visibility = Visibility.Visible;
        TreeViewControl.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Collapsed;
    }

    private void TreeViewTab_Checked(object sender, RoutedEventArgs e)
    {
        if (TreemapView == null || TreeViewControl == null || AboutPage == null)
            return;
        TreemapView.Visibility = Visibility.Collapsed;
        TreeViewControl.Visibility = Visibility.Visible;
        AboutPage.Visibility = Visibility.Collapsed;

        // Sync TreeView with current Treemap data
        SyncTreeViewWithTreemap();
    }

    private void AboutTab_Checked(object sender, RoutedEventArgs e)
    {
        if (TreemapView == null || TreeViewControl == null || AboutPage == null)
            return;
        TreemapView.Visibility = Visibility.Collapsed;
        TreeViewControl.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Visible;
    }

    private void SyncTreeViewWithTreemap()
    {
        if (_treemapViewModel?.CurrentRoot is null)
            return;

        var root = _treemapViewModel.CurrentRoot;
        var treeViewModel = (TreeViewModel)TreeViewControl.DataContext;
        treeViewModel.SetRoot(root, GetScanSession());
        TreeViewControl.SelectEntry(root);
    }

    private ScanSession? GetScanSession()
    {
        // Try to get the session from MainViewModel
        if (DataContext is MainViewModel mainVm)
        {
            return mainVm.CurrentSession;
        }
        return null;
    }

    // 鈹€鈹€鈹€ Console 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

}
