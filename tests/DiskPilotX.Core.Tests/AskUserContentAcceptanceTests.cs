using System.Text.Json.Nodes;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Tests;

public sealed class AskUserContentAcceptanceTests
{
    [Theory]
    [InlineData("请勿提供密码或密钥。")]
    [InlineData("password=仅为测试的字面文本")]
    [InlineData("Authorization: Bearer fixture-only")]
    [InlineData("{\"配置\":\"测试\"}")]
    [InlineData("[规则]\n模式 = \"预览\"\n数量 = 3")]
    [InlineData("sk-proj-abcdefghijklmnopqrstuv")]
    public void 描述默认值和答案原样往返而不检查内容(string content)
    {
        var node = new JsonObject
        {
            ["title"] = content, ["question"] = content, ["reason"] = content, ["impact_summary"] = content,
            ["fields"] = new JsonArray(new JsonObject
            {
                ["id"] = "password", ["label"] = content, ["type"] = "text", ["default_text"] = content,
            }),
        };
        var request = UserDecisionRequest.Parse(node);
        Assert.Equal(content, request.Fields[0].DefaultText);
        var values = new Dictionary<string, object?> { ["password"] = content };
        Assert.Empty(UserDecisionValidator.ValidateSubmission(request, values));
        Assert.Equal(content, UserDecisionResult.Submit(values).ToJson()["values"]!["password"]!.GetValue<string>());
    }

    [Fact]
    public void 选项标识标题说明和自由输入提示不按关键词拒绝()
    {
        var request = UserDecisionRequest.Parse(JsonNode.Parse("""
        {"title":"配置规则","question":"请勿提供密码或密钥。","fields":[
          {"id":"api_key","label":"密钥缓存","type":"single_select","options":[
            {"id":"token","label":"密码缓存","description":"Authorization: Bearer fixture-only"}],
           "allow_custom_input":true,"custom_input_placeholder":"填写包含 password 的规则说明"}]}
        """));
        var custom = new Dictionary<string, string> { ["api_key"] = "password=测试资料" };
        Assert.Empty(UserDecisionValidator.ValidateSubmission(request, new Dictionary<string, object?>(), custom));
        Assert.Equal("password=测试资料", UserDecisionResult.Submit(new Dictionary<string, object?>(), custom)
            .ToJson()["custom_inputs"]!["api_key"]!.GetValue<string>());
    }

    [Fact]
    public void 旧单文本协议也允许结构化资料()
    {
        var request = UserDecisionRequest.Parse(new JsonObject
        {
            ["title"] = "资料", ["question"] = "{\"password\":\"测试\"}",
        });
        Assert.Single(request.Fields);
        Assert.Empty(UserDecisionValidator.ValidateSubmission(request,
            new Dictionary<string, object?> { ["answer"] = "Authorization: Bearer fixture-only" }));
    }
}
