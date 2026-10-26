using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Views;

namespace DiskPilotX.App.Tests;

public sealed class SkillTaskThemeTests
{
    [Fact]
    public void 任务窗复用标题栏和主题控件并响应资源更新() => RunSta(() =>
    {
        var window = new SkillTaskWindow("缓存检查", """
            <StackPanel><TextBox Name="scope_path" Text="测试目录" />
            <CheckBox Name="cache" Content="缓存" /><ComboBox Name="mode"><ComboBoxItem Content="预览" /></ComboBox>
            <Border><TextBlock Text="提示" /></Border><DataGrid ItemsSource="{Binding candidates}" /></StackPanel>
            """, new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["path"] = "测试目录/cache", ["size_bytes"] = 128, ["explanation"] = "缓存" }) }, (_, _, _) => Task.FromResult<JsonNode>(new JsonObject()));
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative)
        });
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(780, 700));
        content.Arrange(new Rect(0, 0, 780, 700));
        content.UpdateLayout();
        Assert.Equal(WindowStyle.None, window.WindowStyle);
        var titleBar = Assert.Single(Visuals<WindowTitleBar>(content));
        Assert.Contains(Visuals<TextBlock>(titleBar), t => t.Text == "缓存检查");
        Assert.Equal(Visibility.Collapsed, ((Button)titleBar.FindName("SettingsButton")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((Button)titleBar.FindName("CollapseChatButton")).Visibility);
        var scope = Visuals<TextBox>(content).Single(t => t.Text == "测试目录");
        Assert.Same(window.FindResource("VP.TextBox"), scope.Style);
        Assert.Same(window.FindResource("VP.CheckBox"), Assert.Single(Visuals<CheckBox>(content)).Style);
        Assert.Same(window.FindResource("VP.ComboBox"), Assert.Single(Visuals<ComboBox>(content)).Style);
        var table = Assert.Single(Visuals<DataGrid>(content));
        window.Resources["VP.TextPrimaryBrush"] = Brushes.DarkBlue;
        window.Resources["VP.ControlCornerRadius"] = new CornerRadius(12);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.Same(Brushes.DarkBlue, scope.Foreground);
        Assert.Same(Brushes.DarkBlue, table.Foreground);
        var headers = Visuals<DataGridColumnHeader>(table).Where(h => h.Column is not null).ToArray();
        Assert.NotEmpty(headers);
        Assert.All(headers, h => Assert.Same(Brushes.DarkBlue, h.Foreground));
        var inputBorder = (Border)scope.Template.FindName("Bd", scope);
        Assert.Equal(new CornerRadius(12), inputBorder.CornerRadius);
        Assert.Equal(new CornerRadius(10), ((Border)window.Content).CornerRadius);
        window.Resources["VP.WindowCornerRadius"] = new CornerRadius(16);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.Equal(16, Assert.IsType<RectangleGeometry>(content.Clip).RadiusX);
        window.Close();
    });

    [Fact]
    public void 原生缩放边界保留任务窗最小尺寸() => RunSta(() =>
    {
        var window = new SkillTaskWindow("尺寸验收", null, null, (_, _, _) => Task.FromResult<JsonNode>(new JsonObject()));
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var info = Marshal.AllocHGlobal(40);
        try
        {
            for (var offset = 0; offset < 40; offset += 4) Marshal.WriteInt32(info, offset, 0);
            SendMessage(handle, 0x0024, IntPtr.Zero, info);
            var dpi = VisualTreeHelper.GetDpi(window);
            Assert.True(Marshal.ReadInt32(info, 24) >= (int)(560 * dpi.DpiScaleX), "原生最小宽度没有保留 WPF 限制。");
            Assert.True(Marshal.ReadInt32(info, 28) >= (int)(430 * dpi.DpiScaleY), "原生最小高度没有保留 WPF 限制。");
        }
        finally { Marshal.FreeHGlobal(info); window.Close(); }
    });

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "主题控件测试超时。");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
