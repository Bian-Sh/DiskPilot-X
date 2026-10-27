using System.Reflection;

namespace DiskPilotX.Core.Tests;

public sealed class AnalysisContextStoreTests
{
    [Fact]
    public void 新存储默认关闭行为记录()
    {
        // 先通过反射验证尚未实现的公开入口，确保观察到断言失败而不是编译错误。
        var type = Assembly.Load("DiskPilotX.Core")
            .GetType("DiskPilotX.Core.Services.Analysis.AnalysisContextStore");
        Assert.NotNull(type);
        var directory = Path.Combine(Path.GetTempPath(), "DiskPilot-X-分析上下文测试-" + Guid.NewGuid().ToString("N"));
        var store = Activator.CreateInstance(type, [directory, null]);
        Assert.False((bool)type.GetProperty("UsageRecordingEnabled")!.GetValue(store)!);
        Assert.False(Directory.Exists(directory));
    }
}