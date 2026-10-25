using System.Globalization;
using System.Text.Json;
using SpaceMonger.App.Views;

namespace SpaceMonger.App.Tests;

public class JsonDetailConverterTests
{
    [Fact]
    public void 树形结果按真实换行展示并保留元数据()
    {
        var input = JsonSerializer.Serialize(new { ok = true, base_path = @"D:\项目", path_format = "relative", count = 1,
            truncated = true, tree = "└─ \"a/b\"/\n   └─ \"cache.bin\" #1 {\"size_bytes\":12}" });
        var display = (string)new JsonDetailConverter().Convert(input, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Contains("D:\\\\项目", display);
        Assert.Contains("\"truncated\": true", display);
        Assert.Contains("\n   └─ \"cache.bin\"", display);
        Assert.DoesNotContain("\\n", display);
    }

    [Fact]
    public void 普通JSON和错误文本仍可展示()
    {
        var converter = new JsonDetailConverter();
        var json = (string)converter.Convert("{\"ok\":false,\"error\":\"路径不可访问\"}", typeof(string), null!, CultureInfo.InvariantCulture);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("路径不可访问", parsed.RootElement.GetProperty("error").GetString());
        Assert.Equal("工具失败", converter.Convert("工具失败", typeof(string), null!, CultureInfo.InvariantCulture));
    }
}
