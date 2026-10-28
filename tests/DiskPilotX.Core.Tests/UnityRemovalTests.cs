using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.Core.Tests;

/// <summary>Unity 清理剥离后的宿主守护：内建工具、动作枚举与 Unity 专属推荐元数据都不应残留。</summary>
public sealed class UnityRemovalTests
{
    [Fact]
    public void 宿主不再注册Unity专用工具动作和推荐元数据()
    {
        var core = typeof(CleanupSkillStore).Assembly;
        Assert.Null(core.GetType("DiskPilotX.Core.Services.Agent.UnityRegistryAgentTool"));
        Assert.DoesNotContain(Enum.GetNames<AiActionKind>(), name => name.Contains("Unity", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(core.GetTypes(), type => type.GetProperties().Any(p => p.Name.StartsWith("Unity", StringComparison.Ordinal)));
    }
}
