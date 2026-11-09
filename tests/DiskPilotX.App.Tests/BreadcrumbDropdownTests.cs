using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class BreadcrumbDropdownTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void 外部目录箭头无论后面是否有路径段都列出真实子目录(bool enterChild, bool hasScan)
    {
        var path = Path.Combine(Path.GetTempPath(), "DiskPilotX-breadcrumb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            foreach (var name in new[] { "envs", "FunGen", "patterns", "pkgs" })
                Directory.CreateDirectory(Path.Combine(path, name));
            File.WriteAllText(Path.Combine(path, "不是目录.txt"), "回归测试");
            WithWindow((window, vm) =>
            {
                if (hasScan)
                    vm.SetRoot(new FileEntry { Path = Path.Combine(path, "未扫描的其他根"), IsDirectory = true });
                vm.NavigateToExternalPath(enterChild ? Path.Combine(path, "envs") : path);
                Rebuild(window);

                var items = OpenDropdown(window, path);

                Assert.Equal(new[] { "envs", "FunGen", "patterns", "pkgs" }, items.Select(Name));
                Assert.All(items, item => Assert.Equal(Path.Combine(path, Name(item)), Property(item, "Path")));
            });
        }
        finally { Directory.Delete(path, recursive: true); }
    }

    [Fact]
    public void 扫描节点箭头保留扫描子目录及导航对象()
    {
        WithWindow((window, vm) =>
        {
            var path = Path.Combine(Path.GetTempPath(), "DiskPilotX-unavailable-" + Guid.NewGuid().ToString("N"));
            var root = new FileEntry { Path = path, Name = "扫描根", IsDirectory = true };
            var child = new FileEntry { Path = Path.Combine(path, "envs"), Name = "envs", IsDirectory = true, Parent = root };
            root.Children.Add(child);
            vm.SetRoot(root);
            vm.DrillDown(child);
            Rebuild(window);

            var item = Assert.Single(OpenDropdown(window, path));

            Assert.Equal("envs", Name(item));
            Assert.Same(child, Property(item, "Entry"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 点击中间或末尾箭头时菜单紧贴对应按钮下方(bool trailing)
    {
        WithWindow((window, vm) =>
        {
            var path = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "面包屑定位夹具");
            vm.NavigateToExternalPath(trailing ? path : Path.Combine(path, "envs"));
            Rebuild(window);
            window.ShowActivated = false;
            window.Left = 600;
            window.Top = 250;
            window.Show();
            window.UpdateLayout();
            var button = FindDropdownButton(window, path);
            var menu = button.ContextMenu!;
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var frame = new DispatcherFrame();
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                menu.UpdateLayout();

                Assert.True(menu.IsOpen);
                var anchor = button.PointToScreen(new Point(0, button.ActualHeight));
                var origin = menu.PointToScreen(new Point());
                Assert.InRange(origin.X - anchor.X, -24, 24);
                Assert.InRange(origin.Y - anchor.Y, -2, 24);
            }
            finally { menu.IsOpen = false; }
        });
    }

    private static object[] OpenDropdown(MainWindow window, string path)
    {
        var button = FindDropdownButton(window, path);
        var menu = button.ContextMenu!;
        menu.PlacementTarget = button;
        typeof(MainWindow).GetMethod("BreadcrumbDropdown_Opened", PrivateInstance)!.Invoke(window, [menu, new RoutedEventArgs()]);
        return menu.ItemsSource.Cast<object>().ToArray();
    }

    private static Button FindDropdownButton(MainWindow window, string path)
    {
        var bar = (StackPanel)window.FindName("BreadcrumbBar");
        return Assert.Single(bar.Children.OfType<Button>().Where(button =>
            button.ContextMenu is not null && button.Tag is ValueTuple<string, string, FileEntry?> tag
            && string.Equals(tag.Item1.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)));
    }

    private static void Rebuild(MainWindow window) =>
        typeof(MainWindow).GetMethod("RebuildBreadcrumbBar", PrivateInstance)!.Invoke(window, null);

    private static object? Property(object item, string name) => item.GetType().GetProperty(name)!.GetValue(item);
    private static string Name(object item) => (string)Property(item, "Name")!;

    private static void WithWindow(Action<MainWindow, TreemapViewModel> action) => StaHost.Run(() =>
    {
        var resources = Application.Current.Resources;
        Application.Current.Resources = new ResourceDictionary
        {
            Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative)
        };
        var window = new MainWindow();
        try
        {
            var vm = new TreemapViewModel(new SquarifiedTreemapLayout());
            typeof(MainWindow).GetField("_treemapViewModel", PrivateInstance)!.SetValue(window, vm);
            action(window, vm);
        }
        finally
        {
            typeof(MainWindow).GetField("_closeConfirmed", PrivateInstance)!.SetValue(window, true);
            window.Close();
            Application.Current.Resources = resources;
        }
    });
}
