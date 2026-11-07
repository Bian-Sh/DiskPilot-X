using System.Reflection;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Services.Cleanup;
namespace DiskPilotX.Core.Tests;
public class CleanupSystemProgressTests
{
    [Theory]
    [InlineData(DeletionMode.PermanentDelete)]
    [InlineData(DeletionMode.MoveToRecycleBin)]
    public void 系统操作只显示执行进度而不再次确认删除(DeletionMode mode)
    {
        var type=typeof(CleanupService).Assembly.GetType("DiskPilotX.Core.Services.Cleanup.SystemDeleteProgress");
        Assert.NotNull(type);
        var method=type.GetMethod("GetOperationFlags",BindingFlags.NonPublic|BindingFlags.Static);
        Assert.NotNull(method);
        var flags=(uint)method.Invoke(null,[mode])!;
        Assert.NotEqual(0u,flags & 0x0010u); // FOF_NOCONFIRMATION：应用已经批准，不二次确认。
        Assert.Equal(0u,flags & 0x0004u); // 不设置 FOF_SILENT，保留系统执行进度。
        Assert.NotEqual(0u,flags & 0x2000u); // 不连带删除未在批准范围的关联文件夹。
        Assert.Equal(mode==DeletionMode.MoveToRecycleBin, (flags & 0x00080000u)!=0);
    }
}