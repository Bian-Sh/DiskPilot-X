using System.Text.Json.Nodes;
using SpaceMonger.Core.Models;
using SpaceMonger.App.ViewModels;

namespace SpaceMonger.App.Tests;

public sealed class AskUserDialogViewModelTests
{
    private static AskUserDialogViewModel Dialog(string fields, bool allowCancel = true) => new(UserDecisionRequest.Parse(JsonNode.Parse("""{"title":"清理确认","question":"请选择清理方式","allow_cancel":CANCEL,"fields":FIELDS}""".Replace("FIELDS", fields).Replace("CANCEL", allowCancel ? "true" : "false"))));

    [Fact]
    public async Task 四类默认值提交为结构化JSON并只完成一次()
    {
        var vm = Dialog("""[{"id":"mode","label":"方式","type":"single_select","options":[{"id":"safe","label":"安全"}],"default_option_id":"safe"},{"id":"targets","label":"目标","type":"multi_select","options":[{"id":"a","label":"缓存"}],"default_option_ids":["a"]},{"id":"count","label":"数量","type":"number","default_number":4,"min_number":0,"max_number":10,"step":2},{"id":"note","label":"说明","type":"text","default_text":"先预览"}]""");
        Assert.True(vm.TryCompleteSubmission());
        var result = await vm.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(result["cancelled"]!.GetValue<bool>());
        Assert.Equal("safe", result["values"]!["mode"]!.GetValue<string>());
        Assert.Equal("a", result["values"]!["targets"]![0]!.GetValue<string>());
        Assert.Equal(4m, result["values"]!["count"]!.GetValue<decimal>());
        Assert.Equal("先预览", result["values"]!["note"]!.GetValue<string>());
        Assert.Empty(result["custom_inputs"]!.AsObject());
        vm.Cancel();
        Assert.False(vm.TryCompleteSubmission());
        Assert.Same(result, await vm.Completion);
        Assert.True(vm.IsCompleted);
    }

    [Fact]
    public async Task 单选其他答案与选项同时保留()
    {
        var vm = Dialog("""[{"id":"mode","label":"方式","type":"single_select","is_required":true,"options":[{"id":"safe","label":"安全"},{"id":"fast","label":"快速"}],"allow_custom_input":true,"max_length":80}]""");
        var field = Assert.IsType<AskUserSingleSelectFieldViewModel>(vm.CurrentField);
        field.Options[0].IsSelected = true;
        field.Options[1].IsSelected = true;
        Assert.False(field.Options[0].IsSelected);
        field.CustomInput = "先逐项确认";
        Assert.True(vm.TryCompleteSubmission());
        var result = await vm.Completion;
        Assert.Equal("fast", result["values"]!["mode"]!.GetValue<string>());
        Assert.Equal("先逐项确认", result["custom_inputs"]!["mode"]!.GetValue<string>());
    }

    [Fact]
    public async Task 必填单选允许只填写其他答案()
    {
        var vm = Dialog("""[{"id":"mode","label":"方式","type":"single_select","is_required":true,"options":[{"id":"safe","label":"安全"}],"allow_custom_input":true}]""");
        var field = Assert.IsType<AskUserSingleSelectFieldViewModel>(vm.CurrentField);
        Assert.False(vm.TryCompleteSubmission());
        field.CustomInput = "先预览";
        Assert.True(vm.TryCompleteSubmission());
        var result = await vm.Completion;
        Assert.Null(result["values"]!["mode"]);
        Assert.Equal("先预览", result["custom_inputs"]!["mode"]!.GetValue<string>());
    }

    [Fact]
    public async Task 必填和长度校验阻止前进且后退保留答案()
    {
        var vm = Dialog("""[{"id":"note","label":"说明","type":"text","is_required":true,"max_length":4},{"id":"extra","label":"备注","type":"text"}]""");
        var field = Assert.IsType<AskUserTextFieldViewModel>(vm.CurrentField);
        Assert.False(vm.TryAdvanceCurrentField());
        Assert.Equal(0, vm.CurrentFieldIndex);
        Assert.True(field.HasError);
        field.TextValue = "这段文字太长";
        Assert.False(vm.TryAdvanceCurrentField());
        field.TextValue = "先预览";
        Assert.True(vm.TryAdvanceCurrentField());
        vm.MovePrevious();
        Assert.Equal("先预览", field.TextValue);
        Assert.True(vm.TryAdvanceCurrentField());
        Assert.True(vm.TrySkipCurrentField(out var submit));
        Assert.True(submit);
        Assert.True(vm.TryCompleteSubmission());
        Assert.Null((await vm.Completion)["values"]!["extra"]);
    }

