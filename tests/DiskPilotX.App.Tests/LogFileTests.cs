using System.Text;
using Microsoft.VisualBasic.FileIO;
using DiskPilotX.App.Logging;
using Serilog;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class LogFileTests
{
    [Fact]
    public void 文件Logger关闭后不提前销毁应用共享日志缓存()
    {
        WithLogRoot(root =>
        {
            AppLog.Configure();
            Log.CloseAndFlush();
            AppLog.UiSink.Clear();
            AppLog.Configure();
            Log.Information("文件记录器重建后仍能采集");
            Assert.Equal(1, AppLog.UiSink.State.Count);
        });
    }

    [Fact]
    public void 真实文件滚动与周期刷新保留全部消息()
    {
        WithLogRoot(root =>
        {
            AppLog.Configure();
            var message = new string('测', 4000);
            for (var index = 0; index < 1000; index++) Log.Information("持久化 {Index} {Payload:l}", index, message);
            Thread.Sleep(1500);
            var files = Directory.GetFiles(AppLog.LogDirectory, "app-*.tsv");
            Assert.InRange(files.Length, 2, 14);
            var count = 0;
            foreach (var file in files)
            {
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var preamble = new byte[3];
                input.ReadExactly(preamble);
                Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, preamble);
                input.Position = 0;
                using var parser = new TextFieldParser(input, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
                parser.SetDelimiters("\t");
                Assert.Equal(new[] { "时间", "等级", "进程", "来源", "消息", "异常类型", "异常堆栈" }, parser.ReadFields());
                while (!parser.EndOfData)
                {
                    var fields = parser.ReadFields()!;
                    Assert.Equal(7, fields.Length);
                    Assert.EndsWith(message, fields[4]);
                    count++;
                }
            }
            Assert.Equal(1000, count);
            Log.Information("退出刷新标记");
            Log.CloseAndFlush();
            Assert.Contains(Directory.GetFiles(AppLog.LogDirectory, "app-*.tsv"), file => File.ReadAllText(file).Contains("退出刷新标记"));
        });
    }

    [Fact]
    public void 落盘目录不可写时保留独立诊断而不递归报错()
    {
        WithLogRoot(root =>
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "logs"), "本测试的阻塞夹具");
            AppLog.Configure();
            Log.Information("不能落盘的普通日志");
            var fallback = Path.Combine(Path.GetTempPath(), $"DiskPilot-X-{Environment.ProcessId}", "logging-failure.tsv");
            Assert.True(File.Exists(fallback));
            Assert.Contains(root, File.ReadAllText(fallback));
        });
    }

    private static void WithLogRoot(Action<string> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "DiskPilot-X-log-tests", Guid.NewGuid().ToString("N"));
        var pipe = Environment.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_PIPE");
        var settings = Environment.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS");
        var previous = Log.Logger;
        try
        {
            Environment.SetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_PIPE", "1");
            Environment.SetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS", Path.Combine(root, "settings.json"));
            run(root);
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = previous;
            Environment.SetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_PIPE", pipe);
            Environment.SetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS", settings);
        }
    }
}
