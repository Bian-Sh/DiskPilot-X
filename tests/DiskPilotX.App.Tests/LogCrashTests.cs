using System.Reflection;
using Microsoft.VisualBasic.FileIO;
using DiskPilotX.App.Logging;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class LogCrashTests
{
    [Fact]
    public void 可恢复异常不会阻止后续独立崩溃报告()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiskPilot-X-log-tests", Guid.NewGuid().ToString("N"));
        var pipe = Environment.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_PIPE");
        var settings = Environment.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS");
        try
        {
            Environment.SetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_PIPE", "1");
            Environment.SetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS", Path.Combine(root, "settings.json"));
            var type = typeof(AppLog).Assembly.GetType("DiskPilotX.App.Diagnostics.CrashDiagnostics")!;
            var directory = (string)type.GetProperty("LogDirectory")!.GetValue(null)!;
            Assert.StartsWith(root, directory);
            var capture = type.GetMethod("CaptureException")!;
            var exception = CreateException();
            capture.Invoke(null, ["第一次可恢复异常", exception, false]);
            capture.Invoke(null, ["第二次异常", exception, false]);
            var files = Directory.GetFiles(directory, "crash-*.tsv");
            Assert.Equal(2, files.Length);
            Assert.Equal(2, Directory.GetFiles(directory, "crash-*.log").Length);
            foreach (var file in files)
            {
                Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, File.ReadAllBytes(file)[..3]);
                using var parser = new TextFieldParser(file) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
                parser.SetDelimiters("\t");
                Assert.Equal("时间", parser.ReadFields()![0]);
                var fields = parser.ReadFields()!;
                Assert.Equal(11, fields.Length);
                Assert.Equal("System.InvalidOperationException", fields[3]);
                Assert.Equal("崩溃\t字段\n详情\"引号\"", fields[4]);
                Assert.Contains(nameof(CreateException), fields[5]);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_PIPE", pipe);
            Environment.SetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS", settings);
        }
    }

    private static Exception CreateException()
    {
        try { throw new InvalidOperationException("崩溃\t字段\n详情\"引号\""); }
        catch (Exception exception) { return exception; }
    }
}