    [Fact]
    public void 提交全量校验会跳到先前遗漏的必填题()
    {
        var vm = Dialog("""[{"id":"first","label":"说明","type":"text","is_required":true},{"id":"second","label":"备注","type":"text"}]""");
        vm.MoveNextWithoutValidation();
        Assert.False(vm.TryCompleteSubmission());
        Assert.Equal(0, vm.CurrentFieldIndex);
        Assert.True(vm.CurrentField.HasError);
        Assert.False(vm.Completion.IsCompleted);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("1e999")]
    [InlineData("-2")]
    [InlineData("12")]
    [InlineData("3")]
    public async Task 无效数字不会沿用先前有效值(string text)
    {
        var vm = Dialog("""[{"id":"count","label":"数量","type":"number","is_required":true,"default_number":4,"min_number":0,"max_number":10,"step":2}]""");
        var field = Assert.IsType<AskUserNumberFieldViewModel>(vm.CurrentField);
        field.NumberText = text;
        Assert.False(vm.TryCompleteSubmission());
        Assert.True(field.HasError);
        Assert.False(vm.Completion.IsCompleted);
        field.NumberText = "6";
        Assert.True(vm.TryCompleteSubmission());
        Assert.Equal(6m, (await vm.Completion)["values"]!["count"]!.GetValue<decimal>());
    }

    [Fact]
    public void 必填多选不能跳过且检查最少最多数量()
    {
        var vm = Dialog("""[{"id":"targets","label":"目标","type":"multi_select","is_required":true,"min_selections":1,"max_selections":1,"options":[{"id":"a","label":"缓存"},{"id":"b","label":"日志"}]}]""");
        var field = Assert.IsType<AskUserMultiSelectFieldViewModel>(vm.CurrentField);
        Assert.False(vm.TrySkipCurrentField(out _));
        Assert.False(vm.TryCompleteSubmission());
        field.Options[0].IsSelected = true;
        field.Options[1].IsSelected = true;
        Assert.False(vm.TryCompleteSubmission());
        field.Options[1].IsSelected = false;
        Assert.True(vm.TryCompleteSubmission());
    }

    [Fact]
    public async Task 取消返回空结构且主流程可强制释放不可取消的等待()
    {
        var vm = Dialog("""[{"id":"note","label":"说明","type":"text","default_text":"尚未提交"}]""", allowCancel: false);
        Assert.False(vm.TryCancel());
        Assert.False(vm.Completion.IsCompleted);
        vm.Cancel();
        var result = await vm.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(result["cancelled"]!.GetValue<bool>());
        Assert.Empty(result["values"]!.AsObject());
        Assert.Empty(result["custom_inputs"]!.AsObject());
    }

    [Theory]
    [InlineData("sk-proj-abcdefghijklmnopqrstuv")]
    [InlineData("password=secret-value")]
    [InlineData("访问密钥")]
    public void 文本和其他答案都拒绝密钥(string secret)
    {
        var text = Dialog("""[{"id":"note","label":"说明","type":"text"}]""");
        Assert.IsType<AskUserTextFieldViewModel>(text.CurrentField).TextValue = secret;
        Assert.False(text.TryCompleteSubmission());
        Assert.DoesNotContain(secret, text.CurrentField.ErrorMessage);
        var select = Dialog("""[{"id":"mode","label":"方式","type":"single_select","options":[{"id":"a","label":"缓存"}],"allow_custom_input":true}]""");
        Assert.IsType<AskUserSingleSelectFieldViewModel>(select.CurrentField).CustomInput = secret;
        Assert.False(select.TryCompleteSubmission());
    }
}
