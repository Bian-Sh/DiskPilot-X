using DiskPilotX.Core.Models;
namespace DiskPilotX.Core.Tests;
public sealed class SkillChartContractTests
{
    [Fact]
    public void 聊天只暴露显式条形图区块()
    {
        var chart=new SkillUiSection { Kind=SkillUiSectionKind.BarChart,Items=[new() { Metric=3 }] };
        var spec=new SkillUiSpec { Sections=[new() { Kind=SkillUiSectionKind.Table },new() { Kind=SkillUiSectionKind.PosterGrid,Items=[new() { Metric=999 }] },chart] };
        var property=typeof(SkillUiSpec).GetProperty("InlineCharts");Assert.NotNull(property);
        Assert.Same(chart,Assert.Single(Assert.IsAssignableFrom<IEnumerable<SkillUiSection>>(property!.GetValue(spec))));
    }
    [Fact]
    public void 图表最大值忽略负数和非有限值()
    {
        var chart=new SkillUiSection { Kind=SkillUiSectionKind.BarChart,Items=[new() { Metric=double.NaN },new() { Metric=double.PositiveInfinity },new() { Metric=-5 },new() { Metric=3 }] };
        Assert.Equal(3,chart.MaxMetric);
    }
}
