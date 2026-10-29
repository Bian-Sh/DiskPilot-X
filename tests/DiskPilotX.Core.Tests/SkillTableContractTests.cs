using System.Text.Json;
using DiskPilotX.Core.Models;
namespace DiskPilotX.Core.Tests;

public sealed class SkillTableContractTests
{
    [Fact]
    public void 新列契约保留表头和布局配置()
    {
        using var json = JsonDocument.Parse("""{"sections":[{"kind":"table","table_columns":[{"header":"项目","width":"2*"},{"header":"磁盘","width":"auto","min_width":48,"max_width":96},{"header":"容量","width":"100","align":"right"}],"items":[{"title":"项目甲","cells":["项目甲","E:\\","1 GB"]}]}]}""");
        var table = Assert.Single(SkillUiSpecContract.Parse(json.RootElement).Sections);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(table));
        Assert.True(serialized.RootElement.TryGetProperty("TableColumns", out var columns), "缺少可声明的内置表格列契约。");
        Assert.Equal(3, columns.GetArrayLength());
        Assert.Equal("2*", columns[0].GetProperty("Width").GetString());
        Assert.Equal("right", columns[2].GetProperty("Align").GetString());
        Assert.Equal(3, table.ColumnCount);
    }

    [Fact]
    public void 空表头时列数回退到单元格()
    {
        var table = new SkillUiSection { Kind=SkillUiSectionKind.Table, Headers=[], Items=[new SkillUiItem { Cells=["项目", "磁盘", "容量"] }] };
        Assert.Equal(3, table.ColumnCount);
    }

    [Fact]
    public void 非法列配置有界降级且限制六列()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { sections = new[] { new { kind="table", table_columns=Enumerable.Repeat(new { header="列", width="NaN*", min_width=-1, max_width=-8, align="evil" },9) } } }));
        var table = Assert.Single(SkillUiSpecContract.Parse(json.RootElement).Sections);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(table));
        Assert.True(serialized.RootElement.TryGetProperty("TableColumns", out var columns), "非法配置也必须经过统一列解析。");
        Assert.Equal(6, columns.GetArrayLength());
        Assert.Equal("auto", columns[0].GetProperty("Width").GetString());
        Assert.Equal("left", columns[0].GetProperty("Align").GetString());
        Assert.True(columns[0].GetProperty("MinWidth").GetDouble() >= 32);
        Assert.True(columns[0].GetProperty("MaxWidth").GetDouble() >= columns[0].GetProperty("MinWidth").GetDouble());
    }
}
