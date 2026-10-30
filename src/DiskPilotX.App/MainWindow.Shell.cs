using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DiskPilotX.App.Helpers;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Views;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private const double DefaultChatPanelWidth = 360;
    private const double MinChatPanelWidth = 260;

    private bool _isChatCollapsed;
    private bool _isChatAnimating;
    private double _expandedChatPanelWidth = DefaultChatPanelWidth;

    // 设置改为独立弹窗（非模态）；单实例随主窗口存续，隐藏而非销毁，验收入口随时可读取其内容。
    private SettingsWindow? _settingsWindow;

    private SettingsWindow EnsureSettingsWindow()
    {
        if (_settingsWindow is not null) return _settingsWindow;
        var window = new SettingsWindow();
        // Owner 只能指向已显示的窗口；主窗口尚未显示时留空，ShowSettingsPage 再补挂。
        if (IsLoaded) window.Owner = this;
        window.SetViewModels(_settingsViewModel, _updateViewModel);
        window.Closed += (_, _) => { if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null; };
        _settingsWindow = window;
        return window;
    }

    /// <summary>设置内容控件；供版本入口定位更新分区及更新验收入口读取滚动状态。</summary>
    private SettingsPage SettingsPage => EnsureSettingsWindow().Settings;

    private void OpenSettingsDialog() => ShowSettingsPage();

    private void ToggleChatPanel()
    {
        if (_isChatAnimating)
            return;

        _isChatCollapsed = !_isChatCollapsed;
        AnimateChatPanel(_isChatCollapsed);
        TitleBar.UpdateCollapseIcon(!_isChatCollapsed);
    }

    private void ChatPanelViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isChatCollapsed || _isChatAnimating || e.NewSize.Width < MinChatPanelWidth)
            return;

        _expandedChatPanelWidth = e.NewSize.Width;
    }

    private void ClearChatPanelSlideTransform()
    {
        if (ChatPanelContainer.RenderTransform is TranslateTransform translateTransform)
            translateTransform.BeginAnimation(TranslateTransform.XProperty, null);

        ChatPanelContainer.RenderTransform = null;
    }

    private void AnimateChatPanel(bool collapse)
    {
        _isChatAnimating = true;
        var duration = TimeSpan.FromMilliseconds(250);
        var easing = new CubicEase { EasingMode = collapse ? EasingMode.EaseIn : EasingMode.EaseOut };

        ChatPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
        ClearChatPanelSlideTransform();

        if (collapse)
        {
            _expandedChatPanelWidth = Math.Max(ChatPanelColumn.ActualWidth, MinChatPanelWidth);
            ChatPanelColumn.MinWidth = 0;
            ChatPanelContainer.Width = _expandedChatPanelWidth;
            ChatPanelContainer.HorizontalAlignment = HorizontalAlignment.Left;

            var widthAnim = new GridLengthAnimation
            {
                From = new GridLength(_expandedChatPanelWidth),
                To = new GridLength(0),
                Duration = duration,
                EasingFunction = easing
            };

            widthAnim.Completed += (_, _) =>
            {
                ChatPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                ChatPanelColumn.Width = new GridLength(0);
                ClearChatPanelSlideTransform();
                ChatPanelContainer.Width = double.NaN;
                ChatPanelViewport.Visibility = Visibility.Collapsed;
                ChatSplitter.Visibility = Visibility.Collapsed;
                _isChatAnimating = false;
            };
            ChatPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, widthAnim);
        }
        else
        {
            var targetWidth = Math.Max(_expandedChatPanelWidth, MinChatPanelWidth);
            ChatPanelColumn.MinWidth = 0;
            ChatPanelColumn.Width = new GridLength(0);
            ChatPanelViewport.Visibility = Visibility.Visible;
            ChatSplitter.Visibility = Visibility.Visible;
            ChatPanelContainer.Width = targetWidth;
            ChatPanelContainer.HorizontalAlignment = HorizontalAlignment.Left;

            var widthAnim = new GridLengthAnimation
            {
                From = new GridLength(0),
                To = new GridLength(targetWidth),
                Duration = duration,
                EasingFunction = easing
            };

            widthAnim.Completed += (_, _) =>
            {
                ChatPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                ChatPanelColumn.Width = new GridLength(targetWidth);
                ChatPanelColumn.MinWidth = MinChatPanelWidth;
                ClearChatPanelSlideTransform();
                ChatPanelContainer.Width = double.NaN;
                ChatPanelContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                _isChatAnimating = false;
            };
            ChatPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, widthAnim);
        }
    }

    private void ShowSettingsPage()
    {
        // 删除执行会再次遍历磁盘；期间不允许打开设置改变方式或保护规则。
        if (_cleanupRunning) return;
        if (_settingsViewModel is null)
            return;

        var window = EnsureSettingsWindow();
        if (window.Owner is null && IsLoaded) window.Owner = this;
        window.ReloadSettingsForOpen();
        window.Show();
        window.Activate();
    }

    private void HideSettingsPage()
    {
        _settingsWindow?.Hide();
    }

    private void VersionDisplay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ShowSettingsPage();
        SettingsPage.NavigateToUpdates();
    }

    private void OnSettingsChanged()
    {
        Interlocked.Increment(ref _cleanupSettingsRevision);
        L.SetLanguage(_settingsViewModel?.Language);
        _chatViewModel?.RefreshApiKeyStatus();
    }
}


