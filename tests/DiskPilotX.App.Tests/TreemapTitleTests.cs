using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Converters;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;
using SkiaSharp;

namespace DiskPilotX.App.Tests;

public class TreemapTitleTests
{
    [Theory]
    [InlineData("旅行视频.mp4", "2.4 GB")]
    [InlineData("outputs", "15.2 GB")]
    [InlineData("+472 项", "46.1 KB")]
    public void 完整标题_容量紧随名称且由括号包裹(string name, string size)
    {
        using var font = Font();
        string expected = $"{name} ({size})";
        Assert.Equal(expected, Format(name, size, font, font.MeasureText(expected)));
    }

    [Theory]
    [InlineData("旅行视频文件.mp4", "旅")]
    [InlineData("long-video-name.mp4", "l")]
    [InlineData("👩‍🎨作品视频.mp4", "👩‍🎨")]
    [InlineData("e\u0301clair-video.mp4", "e\u0301")]
    public void 窄标题_保留完整首字符及完整容量(string name, string first)
    {
        using var font = Font();
        string expected = $"{first}... (2.4 GB)";
        float width = font.MeasureText(expected) + 0.01f;
        Assert.Equal(expected, Format(name, "2.4 GB", font, width));
    }

    [Fact]
    public void 名称首字符放不下时_整行隐藏而非仅显示省略号或容量()
    {
        using var font = Font();
        float width = font.MeasureText("旅... (2.4 GB)") - 0.1f;
        Assert.Null(Format("旅行视频.mp4", "2.4 GB", font, width));
        Assert.Null(Format("旅行视频.mp4", "2.4 GB", font, font.MeasureText("... (2.4 GB)")));
        Assert.Null(Format("旅行视频.mp4", "2.4 GB", font, 0));
    }

    [Fact]
    public void 完整短名称放得下时_不强制预留省略号()
    {
        using var font = Font();
        string expected = "片 (0 B)";
        Assert.Equal(expected, Format("片", "0 B", font, font.MeasureText(expected)));
    }

    [Fact]
    public void 布局为目录及文件分别保留名称和容量()
    {
        var root = Root();
        root.Children.Add(new FileEntry { Name = "旅行视频.mp4", Path = "D:/完整目录/旅行视频.mp4", Size = 1024, Parent = root });
        root.Size = 1024;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 800, 600, 64);
        Assert.All(nodes, node => Assert.Equal("1.0 KB", node.SizeLabel));
        Assert.Equal("旅行视频.mp4", Assert.Single(nodes.Where(n => !n.Entry.IsDirectory)).Label);
    }

    [Fact]
    public void 聚合块_标题和容量分开且使用聚合容量而非父目录容量()
    {
        var root = Root();
        for (int i = 0; i < 600; i++)
            root.Children.Add(new FileEntry { Name = $"文件{i}.js", Path = $"D:/完整目录/文件{i}.js", Size = 100, Parent = root });
        root.Size = 60000;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1200, 800, 64);
        var tail = Assert.Single(nodes.Where(n => n.AggregatedCount > 0));
        Assert.Equal("+472 项", tail.Label);
        Assert.Equal("46.1 KB", tail.SizeLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 隐藏标题的悬停信息_保留完整路径和对应容量(bool aggregated) => InSta(() =>
    {
        var root = Root(); root.Size = 60000;
        var node = new TreemapNode { Entry = root, Label = aggregated ? "+472 项" : null,
            AggregatedCount = aggregated ? 472 : 0, AggregatedSize = aggregated ? 47200 : null };
        var control = new TreemapControl();
        typeof(TreemapControl).GetMethod("UpdateToolTipContent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [node]);
        var tooltip = (ToolTip)typeof(TreemapControl).GetField("_toolTip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        var lines = ((StackPanel)tooltip.Content).Children.OfType<TextBlock>().Select(text => text.Text).ToArray();
        Assert.Contains(root.Path, lines);
        Assert.Contains(lines, line => line.Contains(FileSizeConverter.FormatSize(aggregated ? 47200 : 60000)));
        if (aggregated) Assert.Contains("+472 项", lines);
    });

    private static FileEntry Root() => new() { Name = "完整目录", Path = "D:/完整目录", IsDirectory = true };

    private static SKFont Font() => (SKFont)typeof(TreemapControl)
        .GetMethod("CreateTreemapFont", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [11f])!;

    private static string? Format(string name, string size, SKFont font, float width)
    {
        var method = typeof(TreemapControl).GetMethod("FormatLabel", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string?)method.Invoke(null, [name, size, font, width]);
    }

    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "悬停测试未在规定时间内完成");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
