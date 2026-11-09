using System.Globalization;
using System.IO;
using System.Text;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Sinks.File;

namespace DiskPilotX.App.Logging;

public sealed class LogTsvFormatter : ITextFormatter
{
    public static readonly string[] Columns = ["时间", "等级", "进程", "来源", "消息", "异常类型", "异常堆栈"];
    private static readonly MessageTemplateTextFormatter MessageFormatter = new("{Message:lj}", CultureInfo.InvariantCulture);

    public static string RenderMessage(LogEvent logEvent)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        MessageFormatter.Format(logEvent, writer);
        return writer.ToString();
    }

    public void Format(LogEvent logEvent, TextWriter output) => WriteRow(output,
        logEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture), logEvent.Level.ToString(),
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
        logEvent.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string source } ? source : "Runtime",
        RenderMessage(logEvent), logEvent.Exception?.GetType().FullName, logEvent.Exception?.ToString());

    public static void WriteRow(TextWriter output, params string?[] fields)
    {
        for (var index = 0; index < fields.Length; index++)
        {
            if (index > 0) output.Write('\t');
            var field = fields[index] ?? "";
            if (field.IndexOfAny(['\t', '\r', '\n', '"']) >= 0)
            {
                output.Write('"');
                output.Write(field.Replace("\"", "\"\""));
                output.Write('"');
            }
            else output.Write(field);
        }
        output.WriteLine();
    }
}

public sealed class TsvLogFileHooks : FileLifecycleHooks
{
    public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
    {
        if (underlyingStream.Length == 0)
        {
            using var writer = new StreamWriter(underlyingStream, new UTF8Encoding(true), leaveOpen: true);
            LogTsvFormatter.WriteRow(writer, LogTsvFormatter.Columns);
            writer.Flush();
        }
        return underlyingStream;
    }
}
