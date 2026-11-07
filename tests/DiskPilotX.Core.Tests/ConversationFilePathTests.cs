using System.Reflection;
using DiskPilotX.Core.Services.Assistant;
namespace DiskPilotX.Core.Tests;
public class ConversationFilePathTests
{
    [Fact] public void 路径由真实存储根返回为绝对JSON路径()
    {
        var root=Path.Combine(Path.GetTempPath(), "DiskPilot-X-路径夹具");
        var id=Guid.NewGuid().ToString("N");
        var method=typeof(ConversationStore).GetMethod("GetFilePath");
        Assert.NotNull(method);
        Assert.Equal(Path.GetFullPath(Path.Combine(root,id+".json")),method.Invoke(new ConversationStore(root),[id]));
    }
    [Theory] [InlineData("../越界")] [InlineData("not-a-guid")]
    public void 复制路径仍校验会话标识(string id)
    {
        var method=typeof(ConversationStore).GetMethod("GetFilePath"); Assert.NotNull(method);
        var exception=Assert.Throws<TargetInvocationException>(()=>method.Invoke(new ConversationStore(Path.GetTempPath()),[id]));
        Assert.IsType<InvalidDataException>(exception.InnerException);
    }
}
