using System.Text.Json.Nodes;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Tests;

public sealed class AskUserRequestTests
{
    private static JsonObject Request(string fields) => JsonNode.Parse("""{"title":"清理确认","question":"请选择清理方式","fields":FIELDS}""".Replace("FIELDS", fields))!.AsObject();

    [Fact]
    public void 解析四类字段并保留默认值和限制()
    {
        var request = UserDecisionRequest.Parse(Request("""
        [
          {"id":"mode","label":"方式","type":"single_select","is_required":true,"options":[{"id":"safe","label":"安全","description":"逐项确认"}],"default_option_id":"safe","allow_custom_input":true,"max_length":80},
          {"id":"targets","label":"目标","type":"multi_select","options":[{"id":"a","label":"缓存"},{"id":"b","label":"日志"}],"default_option_ids":["a"],"min_selections":1,"max_selections":2},
          {"id":"count","label":"数量","type":"number","default_number":4,"min_number":0,"max_number":10,"step":2},
          {"id":"note","label":"说明","type":"text","default_text":"先预览","is_multiline":true,"max_length":20}
        ]
        """));
        Assert.Empty(UserDecisionValidator.ValidateRequest(request));
        Assert.Equal([UserDecisionFieldType.SingleSelect, UserDecisionFieldType.MultiSelect, UserDecisionFieldType.Number, UserDecisionFieldType.Text], request.Fields.Select(f => f.Type));
        Assert.Equal("safe", request.Fields[0].DefaultOptionId);
        Assert.Equal(80, request.Fields[0].MaxLength);
        Assert.Equal("逐项确认", request.Fields[0].Options[0].Description);
        Assert.Equal(["a"], request.Fields[1].DefaultOptionIds);
        Assert.Equal(4m, request.Fields[2].DefaultNumber);
        Assert.True(request.Fields[3].IsMultiline);
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""[{"id":"x","label":"方式","type":"single_select"}]""")]
    [InlineData("""[{"id":"x","label":"方式","type":"multi_select","options":[{"id":"a","label":"缓存"}],"max_selections":2}]""")]
    [InlineData("""[{"id":"x","label":"数量","type":"number","min_number":5,"max_number":1}]""")]
    [InlineData("""[{"id":"x","label":"数量","type":"number","step":0}]""")]
    [InlineData("""[{"id":"x","label":"数量","type":"number","default_number":3,"step":2}]""")]
    [InlineData("""[{"id":"x","label":"说明","type":"text","max_length":4001}]""")]
    [InlineData("""[{"id":"x","label":"说明","type":"text","is_required":"true"}]""")]
    [InlineData("""[{"id":"x","label":"说明","type":"text","default_text":null}]""")]
    [InlineData("""[{"id":"x","label":"说明","type":"text","unknown":true}]""")]
    [InlineData("""[{"id":"x","label":"说明","type":"unknown"}]""")]
    [InlineData("""[{"id":"x","label":"说明","type":"text"},{"id":"x","label":"备注","type":"text"}]""")]
    [InlineData("""[{"id":"x","label":"说明","type":"text","default_option_id":"a"}]""")]
    public void 无效请求在显示前被拒绝(string fields) => Assert.Throws<UserDecisionValidationException>(() => UserDecisionRequest.Parse(Request(fields)));

    [Theory]
    [InlineData("null")]
    [InlineData("""[]""")]
    [InlineData("""{"title":"确认","question":"清理哪里","fields":null}""")]
    [InlineData("""{"title":"确认","question":"清理哪里","allow_cancel":null}""")]
    [InlineData("""{"title":"确认","question":"清理哪里","unexpected":1}""")]
    public void 不把无效结构降级成旧协议(string json) => Assert.Throws<UserDecisionValidationException>(() => UserDecisionRequest.Parse(JsonNode.Parse(json)));

    [Theory]
    [InlineData("请输入 API Key")]
    [InlineData("请提供访问密钥")]
    [InlineData("请填写密码")]
    [InlineData("Paste your access token")]
    [InlineData("Authorization: Bearer abcdef")]
    [InlineData("sk-proj-abcdefghijklmnopqrstuv")]
    public void 问题不得索取或携带密钥(string question)
    {
        var node = Request("""[{"id":"answer","label":"回答","type":"text"}]""");
        node["question"] = question;
        var error = Assert.Throws<UserDecisionValidationException>(() => UserDecisionRequest.Parse(node));
        Assert.DoesNotContain(question, error.Message);
    }

    [Fact]
    public void 凭据名称用途允许名称术语但拒绝真实密钥与引用()
    {
        var node = Request("""[{"id":"name","label":"API Key 名称","type":"text","purpose":"credential_name","max_length":80}]""");
        node["question"] = "请选择 API Key 名称";
        var request = UserDecisionRequest.Parse(node);
        Assert.Empty(UserDecisionValidator.ValidateSubmission(request, new Dictionary<string, object?> { ["name"] = "生产 API Key" }));
        foreach (var value in new[] { "sk-proj-abcdefghijklmnopqrstuv", "authorization=secret-value", "LOOMX_CREDENTIAL_PROD", "a\nb" })
            Assert.NotEmpty(UserDecisionValidator.ValidateSubmission(request, new Dictionary<string, object?> { ["name"] = value }));
    }

    [Fact]
    public void 多选其他答案替代最少选择但仍限制最多数量()
    {
        var request = UserDecisionRequest.Parse(Request("""[{"id":"targets","label":"目标","type":"multi_select","is_required":true,"options":[{"id":"a","label":"缓存"},{"id":"b","label":"日志"}],"min_selections":1,"max_selections":1,"allow_custom_input":true,"max_length":4}]"""));
        var custom = new Dictionary<string, string> { ["targets"] = "临时文件" };
        Assert.Empty(UserDecisionValidator.ValidateSubmission(request, new Dictionary<string, object?> { ["targets"] = Array.Empty<string>() }, custom));
        Assert.NotEmpty(UserDecisionValidator.ValidateSubmission(request, new Dictionary<string, object?> { ["targets"] = new[] { "a", "b" } }, custom));
        custom["targets"] = "这段文字太长了";
        Assert.NotEmpty(UserDecisionValidator.ValidateSubmission(request, new Dictionary<string, object?>(), custom));
    }

    [Fact]
    public void 必填多选显式最少零仍不能提交空数组()
    {
        var request = UserDecisionRequest.Parse(Request("""[{"id":"targets","label":"目标","type":"multi_select","is_required":true,"min_selections":0,"options":[{"id":"a","label":"缓存"}]}]"""));
        Assert.NotEmpty(UserDecisionValidator.ValidateSubmission(request, new Dictionary<string, object?> { ["targets"] = Array.Empty<string>() }));
    }

    [Theory]
    [InlineData("请输入真实的 API Key")]
    [InlineData("请粘贴实际密钥")]
    [InlineData("Paste your API key")]
    public void 凭据名称用途也不得索取密钥值(string question)
    {
        var node = Request("""[{"id":"name","label":"凭据名称","type":"text","purpose":"credential_name"}]""");
        node["question"] = question;
        Assert.Throws<UserDecisionValidationException>(() => UserDecisionRequest.Parse(node));
    }
}
