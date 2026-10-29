using System.Globalization;
using DiskPilotX.App.Converters;
namespace DiskPilotX.App.Tests;
public sealed class SkillChartWidthTests
{
    [Fact]
    public void 条宽使用容器实际宽度且非法值不画条()
    {
        var converter=new RatioToLengthConverter();
        Assert.Equal(100d,converter.Convert([2d,4d,200d],typeof(double),null!,CultureInfo.InvariantCulture));
        foreach(var value in new[] { -1d,double.NaN,double.PositiveInfinity })
            Assert.Equal(0d,converter.Convert([value,4d,200d],typeof(double),null!,CultureInfo.InvariantCulture));
    }
}
