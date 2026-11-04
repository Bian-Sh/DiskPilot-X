using System.ComponentModel;
using System.Globalization;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using TinyHarness;
using Message = DiskPilotX.Core.Models.ChatMessage;

namespace DiskPilotX.Core.Tests;

public sealed class ChatUsageCardTests
{
    private static IReadOnlyList<KeyValuePair<string,string>> Rows(Message message)
    {
        var property = typeof(Message).GetProperty("UsageTooltipDetails");
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string,string>>>(property.GetValue(message));
    }
    private static string Total(Message message)
    {
        var property = typeof(Message).GetProperty("UsageTotalText");
        Assert.NotNull(property);
        return Assert.IsType<string>(property.GetValue(message));
    }

    [Fact]
    public void 示例精确总量与四行顺序复刻且历史恢复不变()
    {
        var message = new Message { Usage = new(2717274, 8713, 2725987, 2047887, TotalInputTokens: 2717274) };
        Assert.True(message.HasUsage);
        Assert.Equal("2,725,987 tok", Total(message));
        var rows = Rows(message);
        Assert.Equal(new[] { "缓存命中", "未缓存输入", "缓存读取", "输出" }, rows.Select(row => row.Key));
        Assert.Equal(new[] { "75%", "669,387 tok", "2,047,887 tok", "8,713 tok" }, rows.Select(row => row.Value));
        var restored = SavedChatMessage.Capture(message).Restore();
        Assert.Equal(Total(message), Total(restored));
        Assert.Equal(rows, Rows(restored));
    }

    [Fact]
    public void Anthropic写入不当成读取命中也不重复累计输入()
    {
        var message = new Message { Usage = new(40, 10, 270, 200, 20, TotalInputTokens: 260) };
        Assert.Equal(new[] { "77%", "60 tok", "200 tok", "10 tok" }, Rows(message).Select(row => row.Value));
        Assert.Equal("270 tok", Total(message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    [InlineData(101L)]
    public void 缓存缺失或无效时不伪造未缓存拆分(long? cached)
    {
        var message = new Message { Usage = new(100, 20, 120, cached, TotalInputTokens: 100) };
        var rows = Rows(message);
        Assert.Equal("未提供", rows[0].Value);
        Assert.Equal("未提供", rows[1].Value);
    }

    [Fact]
    public void 明确零缓存显示零而部分请求缺少统计不能当零()
    {
        var message = new Message { Usage = new(100, 20, 120, 0, TotalInputTokens: 100) };
        Assert.Equal(new[] { "0%", "100 tok", "0 tok", "20 tok" }, Rows(message).Select(row => row.Value));
        message.HasIncompleteCacheUsage = true;
        Assert.Equal("未提供", Rows(message)[0].Value);
        Assert.Equal("未提供", Rows(message)[1].Value);
    }

    [Fact]
    public void 单步异常缓存不能在累计后变成可信的缓存读取量()
    {
        var message = new Message();
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.MessageCompleted) with
        { Message = TinyHarness.ChatMessage.Assistant("第一步") with { Usage = new(100, 20, 120, 120, TotalInputTokens: 100) } });
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.MessageCompleted) with
        { Message = TinyHarness.ChatMessage.Assistant("第二步") with { Usage = new(900, 20, 920, 0, TotalInputTokens: 900) } });
        Assert.True(message.HasIncompleteCacheUsage);
        Assert.Equal("未提供", Rows(message)[2].Value);
        Assert.Equal(120, message.Usage!.CachedInputTokens);
        Assert.Equal("未提供", Rows(SavedChatMessage.Capture(message).Restore())[2].Value);
    }
    [Fact]
    public void 没有统计不伪造胶囊并且增量统计通知卡片()
    {
        var message = new Message();
        Assert.False(message.HasUsage);
        Assert.Empty(Rows(message));
        var changed = new List<string?>();
        message.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        message.Usage = new(100, 20, 120, 0, TotalInputTokens: 100);
        Assert.Contains("UsageTooltipDetails", changed);
        Assert.Contains("UsageTotalText", changed);
        Assert.True(message.HasUsage);
    }
}