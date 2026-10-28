using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using DiskPilotX.App.Logging;
using Serilog;
using Serilog.Events;

namespace DiskPilotX.App.Tests;

// 主题资源是进程级状态，和其他 WPF 控件测试串行运行，避免互相污染。
[Collection("聊天滚动控件")]
public sealed class ConsolePanelTests
{
    [Theory]
    [InlineData(LogEventLevel.Verbose, "#FF7AA2F7", null)]
    [InlineData(LogEventLevel.Debug, "#FFBB9AF7", null)]
    [InlineData(LogEventLevel.Information, "#FF9ECE6A", null)]
    [InlineData(LogEventLevel.Warning, "#FFE0AF68", "#FFFFC777")]
    [InlineData(LogEventLevel.Error, "#FFF7768E", "#FFFF9EAB")]
    [InlineData(LogEventLevel.Fatal, "#FFF7768E", "#FFFF9EAB")]
    public void 未初始化主题时日志仍有独立的语义基色(LogEventLevel level, string expected, string? expectedMessage) => StaHost.Run(() =>
    {
        var savedResources = Application.Current.Resources;
        var resources = new ResourceDictionary();
        Application.Current.Resources = resources;
        var keys = new[] { "VP.AccentTextBrush", "VP.AccentHoverTextBrush", "VP.SuccessTextBrush", "VP.WarningTextBrush", "VP.DangerTextBrush" };
        var originalKeys = resources.Keys.Cast<object>().ToHashSet();
        var previous = keys.Select(key => (Key: key, Exists: originalKeys.Contains(key), Value: resources[key])).ToArray();
        try
        {
            foreach (var themeColor in new[] { Brushes.White, Brushes.Black, Brushes.Magenta })
            {
                foreach (var key in keys) resources[key] = themeColor;
                var entry = new UiLogEntry(DateTimeOffset.Now, level, "语义色验证", null);
                var brush = Assert.IsType<SolidColorBrush>(entry.LevelBrush);
                Assert.Equal(expected, brush.Color.ToString());
                Assert.True(brush.IsFrozen);
                if (expectedMessage is not null)
                {
                    var messageBrush = Assert.IsType<SolidColorBrush>(entry.MessageBrush);
                    Assert.Equal(expectedMessage, messageBrush.Color.ToString());
                    Assert.True(messageBrush.IsFrozen);
                }
            }
        }
        finally
        {
            foreach (var item in previous)
                if (item.Exists) resources[item.Key] = item.Value;
                else resources.Remove(item.Key);
            Application.Current.Resources = savedResources;
            Assert.True(originalKeys.SetEquals(resources.Keys.Cast<object>()), "主题测试不得留下应用级资源覆盖项。");
        }
    });

    [Fact]
    public void 清空当前列表后仍能接收新日志() => StaHost.Run(() =>
    {
        var sink = new UiLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        logger.Information("清空前的信息");
        logger.Warning("清空前的警告");
        Assert.Equal(2, sink.Entries.Count);
        sink.Clear();
        Assert.Empty(sink.Entries);
        sink.Clear();
        logger.Error("清空后仍继续记录");
        var entry = Assert.Single(sink.Entries);
        Assert.Equal(LogEventLevel.Error, entry.Level);
        Assert.Equal("清空后仍继续记录", entry.Message);
    });

    [Fact]
    public void 右键清空同时覆盖空白区和日志文本且不新增按钮()
    {
        var document = LoadMainWindow();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var frame = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "ConsoleFrame");
        Assert.Equal("{StaticResource ConsoleContextMenu}", (string?)frame.Attribute("ContextMenu"));
        var text = Assert.Single(frame.Descendants().Where(element => element.Name.LocalName == "TextBox"));
        Assert.Equal("{StaticResource ConsoleContextMenu}", (string?)text.Attribute("ContextMenu"));
        var menu = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "ConsoleContextMenu");
        var clear = Assert.Single(menu.Elements());
        Assert.Equal("{loc:Loc ConsoleClearMenuItem}", (string?)clear.Attribute("Header"));
        Assert.Equal("ConsoleClearMenuItem_Click", (string?)clear.Attribute("Click"));
        Assert.DoesNotContain(document.Descendants().Where(element => element.Name.LocalName == "Button"),
            element => (string?)element.Attribute("Click") == "ConsoleClearMenuItem_Click");
    }

    [Fact]
    public void 控制台保持透明并共用推荐清理面板背景()
    {
        var document = LoadMainWindow();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var frame = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "ConsoleFrame");
        Assert.Equal("Transparent", (string?)frame.Attribute("Background"));
        var sharedSurface = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "BottomPanelSurface");
        Assert.Equal("{DynamicResource VP.SurfaceBrush}", (string?)sharedSurface.Attribute("Background"));
        Assert.Contains(sharedSurface.Parent!.Descendants(), element => element.Name.LocalName == "RecommendationsPanel");
        Assert.Contains(sharedSurface.Parent.Descendants(), element => element == frame);
    }

    [Fact]
    public void 面板投影只作用于背景而不作用于控制台文字()
    {
        var document = LoadMainWindow();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var content = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "BottomContentGrid");
        Assert.DoesNotContain(content.Ancestors(), element => element.Elements().Any(child => child.Name.LocalName.EndsWith(".Effect")));
        var surface = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "BottomPanelSurface");
        Assert.DoesNotContain(surface.Descendants(), element => element.Name.LocalName == "DropShadowEffect");
        var shadow = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "BottomPanelShadow");
        Assert.Contains(shadow.Descendants(), element => element.Name.LocalName == "DropShadowEffect");
        Assert.DoesNotContain(content.Ancestors(), element => element == shadow);
        Assert.DoesNotContain(content.Ancestors(), element => element == surface);
    }

    private static XDocument LoadMainWindow()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, "src/DiskPilotX.App/MainWindow.xaml");
            if (File.Exists(file)) return XDocument.Load(file);
        }
        throw new FileNotFoundException("未找到当前工作区的主窗口 XAML。");
    }
}
