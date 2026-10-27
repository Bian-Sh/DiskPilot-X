using DiskPilotX.App.Logging;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DiskPilotX.App.Tests;

// 替换进程级日志器时复用现有串行分组，避免影响其他测试。
[Collection("聊天滚动控件")]
public sealed class TreeViewDiagnosticsTests
{
    [Fact]
    public void 正常重建和展开只记录调试诊断且不提示崩溃()
    {
        var events = new List<LogEvent>();
        var previous = Log.Logger;
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose()
            .WriteTo.Sink(new CollectingSink(events)).CreateLogger();
        try
        {
            Log.Logger = logger;
            ExerciseTree();
            Assert.Contains(events, entry => entry.RenderMessage().Contains("TreeView.SetRoot"));
            Assert.Contains(events, entry => entry.RenderMessage().Contains("TreeView.RebuildTree"));
            Assert.Contains(events, entry => entry.RenderMessage().Contains("TreeView.LoadChildren"));
            var defaultLevels = AppLogLevelFilter.Information | AppLogLevelFilter.Warning | AppLogLevelFilter.Error | AppLogLevelFilter.Fatal;
            Assert.All(events, entry =>
            {
                Assert.Equal(LogEventLevel.Debug, entry.Level);
                Assert.DoesNotContain("crash", entry.RenderMessage().ToLowerInvariant());
                Assert.False(defaultLevels.Includes(entry.Level));
            });
        }
        finally { Log.Logger = previous; }
    }

    [Fact]
    public void 树形列表调试信息仍写入文件且真实错误保持可见()
    {
        var file = Path.Combine(Path.GetTempPath(), "DiskPilot-X-tree-diagnostics-" + Guid.NewGuid().ToString("N") + ".log");
        var events = new List<LogEvent>();
        var previous = Log.Logger;
        try
        {
            using (var logger = new LoggerConfiguration().MinimumLevel.Verbose()
                .WriteTo.File(file).WriteTo.Sink(new CollectingSink(events)).CreateLogger())
            {
                Log.Logger = logger;
                ExerciseTree();
                logger.Error(new InvalidOperationException("异常验证"), "真实错误验证");
            }
            var text = File.ReadAllText(file);
            Assert.Contains("TreeView.LoadChildren", text);
            Assert.Contains("真实错误验证", text);
            var error = Assert.Single(events.Where(entry => entry.Level == LogEventLevel.Error));
            Assert.True(AppLogLevelFilter.Error.Includes(error.Level));
            Assert.IsType<InvalidOperationException>(error.Exception);
        }
        finally
        {
            Log.Logger = previous;
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static void ExerciseTree()
    {
        var root = new FileEntry { Path = "C:/验收", Name = "验收", IsDirectory = true };
        var child = new FileEntry { Path = "C:/验收/子目录", Name = "子目录", IsDirectory = true, Parent = root };
        root.Children.Add(child);
        var viewModel = new TreeViewModel();
        viewModel.SetRoot(root);
        viewModel.SelectEntry(child);
        Assert.True(viewModel.SelectedItem!.IsExpanded);
    }

    private sealed class CollectingSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
