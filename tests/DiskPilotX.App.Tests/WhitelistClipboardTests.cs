using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;

namespace DiskPilotX.App.Tests;

[Collection("CLI 指纹状态")]
public sealed class WhitelistClipboardTests
{
    [Theory]
    [InlineData("scan")]
    [InlineData("cleanup")]
    public void 复制占用异常变为可恢复提示且成功后清除提示(string kind)
    {
        using var f = new Fixture();
        f.WriteFailure = new COMException("剪贴板被占用", unchecked((int)0x800401D0));
        var before = JsonSerializer.Serialize(f.Saved);
        Assert.Null(Record.Exception(() => f.Vm.CopyWhitelistCommand.Execute(kind)));
        Assert.False(string.IsNullOrWhiteSpace(f.Vm.WhitelistErrorMessage));
        Assert.Equal(before, JsonSerializer.Serialize(f.Saved));
        f.WriteFailure = null;
        f.Vm.CopyWhitelistCommand.Execute(kind);
        Assert.Null(f.Vm.WhitelistErrorMessage);
        Assert.Single(JsonSerializer.Deserialize<List<PathWhitelistEntry>>(f.Text)!);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("cleanup")]
    public void 粘贴占用异常不退出且不改动原有数据(string kind)
    {
        using var f = new Fixture();
        f.ReadFailure = new COMException("剪贴板被占用", unchecked((int)0x800401D0));
        var before = JsonSerializer.Serialize(f.Saved);
        Assert.Null(Record.Exception(() => f.Vm.PasteWhitelistCommand.Execute(kind)));
        Assert.False(string.IsNullOrWhiteSpace(f.Vm.WhitelistErrorMessage));
        Assert.Equal(before, JsonSerializer.Serialize(f.Saved));
        f.Store.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("cleanup")]
    public void 复制与粘贴配置可往返且立即保存并且不影响其他分组(string kind)
    {
        using var source = new Fixture();
        source.Entries(kind).Clear();
        source.Entries(kind).Add(new() { Path = @" D:\Shared\RoundTrip ", Description = " 往返备注 " });
        source.Vm.CopyWhitelistCommand.Execute(kind);
        var export = Assert.Single(JsonSerializer.Deserialize<List<PathWhitelistEntry>>(source.Text)!);
        Assert.Equal(@"D:\Shared\RoundTrip", export.Path);
        Assert.Equal("往返备注", export.Description);
        using var target = new Fixture { Text = source.Text };
        var otherKind = kind == "scan" ? "cleanup" : "scan";
        var otherBefore = JsonSerializer.Serialize(target.Entries(otherKind));
        var legacyBefore = JsonSerializer.Serialize(target.Saved.AiConversationWhitelist);
        target.Vm.PasteWhitelistCommand.Execute(kind);
        Assert.Null(target.Vm.WhitelistErrorMessage);
        Assert.Equal(2, target.Entries(kind).Count);
        Assert.Contains(target.Entries(kind), e => e.Path == export.Path && e.Description == export.Description);
        Assert.Equal(JsonSerializer.Serialize(target.Entries(kind)), JsonSerializer.Serialize(target.Persisted(kind)));
        Assert.Equal(otherBefore, JsonSerializer.Serialize(target.Entries(otherKind)));
        Assert.Equal(legacyBefore, JsonSerializer.Serialize(target.Saved.AiConversationWhitelist));
        target.Store.Received(1).SaveSettings(target.Saved);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("cleanup")]
    public void 重复路径忽略大小写末尾分隔符和点段且重复粘贴幂等(string kind)
    {
        using var f = new Fixture();
        f.Text = JsonSerializer.Serialize(new[]
        {
            new PathWhitelistEntry { Path = @"d:\work\protected\", Description = "新备注" },
            new PathWhitelistEntry { Path = "D:/Work/./Protected", Description = " " },
            new PathWhitelistEntry { Path = @"D:\Work\Protected\Child", Description = "子项仍独立保留" }
        });
        f.Vm.PasteWhitelistCommand.Execute(kind);
        f.Vm.PasteWhitelistCommand.Execute(kind);
        Assert.Null(f.Vm.WhitelistErrorMessage);
        Assert.Equal(2, f.Entries(kind).Count);
        Assert.Equal("新备注", f.Entries(kind).Single(e => e.Path == @"D:\Work\Protected").Description);
        Assert.Contains(f.Entries(kind), e => e.Path.EndsWith(@"\Child") && e.Description == "子项仍独立保留");
        Assert.Equal(JsonSerializer.Serialize(f.Entries(kind)), JsonSerializer.Serialize(f.Persisted(kind)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void 重复条目的空备注不覆盖旧备注(string? note)
    {
        using var f = new Fixture();
        f.Text = JsonSerializer.Serialize(new[] { new PathWhitelistEntry { Path = @"D:\WORK\PROTECTED\", Description = note } });
        f.Vm.PasteWhitelistCommand.Execute("scan");
        Assert.Equal("旧备注", Assert.Single(f.Vm.ScanWhitelist).Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("不是配置")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[42]")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"Path\":null}]")]
    [InlineData("[{\"Path\":\"  \"}]")]
    [InlineData("[{\"Path\":\"D:/New\"},null]")]
    public void 无效配置或空条目不抛异常也不部分写入(string text)
    {
        using var f = new Fixture { Text = text };
        var before = JsonSerializer.Serialize(f.Saved);
        Assert.Null(Record.Exception(() => f.Vm.PasteWhitelistCommand.Execute("scan")));
        Assert.False(string.IsNullOrWhiteSpace(f.Vm.WhitelistErrorMessage));
        Assert.Equal(before, JsonSerializer.Serialize(f.Saved));
        Assert.Single(f.Vm.ScanWhitelist);
        f.Store.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
    }

    [Fact]
    public void 空数组是有效合并但不会清空原有白名单()
    {
        using var f = new Fixture { Text = "[]" };
        var before = JsonSerializer.Serialize(f.Saved);
        f.Vm.PasteWhitelistCommand.Execute("scan");
        Assert.Null(f.Vm.WhitelistErrorMessage);
        Assert.Equal(before, JsonSerializer.Serialize(f.Saved));
        Assert.Single(f.Vm.ScanWhitelist);
    }

    [Fact]
    public void 小写字段可以导入且配置内重复备注取最后一个非空值()
    {
        using var f = new Fixture
        {
            Text = """[{"path":"D:/Work/Protected","description":"较早备注"},{"PATH":"d:/work/protected/","DESCRIPTION":"最终备注"},{"Path":"D:/Work/Protected","Description":""}]"""
        };
        f.Vm.PasteWhitelistCommand.Execute("scan");
        Assert.Null(f.Vm.WhitelistErrorMessage);
        Assert.Equal("最终备注", Assert.Single(f.Vm.ScanWhitelist).Description);
    }
    [Theory]
    [InlineData("scan")]
    [InlineData("cleanup")]
    public void 复制成功立即显示本地化Toast(string kind)
    {
        using var f = new Fixture();
        f.Vm.CopyWhitelistCommand.Execute(kind);
        Assert.True(f.Vm.IsSaveToastVisible);
        Assert.Equal(DiskPilotX.App.Localization.L.Text("SettingsWhitelistCopiedToast"), f.Vm.SaveToastMessage);
        Assert.Null(f.Vm.WhitelistErrorMessage);
    }

    [Fact]
    public async Task 复制成功Toast约两秒后自动收起()
    {
        using var f = new Fixture();
        f.Vm.CopyWhitelistCommand.Execute("scan");
        Assert.True(f.Vm.IsSaveToastVisible);
        await Task.Delay(2400);
        Assert.False(f.Vm.IsSaveToastVisible);
    }

    [Fact]
    public async Task 连续复制重新计时旧任务不会提前收起新Toast()
    {
        using var f = new Fixture();
        f.Vm.CopyWhitelistCommand.Execute("scan");
        await Task.Delay(1100);
        f.Vm.CopyWhitelistCommand.Execute("cleanup");
        Assert.True(f.Vm.IsSaveToastVisible);
        await Task.Delay(1200);
        Assert.True(f.Vm.IsSaveToastVisible);
        await Task.Delay(1100);
        Assert.False(f.Vm.IsSaveToastVisible);
    }

    [Fact]
    public void 复制失败收起旧成功Toast而只保留错误反馈()
    {
        using var f = new Fixture();
        f.Vm.CopyWhitelistCommand.Execute("scan");
        Assert.True(f.Vm.IsSaveToastVisible);
        f.WriteFailure = new COMException("剪贴板被占用", unchecked((int)0x800401D0));
        f.Vm.CopyWhitelistCommand.Execute("scan");
        Assert.False(f.Vm.IsSaveToastVisible);
        Assert.False(string.IsNullOrWhiteSpace(f.Vm.WhitelistErrorMessage));
    }

    [Fact]
    public void 设置页重新打开时可以显式清除Toast()
    {
        using var f = new Fixture();
        f.Vm.CopyWhitelistCommand.Execute("scan");
        Assert.True(f.Vm.IsSaveToastVisible);
        f.Vm.HideSaveToast();
        Assert.False(f.Vm.IsSaveToastVisible);
    }

    private sealed class Fixture : IDisposable
    {
        public string Text = "";
        public Exception? ReadFailure;
        public Exception? WriteFailure;
        public AppSettings Saved { get; } = new()
        {
            ScanWhitelist = [new() { Path = @"D:\Work\Protected", Description = "旧备注" }],
            CleanupRecommendationWhitelist = [new() { Path = @"D:\Work\Protected", Description = "旧备注" }],
            AiConversationWhitelist = [new() { Path = @"D:\Legacy", Description = "旧保护规则不清空" }]
        };
        public ISettingsService Store { get; } = Substitute.For<ISettingsService>();
        public SettingsViewModel Vm { get; }
        private readonly ModelHttpClientFactory _clients = new();
        public Fixture()
        {
            Store.LoadSettings().Returns(Saved);
            Vm = new SettingsViewModel(Store, new ModelConnectionTester(new ModelClientFactory(_clients)), new ThemeManager(Store),
                readClipboard: () => { if (ReadFailure != null) throw ReadFailure; return Text; },
                writeClipboard: text => { if (WriteFailure != null) throw WriteFailure; Text = text; });
        }
        public ObservableCollection<PathWhitelistEntry> Entries(string kind) => kind == "scan" ? Vm.ScanWhitelist : Vm.CleanupRecommendationWhitelist;
        public List<PathWhitelistEntry> Persisted(string kind) => kind == "scan" ? Saved.ScanWhitelist : Saved.CleanupRecommendationWhitelist;
        public void Dispose() { Vm.HideSaveToast(); _clients.Dispose(); }
    }
}
