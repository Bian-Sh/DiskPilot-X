using System.Text.Json;
using System.Text.Json.Nodes;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Services.Assistant;
using FluentAssertions;
using TinyHarness;
using Message = DiskPilotX.Core.Models.ChatMessage;

namespace DiskPilotX.Core.Tests;

public sealed class ConversationMetadataTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DiskPilot-X-元数据测试-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 保存后元数据只含最小字段且不影响历史枚举()
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);

        var source = new FileInfo(SourcePath(snapshot.Id));
        var metadata = store.ReadMetadata(snapshot.Id);
        metadata.Should().Be(new ConversationMetadata(snapshot.Id, "测试隐私签名", source.Length, source.LastWriteTimeUtc));
        metadata!.SourceWriteTimeUtc.Kind.Should().Be(DateTimeKind.Utc);
        using var document = JsonDocument.Parse(File.ReadAllText(MetadataPath(snapshot.Id)));
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("Id", "PrivacySignature", "SourceLength", "SourceWriteTimeUtc");
        store.List().Select(summary => summary.Id).Should().Equal(snapshot.Id);
        Directory.GetFiles(directory, "*.json").Should().ContainSingle();
        Directory.GetFiles(directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void 空隐私签名可以往返且覆盖保存更新元数据()
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        store.Save(snapshot with { PrivacySignature = null });

        store.ReadMetadata(snapshot.Id)!.PrivacySignature.Should().BeNull();
        store.Load(snapshot.Id).PrivacySignature.Should().BeNull();
        Directory.GetFiles(directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void 旧快照缺元数据时不打开正文也不自动迁移()
    {
        var snapshot = Snapshot();
        Directory.CreateDirectory(directory);
        File.WriteAllText(SourcePath(snapshot.Id), JsonSerializer.Serialize(snapshot));
        using var lockedSource = File.Open(SourcePath(snapshot.Id), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        new ConversationStore(directory).ReadMetadata(snapshot.Id).Should().BeNull();
        File.Exists(MetadataPath(snapshot.Id)).Should().BeFalse();
    }

    [Fact]
    public void 有效元数据预审不打开被独占锁定的正文()
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        using var lockedSource = File.Open(SourcePath(snapshot.Id), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        store.ReadMetadata(snapshot.Id)!.PrivacySignature.Should().Be("测试隐私签名");
    }

    [Fact]
    public void 目录缺失时返回空且不创建目录()
    {
        new ConversationStore(directory).ReadMetadata(Guid.NewGuid().ToString("N")).Should().BeNull();
        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public void 源文件删除时拒绝残留元数据()
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        File.Delete(SourcePath(snapshot.Id));

        store.ReadMetadata(snapshot.Id).Should().BeNull();
    }

    [Fact]
    public void 源长度改变即使修改时间未变也拒绝元数据()
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        var writeTime = File.GetLastWriteTimeUtc(SourcePath(snapshot.Id));
        File.AppendAllText(SourcePath(snapshot.Id), " ");
        File.SetLastWriteTimeUtc(SourcePath(snapshot.Id), writeTime);

        store.ReadMetadata(snapshot.Id).Should().BeNull();
    }

    [Fact]
    public void 源修改时间改变即使长度未变也拒绝元数据()
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        File.SetLastWriteTimeUtc(SourcePath(snapshot.Id), File.GetLastWriteTimeUtc(SourcePath(snapshot.Id)).AddMinutes(-1));

        store.ReadMetadata(snapshot.Id).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"Id\":123}")]
    public void 损坏元数据不回退读取正文(string content)
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        File.WriteAllText(MetadataPath(snapshot.Id), content);
        using var lockedSource = File.Open(SourcePath(snapshot.Id), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        store.ReadMetadata(snapshot.Id).Should().BeNull();
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("PrivacySignature")]
    [InlineData("SourceLength")]
    [InlineData("SourceWriteTimeUtc")]
    public void 元数据缺少任一字段均拒绝(string missingProperty)
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        var metadata = JsonNode.Parse(File.ReadAllText(MetadataPath(snapshot.Id)))!.AsObject();
        metadata.Remove(missingProperty);
        File.WriteAllText(MetadataPath(snapshot.Id), metadata.ToJsonString());

        store.ReadMetadata(snapshot.Id).Should().BeNull();
    }

    [Fact]
    public void 拒绝属于其他会话的元数据()
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        var metadata = store.ReadMetadata(snapshot.Id)! with { Id = Guid.NewGuid().ToString("N") };
        File.WriteAllText(MetadataPath(snapshot.Id), JsonSerializer.Serialize(metadata));

        store.ReadMetadata(snapshot.Id).Should().BeNull();
    }

    [Fact]
    public void 元数据文件不可读时返回空()
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        using var lockedMetadata = File.Open(MetadataPath(snapshot.Id), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        store.ReadMetadata(snapshot.Id).Should().BeNull();
    }

    [Fact]
    public void 元数据保存失败不影响原对话保存也不留下可读元数据()
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        Directory.CreateDirectory(MetadataPath(snapshot.Id));

        store.Save(snapshot);

        store.Load(snapshot.Id).Messages.Single().Text.Should().Be("用户正文不得预读");
        store.ReadMetadata(snapshot.Id).Should().BeNull();
        Directory.GetFiles(directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void 覆盖保存元数据失败时旧元数据不能通过核验()
    {
        var snapshot = Snapshot();
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        using (File.Open(MetadataPath(snapshot.Id), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            store.Save(snapshot with { PrivacySignature = "更长的新测试隐私签名" });

        store.Load(snapshot.Id).PrivacySignature.Should().Be("更长的新测试隐私签名");
        store.ReadMetadata(snapshot.Id).Should().BeNull();
        Directory.GetFiles(directory, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("../其他目录")]
    [InlineData("..\\其他目录")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    [InlineData(null)]
    public void 元数据路径仍严格校验会话标识(string? id)
    {
        var read = () => new ConversationStore(directory).ReadMetadata(id!);
        read.Should().Throw<InvalidDataException>();
        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public void 空对话不创建元数据()
    {
        var snapshot = Snapshot() with { Messages = [] };
        new ConversationStore(directory).Save(snapshot);
        Directory.Exists(directory).Should().BeFalse();
    }

    private string SourcePath(string id) => Path.Combine(directory, id + ".json");
    private string MetadataPath(string id) => Path.Combine(directory, id + ".metadata");

    private static ConversationSnapshot Snapshot() => new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
        "测试隐私签名", AgentSessionState.Running, [TinyHarness.ChatMessage.User("模型正文不得预读")],
        [SavedChatMessage.Capture(new Message { Sender = ChatSender.User, Text = "用户正文不得预读", Thinking = "思考正文不得预读" })]);

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
