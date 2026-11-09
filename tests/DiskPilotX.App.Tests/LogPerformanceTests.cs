using System.Collections.Specialized;
using System.Windows;
using DiskPilotX.App.Logging;
using Serilog;
using System.Windows.Threading;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class LogPerformanceTests
{
    [Fact]
    public void 持续并发生产时分批让出界面并最终追上最新日志() => StaHost.Run(() =>
    {
        using var sink = new UiLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        sink.SetActive(true);
        var producer = Task.Run(() => Parallel.For(0, 100_000, index => logger.Information("持续日志 {Index}", index)));
        var ticks = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Input, (_, _) =>
        {
            ticks++;
            Assert.InRange(sink.Entries.Count, 0, 5000);
            Assert.InRange(sink.State.TextCharacters, 0, 4 * 1024 * 1024);
            if ((producer.IsCompleted && !sink.State.Scheduled) || watch.Elapsed > TimeSpan.FromSeconds(15)) frame.Continue = false;
        }, Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(producer.IsCompletedSuccessfully);
        Assert.False(sink.State.Scheduled);
        Assert.True(ticks > 1);
        Assert.Equal(5000, sink.Entries.Count);
        Assert.Equal(100_000, sink.Entries[^1].Sequence);
        Assert.InRange(sink.BatchMetrics.MaximumOperations, 1, 128);
    });

    [Fact]
    public void 首次显示分批让出且清空不会复活未完成快照() => StaHost.Run(() =>
    {
        using var sink = new UiLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        for (var index = 0; index < 5000; index++) logger.Information("预算日志 {Index}", index);
        sink.SetActive(true);
        Assert.InRange(sink.Entries.Count, 1, 128);
        Assert.True(sink.State.Scheduled);
        sink.Clear();
        logger.Information("清空后唯一日志");
        sink.Flush();
        Assert.Equal("清空后唯一日志", Assert.Single(sink.Entries).Message);
        Assert.False(sink.State.Scheduled);
    });

    [Fact]
    public void 同步视图通知耗尽时间预算后停止当前批次() => StaHost.Run(() =>
    {
        using var sink = new UiLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        for (var index = 0; index < 100; index++) logger.Information("慢视图 {Index}", index);
        sink.Entries.CollectionChanged += (_, _) => Thread.Sleep(20);
        sink.SetActive(true);
        Assert.Single(sink.Entries);
        Assert.True(sink.State.Scheduled);
        sink.SetActive(false);
    });

    [Fact]
    public void 视图同步通知不持有后台采集锁() => StaHost.Run(() =>
    {
        using var sink = new UiLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        logger.Information("初始日志");
        using var produced = new ManualResetEventSlim();
        var queued = false;
        sink.Entries.CollectionChanged += (_, _) =>
        {
            if (!queued)
            {
                queued = true;
                ThreadPool.QueueUserWorkItem(_ => { logger.Information("通知期间的后台日志"); produced.Set(); });
            }
            Assert.True(produced.Wait(TimeSpan.FromSeconds(2)), "同步视图通知不能阻塞后台采集锁");
        };
        sink.SetActive(true);
        sink.Flush();
        Assert.Contains(sink.Entries, entry => entry.Message == "通知期间的后台日志");
    });

    [Fact]
    public void 隐藏控制台的十万条日志不会逐条通知界面() => StaHost.Run(() =>
    {
        var sink = new UiLogSink();
        var notifications = 0;
        sink.Entries.CollectionChanged += (_, _) => notifications++;
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        for (var index = 0; index < 100_000; index++) logger.Information("日志 {Index}", index);
        Assert.Equal(0, notifications);
        Assert.Empty(sink.Entries);
        (sink as object as IDisposable)?.Dispose();
    });

    [Fact]
    public void 普通日志文件使用表格软件识别的分列格式()
    {
        Assert.EndsWith(".tsv", AppLog.FilePathPattern);
    }

    [Fact]
    public void 激活后有预算合并且空闲不产生集合通知() => StaHost.Run(() =>
    {
        using var sink = new UiLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        for (var i = 0; i < 6000; i++) logger.Information("日志 {Index}", i);
        var notifications = 0;
        sink.Entries.CollectionChanged += (_, _) => notifications++;
        sink.SetActive(true);
        for (var batch = 0; sink.State.Scheduled && batch < 100; batch++) sink.Flush();
        Assert.False(sink.State.Scheduled);
        Assert.InRange(sink.BatchMetrics.MaximumOperations, 1, 128);
        Assert.Equal(5000, sink.Entries.Count);
        Assert.Equal("日志 1000", sink.Entries[0].Message);
        Assert.Equal("日志 5999", sink.Entries[^1].Message);
        var settledNotifications = notifications;
        sink.Flush(); sink.Flush();
        Assert.Equal(settledNotifications, notifications);
        Assert.False(sink.State.Scheduled);
    });

    [Fact]
    public void 超长及并发日志受文本容量限制且清空覆盖待处理数据() => StaHost.Run(() =>
    {
        using var sink = new UiLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        sink.SetActive(true);
        var message = new string('测', 100_000);
        Parallel.For(0, 6000, _ => logger.Error(new InvalidOperationException(message), "{Message:l}", message));
        Assert.InRange(sink.State.TextCharacters, 1, UiLogSink.MaxTextCharacters);
        Assert.InRange(sink.State.Count, 1, UiLogSink.MaxEntries);
        sink.Clear();
        logger.Information("清空后新日志");
        sink.Flush();
        Assert.Equal("清空后新日志", Assert.Single(sink.Entries).Message);
        sink.SetActive(false);
        logger.Information("隐藏后缓存");
        sink.Flush();
        Assert.Single(sink.Entries);
        sink.SetActive(true);
        Assert.Equal(2, sink.Entries.Count);
    });
}
