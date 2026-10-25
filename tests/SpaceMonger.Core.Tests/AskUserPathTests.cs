using System.Text.Json.Nodes;
using SpaceMonger.Core.Models;
namespace SpaceMonger.Core.Tests;
public sealed class AskUserPathTests
{
    [Theory]
    [InlineData("D:/Projects/logs")]
    [InlineData(@"C:\Users\User\AppData\Local\npm-cache")]
    [InlineData("https://example.com/rules")]
    public void 普通路径和网址可作为默认文本(string value)
    {
        var request=UserDecisionRequest.Parse(new JsonObject { ["title"]="选择检查范围",["question"]="确认目录或参考资料",["fields"]=new JsonArray(new JsonObject { ["id"]="scope",["label"]="目录或网址",["type"]="text",["default_text"]=value }) });
        Assert.Equal(value,Assert.Single(request.Fields).DefaultText);
    }
}
