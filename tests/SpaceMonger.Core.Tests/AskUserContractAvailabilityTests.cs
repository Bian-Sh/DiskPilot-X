using System.Reflection;
using System.Text.Json.Nodes;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Tests;

public sealed class AskUserContractAvailabilityTests
{
    [Fact]
    public void 公开解析入口能够接收旧协议并返回文本题()
    {
        // 通过反射先运行红灯测试，确保缺少实现时是断言失败，而不是编译错误。
        var type = typeof(ChatMessage).Assembly.GetType("SpaceMonger.Core.Models.AskUserRequest")
            ?? typeof(ChatMessage).Assembly.GetType("SpaceMonger.Core.Models.UserDecisionRequest");
        Assert.NotNull(type);
        var parse = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(parse);
        var request = parse.Invoke(null, [JsonNode.Parse("""{"title":"确认","question":"希望清理哪里？"}""")]);
        Assert.NotNull(request);
        Assert.Equal("确认", type.GetProperty("Title")!.GetValue(request));
        var fields = Assert.IsAssignableFrom<System.Collections.IEnumerable>(type.GetProperty("Fields")!.GetValue(request));
        Assert.Single(fields.Cast<object>());
    }
}
