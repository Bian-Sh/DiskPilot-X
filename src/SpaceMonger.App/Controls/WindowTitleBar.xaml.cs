using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpaceMonger.App.Localization;

namespace SpaceMonger.App.Controls;

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

    public event RoutedEventHandler? SettingsRequested;
    public event RoutedEventHandler? CollapseChatRequested;
    public event RoutedEventHandler? CloseRequested;

    public WindowTitleBar()
    {
        InitializeComponent();
    }

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


