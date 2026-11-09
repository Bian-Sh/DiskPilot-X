using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using DiskPilotX.App.Logging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Parsing;

namespace DiskPilotX.App.Tests;

public sealed class LogTsvTests
{
    [Fact]
    public void 标准Tab解析器可完整还原中文引号换行和异常()
    {
        var type = typeof(AppLog).Assembly.GetType("DiskPilotX.App.Logging.LogTsvFormatter");
        Assert.NotNull(type);
        var formatter = Assert.IsAssignableFrom<ITextFormatter>(Activator.CreateInstance(type!));
        var message = "中文\t列内Tab\r\n第二行\"引号\"";
        var exception = new InvalidOperationException("异常\t正文\n详情");
        var entry = new LogEvent(new DateTimeOffset(2026, 11, 9, 12, 34, 56, TimeSpan.FromHours(8)),
            LogEventLevel.Error, exception, new MessageTemplateParser().Parse("{Message}"),
            [new LogEventProperty("Message", new ScalarValue(message)), new LogEventProperty("SourceContext", new ScalarValue("测试模块"))]);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        formatter.Format(entry, writer);
        using var parser = new TextFieldParser(new StringReader(writer.ToString())) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters("\t");
        var fields = parser.ReadFields()!;
        Assert.Equal(7, fields.Length);
        Assert.Equal("Error", fields[1]);
        Assert.Equal("测试模块", fields[3]);
        Assert.Equal(message, fields[4]);
        Assert.Equal("System.InvalidOperationException", fields[5]);
        Assert.Equal(exception.ToString(), fields[6]);
        Assert.True(parser.EndOfData);
    }
}
