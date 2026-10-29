using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Controls;

public partial class WindowTitleBar : UserControl
{
    /// <summary>任务窗隐藏应用级入口，窗口按钮和拖动行为继续共用。</summary>
    public static readonly DependencyProperty ApplicationToolsVisibilityProperty = DependencyProperty.Register(
        nameof(ApplicationToolsVisibility), typeof(Visibility), typeof(WindowTitleBar), new PropertyMetadata(Visibility.Visible));

    public Visibility ApplicationToolsVisibility
    {
        get => (Visibility)GetValue(ApplicationToolsVisibilityProperty);
        set => SetValue(ApplicationToolsVisibilityProperty, value);
    }

    /// <summary>动态面板只保留一个正文主标题，主窗口仍显示品牌区。</summary>
    public static readonly DependencyProperty BrandingVisibilityProperty = DependencyProperty.Register(
        nameof(BrandingVisibility), typeof(Visibility), typeof(WindowTitleBar), new PropertyMetadata(Visibility.Visible));
    public Visibility BrandingVisibility
    {
        get => (Visibility)GetValue(BrandingVisibilityProperty);
        set => SetValue(BrandingVisibilityProperty,value);
    }

    public static readonly DependencyProperty UpdateVMProperty = DependencyProperty.Register(
        nameof(UpdateVM), typeof(UpdateViewModel), typeof(WindowTitleBar), new PropertyMetadata(null));
    public UpdateViewModel? UpdateVM
    {
        get => (UpdateViewModel?)GetValue(UpdateVMProperty);
        set => SetValue(UpdateVMProperty, value);
    }
    private Window? _observedWindow;

    public event RoutedEventHandler? SettingsRequested;
    public event RoutedEventHandler? CollapseChatRequested;
    public event RoutedEventHandler? CloseRequested;

    public WindowTitleBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _observedWindow = Window.GetWindow(this);
            if (_observedWindow is null) return;
            _observedWindow.PreviewMouseMove += ObserveMouseMove;
            _observedWindow.PreviewMouseDown += ObserveMouseDown;
            _observedWindow.Deactivated += CloseUpdatePopup;
            _observedWindow.Closed += CloseUpdatePopup;
        };
        Unloaded += (_, _) =>
        {
            UpdateReleasePopup.IsOpen = false;
            if (_observedWindow is null) return;
            _observedWindow.PreviewMouseMove -= ObserveMouseMove;
            _observedWindow.PreviewMouseDown -= ObserveMouseDown;
            _observedWindow.Deactivated -= CloseUpdatePopup;
            _observedWindow.Closed -= CloseUpdatePopup;
            _observedWindow = null;
        };
    }

    private void UpdateReleaseScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => ReleaseNotesScroll.ForwardMouseWheel((ScrollViewer)sender, e);

    private void UpdateButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (UpdateVM?.IsUpdateEntryVisible == true) UpdateReleasePopup.IsOpen = true;
    }
    private void ObserveMouseMove(object sender, MouseEventArgs e)
    {
        if (UpdateReleasePopup.IsOpen && UpdatePopupPolicy.ShouldClose(e.OriginalSource as DependencyObject, UpdatePopupContent, UpdateButton))
            UpdateReleasePopup.IsOpen = false;
    }
    private void ObserveMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (UpdateReleasePopup.IsOpen && UpdatePopupPolicy.ShouldCloseOnPointerDown(e.OriginalSource as DependencyObject, UpdatePopupContent, UpdateButton))
            UpdateReleasePopup.IsOpen = false;
    }
    private void CloseUpdatePopup(object? sender, EventArgs e) => UpdateReleasePopup.IsOpen = false;

    private Window ParentWindow => Window.GetWindow(this);

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, e);
    }

    private void CollapseChatButton_Click(object sender, RoutedEventArgs e)
    {
        CollapseChatRequested?.Invoke(this, e);
    }

    /// <summary>
    /// Updates the collapse button icon based on chat panel visibility.
    /// PanelOpenIcon: sidebar with divider on right (chat is open).
    /// PanelClosedIcon: sidebar with divider on left (chat is collapsed).
    /// </summary>
    public void UpdateCollapseIcon(bool isChatVisible)
    {
        PanelOpenIcon.Visibility = isChatVisible ? Visibility.Visible : Visibility.Collapsed;
        PanelClosedIcon.Visibility = isChatVisible ? Visibility.Collapsed : Visibility.Visible;
        CollapseChatButton.ToolTip = isChatVisible ? L.Text("CollapseChatPanelToolTip") : L.Text("ExpandChatPanelToolTip");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var window = ParentWindow;
        if (window is null) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize(window);
        }
        else
        {
            // If window is maximized, restore first so drag works naturally
            if (window.WindowState == WindowState.Maximized)
            {
                var point = PointToScreen(e.GetPosition(this));
                window.WindowState = WindowState.Normal;
                window.Left = point.X - window.Width / 2;
                window.Top = point.Y - 18;
            }
            window.DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ParentWindow is { } w)
            w.WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ParentWindow is { } w)
            ToggleMaximize(w);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, e);
    }

    private static void ToggleMaximize(Window window)
    {
        if (window.WindowState == WindowState.Maximized)
        {
            window.WindowState = WindowState.Normal;
        }
        else
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Updates the maximize/restore icon based on current window state.
    /// Call this when the parent window's state changes.
    /// </summary>
    public void UpdateMaximizeIcon(bool isMaximized)
    {
        // E922 = Maximize, E923 = Restore
        MaximizeButton.Content = isMaximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = isMaximized ? L.Text("RestoreToolTip") : L.Text("MaximizeToolTip");
    }
}


