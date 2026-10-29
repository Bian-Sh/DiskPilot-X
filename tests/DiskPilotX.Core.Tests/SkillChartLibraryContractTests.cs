using System.Text.Json;
using DiskPilotX.Core.Models;
namespace DiskPilotX.Core.Tests;
public sealed class SkillChartLibraryContractTests
{
    [Theory]
    [InlineData("bar_chart","BarChart")]
    [InlineData("pie_chart","PieChart")]
    [InlineData("donut_chart","DonutChart")]
    public void 三类图表经JSON解析并仅显式图表内联(string kind,string expected)
    {
        using var json=JsonDocument.Parse($$"""{"sections":[{"kind":"{{kind}}","items":[{"title":"缓存","metric":8,"value":"8 GB"}]},{"kind":"poster_grid","items":[{"metric":99}]}]}""");
        var spec=SkillUiSpecContract.Parse(json.RootElement);
        Assert.Equal(2,spec.Sections.Count);
        Assert.Equal(expected,spec.Sections[0].Kind.ToString());
        Assert.Same(spec.Sections[0],Assert.Single(spec.InlineCharts));
        Assert.Equal(4,(int)SkillUiSectionKind.Text);
    }
    [Fact]
    public void 模型工具Schema同时公布三类图表()
    {
        var field=typeof(DiskPilotX.Core.Services.Assistant.DiskAssistantService).GetField("RenderViewSchema",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        using var json=JsonDocument.Parse(Assert.IsType<string>(field!.GetRawConstantValue()));
        var kinds=json.RootElement.GetProperty("properties").GetProperty("sections").GetProperty("items").GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(v=>v.GetString()).ToArray();
        Assert.Contains("bar_chart",kinds);Assert.Contains("pie_chart",kinds);Assert.Contains("donut_chart",kinds);
    }
}
