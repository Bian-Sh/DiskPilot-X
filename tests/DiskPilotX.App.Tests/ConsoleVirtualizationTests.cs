using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskPilotX.App.Logging;
using Serilog;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ConsoleVirtualizationTests
{
    [Fact]
    public void 搜索图标清空焦点和菜单控制等级统计保持同步() => StaHost.Run(() =>
    {
        var resources = Application.Current.Resources;
        Application.Current.Resources = new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) };
        var window = new MainWindow { ShowActivated = false, Width = 960 };
        try
        {
            AppLog.UiSink.Clear();
            var parser = new Serilog.Parsing.MessageTemplateParser();
            foreach (var level in Enum.GetValues<Serilog.Events.LogEventLevel>())
                AppLog.UiSink.Emit(new Serilog.Events.LogEvent(DateTimeOffset.Now, level, null, parser.Parse($"匹配目标 {level}"), []));
            AppLog.UiSink.Emit(new Serilog.Events.LogEvent(DateTimeOffset.Now, Serilog.Events.LogEventLevel.Error, null, parser.Parse("其他错误"), []));
            window.Show();
            ((RadioButton)window.FindName("ConsoleTabBtn")).IsChecked = true;
            WaitForLogFlush();
            window.UpdateLayout();
            var search = (TextBox)window.FindName("ConsoleSearchBox");
            var placeholder = Assert.IsType<TextBlock>(window.FindName("ConsoleSearchPlaceholder"));
            var icon = Assert.IsType<TextBlock>(window.FindName("ConsoleSearchIcon"));
            var clear = Assert.IsType<Button>(window.FindName("ConsoleSearchClearButton"));
            var field = Assert.IsType<Border>(window.FindName("ConsoleSearchField"));
            var counts = Assert.IsType<ItemsControl>(window.FindName("ConsoleLevelCounts"));
            var list = (ListBox)window.FindName("ConsoleItemsControl");
            Assert.True(field.CornerRadius.TopLeft > 0);
            Assert.Equal(Visibility.Visible, placeholder.Visibility);
            Assert.False(placeholder.IsHitTestVisible);
            Assert.Equal(Visibility.Visible, icon.Visibility);
            Assert.Equal(Visibility.Collapsed, clear.Visibility);
            Assert.Equal(4, counts.Items.Count);
            Assert.Equal(new[] { 1, 1, 2, 1 }, counts.Items.Cast<object>().Select(ReadCount));
            search.Text = "  匹配目标  ";
            Assert.Equal(Visibility.Collapsed, placeholder.Visibility);
            Assert.Equal(Visibility.Collapsed, icon.Visibility);
            Assert.Equal(Visibility.Visible, clear.Visibility);
            Assert.Equal(4, list.Items.Count);
            Assert.All(counts.Items.Cast<object>(), item => Assert.Equal(1, ReadCount(item)));
            search.Text = "不存在的搜索";
            Assert.Empty(list.Items);
            Assert.Equal(4, counts.Items.Count);
            Assert.All(counts.Items.Cast<object>(), item => Assert.Equal(0, ReadCount(item)));
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("", search.Text);
            Assert.True(search.IsFocused);
            Assert.Equal(Visibility.Visible, placeholder.Visibility);
            Assert.Equal(Visibility.Visible, icon.Visibility);
            Assert.Equal(Visibility.Collapsed, clear.Visibility);
            Assert.Equal(5, list.Items.Count);
            var menus = new[] { "Verbose", "Debug", "Info", "Warning", "Error", "Fatal" }
                .Select(level => (MenuItem)window.FindName($"ConsoleLevel{level}MenuItem")).ToArray();
            foreach (var menu in menus) menu.IsChecked = true;
            menus[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(6, counts.Items.Count);
            Assert.Equal(new[] { 1, 1, 1, 1, 2, 1 }, counts.Items.Cast<object>().Select(ReadCount));
            AppLog.UiSink.Emit(new Serilog.Events.LogEvent(DateTimeOffset.Now, Serilog.Events.LogEventLevel.Fatal, null, parser.Parse("新增致命"), []));
            WaitForLogFlush();
            Assert.Equal(2, ReadCount(counts.Items[5]));
            window.UpdateLayout();
            var searchRight = field.TransformToAncestor(window).Transform(new Point(field.ActualWidth, 0)).X;
            var countsLeft = counts.TransformToAncestor(window).Transform(new Point(0, 0)).X;
            Assert.True(searchRight <= countsLeft);
            Assert.True(search.ActualWidth >= 40);
            var menuRoot = ((Button)window.FindName("ConsoleFilterButton")).ContextMenu;
            Assert.Contains(menuRoot.Items.Cast<object>(), item => item == window.FindName("ConsoleOpenLogsMenuItem"));
            Assert.Null(window.FindName("ConsoleCountText"));
            foreach (var menu in menus) menu.IsChecked = false;
            menus[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Empty(counts.Items);
            Assert.Empty(list.Items);
            AppLog.UiSink.Clear();
            Assert.Empty(counts.Items);
        }
        finally
        {
            typeof(MainWindow).GetField("_closeConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close();
            AppLog.UiSink.SetActive(false);
            AppLog.UiSink.Clear();
            Application.Current.Resources = resources;
        }
    });

    private static int ReadCount(object summary) => (int)summary.GetType().GetProperty("Count")!.GetValue(summary)!;

    [Fact]
    public void 各等级筛选复制和清空使用真实WPF控件() => StaHost.Run(() =>
    {
        var resources = Application.Current.Resources;
        Application.Current.Resources = new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) };
        var window = new MainWindow();
        try
        {
            AppLog.UiSink.Clear();
            var parser = new Serilog.Parsing.MessageTemplateParser();
            foreach (var level in Enum.GetValues<Serilog.Events.LogEventLevel>())
                AppLog.UiSink.Emit(new Serilog.Events.LogEvent(DateTimeOffset.Now, level, null, parser.Parse($"等级回归 {level}"), []));
            window.ShowActivated = false;
            window.Show();
            ((RadioButton)window.FindName("ConsoleTabBtn")).IsChecked = true;
            WaitForLogFlush();
            window.UpdateLayout();
            var list = (ListBox)window.FindName("ConsoleItemsControl");
            Assert.Equal(4, list.Items.Count);
            var menus = new[] { "Verbose", "Debug", "Info", "Warning", "Error", "Fatal" }
                .Select(level => (MenuItem)window.FindName($"ConsoleLevel{level}MenuItem")).ToArray();
            for (var index = 0; index < menus.Length; index++)
            {
                foreach (var menu in menus) menu.IsChecked = false;
                menus[index].IsChecked = true;
                menus[index].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal((Serilog.Events.LogEventLevel)index, Assert.IsType<UiLogEntry>(Assert.Single(list.Items.Cast<object>())).Level);
            }
            window.UpdateLayout();
            var copy = Descendants(list).OfType<Border>().Single(border => border.Cursor == System.Windows.Input.Cursors.Hand);
            copy.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert.Equal(((UiLogEntry)list.Items[0]).DisplayText, Clipboard.GetText());
            var context = (ContextMenu)window.Resources["ConsoleContextMenu"];
            ((MenuItem)context.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Empty(AppLog.UiSink.Entries);
            Assert.Equal(0, AppLog.UiSink.State.Count);
        }
        finally
        {
            typeof(MainWindow).GetField("_closeConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close();
            AppLog.UiSink.Clear();
            Application.Current.Resources = resources;
        }
    });

    [Fact]
    public void 五千条日志只生成视口内的回收容器() => StaHost.Run(() =>
    {
        var resources = Application.Current.Resources;
        Application.Current.Resources = new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) };
        var window = new MainWindow();
        try
        {
            var list = Assert.IsType<ListBox>(window.FindName("ConsoleItemsControl"));
            AppLog.UiSink.Clear();
            var parser = new Serilog.Parsing.MessageTemplateParser();
            for (var index = 0; index < 5000; index++)
                AppLog.UiSink.Emit(new Serilog.Events.LogEvent(DateTimeOffset.Now, Serilog.Events.LogEventLevel.Information,
                    null, parser.Parse(index >= 4998 ? $"容器验证 {index}\n第二行\n第三行" : $"容器验证 {index}"), []));
            window.ShowActivated = false;
            window.Show();
            ((RadioButton)window.FindName("ConsoleTabBtn")).IsChecked = true;
            AppLog.UiSink.SetActive(true);
            WaitForLogFlush();
            window.UpdateLayout();
            Assert.Equal(5000, list.Items.Count);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
            var containers = Descendants(list).OfType<ListBoxItem>().Count();
            Assert.InRange(containers, 1, 100);
            var frame = new System.Windows.Threading.DispatcherFrame();
            window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            window.UpdateLayout();
            var scroll = Descendants(list).OfType<ScrollViewer>().First();
            Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset);
        }
        finally
        {
            typeof(MainWindow).GetField("_closeConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close();
            AppLog.UiSink.SetActive(false);
            AppLog.UiSink.Clear();
            Application.Current.Resources = resources;
        }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void WaitForLogFlush()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(10),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle, (_, _) =>
            {
                if (!AppLog.UiSink.State.Scheduled || watch.Elapsed > TimeSpan.FromSeconds(10)) frame.Continue = false;
            }, Application.Current.Dispatcher);
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.False(AppLog.UiSink.State.Scheduled, "预算批次应最终显示完毕并停止调度");
    }
}
