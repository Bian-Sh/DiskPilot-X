using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace DiskPilotX.App.Logging;

public sealed class UiLogSink : ILogEventSink, IDisposable
{
    public const int MaxEntries = 5_000;
    public const int MaxTextCharacters = 4 * 1024 * 1024;
    public const int MaxBatchEntries = 128;
    public const int BatchBudgetMilliseconds = 8;
    private const int PreviewCharacters = 1_024;
    private readonly object _gate = new();
    private readonly Queue<UiLogEntry> _history = new();
    private readonly ObservableCollection<UiLogEntry> _entries = new();
    private readonly Timer _timer;
    private Dispatcher? _dispatcher;
    private long _version, _displayVersion, _clearVersion;
    private int _textCharacters;
    private bool _active, _scheduled, _disposed;
    public ObservableCollection<UiLogEntry> Entries => _entries;
    public event Action? EntryAdded;
    public event Action? BatchUpdating;
    public (int Operations, double DurationMilliseconds, int MaximumOperations, double MaximumDurationMilliseconds) BatchMetrics { get; private set; }

    public UiLogSink() => _timer = new Timer(PostFlush, null, Timeout.Infinite, Timeout.Infinite);

    public void Emit(LogEvent logEvent)
    {
        var entry = new UiLogEntry(
            logEvent.Timestamp,
            logEvent.Level,
            Preview(LogTsvFormatter.RenderMessage(logEvent)),
            logEvent.Exception is null ? null : Preview(logEvent.Exception.ToString()),
            Preview(logEvent.Properties.TryGetValue("SourceContext", out var source) && source is ScalarValue { Value: string category } ? category : "Runtime"));
        lock (_gate)
        {
            if (_disposed) return;
            _history.Enqueue(entry with { Sequence = ++_version });
            _textCharacters += TextSize(entry);
            while (_history.Count > MaxEntries || _textCharacters > MaxTextCharacters)
                _textCharacters -= TextSize(_history.Dequeue());
            Schedule();
        }
    }

    public void SetActive(bool active)
    {
        _dispatcher ??= Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _dispatcher.VerifyAccess();
        lock (_gate)
        {
            if (_disposed) return;
            _active = active;
            if (!active) { _scheduled = false; _timer.Change(Timeout.Infinite, Timeout.Infinite); }
        }
        if (active) Flush();
    }

    public void Flush()
    {
        _dispatcher?.VerifyAccess();
        var watch = Stopwatch.StartNew();
        UiLogEntry[] snapshot;
        long version, clearVersion;
        lock (_gate)
        {
            _scheduled = false;
            if (_disposed || !_active || _displayVersion == _version) return;
            snapshot = _history.ToArray();
            version = _version;
            clearVersion = _clearVersion;
        }
        // 通知和筛选不持采集锁；计时包含同步通知，达到任一预算就让出 Dispatcher。
        BatchUpdating?.Invoke();
        var operations = 0;
        bool CanContinue() => operations < MaxBatchEntries && (operations == 0 || watch.Elapsed.TotalMilliseconds < BatchBudgetMilliseconds)
            && clearVersion == _clearVersion && _active && !_disposed;
        var firstSequence = snapshot.Length == 0 ? long.MaxValue : snapshot[0].Sequence;
        while (_entries.Count > 0 && _entries[0].Sequence < firstSequence && CanContinue())
        {
            _entries.RemoveAt(0);
            operations++;
        }
        var index = 0;
        var obsoleteRemain = _entries.Count > 0 && _entries[0].Sequence < firstSequence;
        if (!obsoleteRemain)
        {
            var lastSequence = _entries.Count == 0 ? 0 : _entries[^1].Sequence;
            // 有界快照按 Sequence 排序，二分跳过已显示部分，避免逐批重扫全部历史。
            var upper = snapshot.Length;
            while (index < upper)
            {
                var middle = index + (upper - index) / 2;
                if (snapshot[middle].Sequence <= lastSequence) index = middle + 1;
                else upper = middle;
            }
            while (index < snapshot.Length && CanContinue())
            {
                _entries.Add(snapshot[index++]);
                operations++;
            }
        }
        EntryAdded?.Invoke();
        BatchMetrics = (operations, watch.Elapsed.TotalMilliseconds, Math.Max(BatchMetrics.MaximumOperations, operations),
            Math.Max(BatchMetrics.MaximumDurationMilliseconds, watch.Elapsed.TotalMilliseconds));
        lock (_gate)
        {
            if (_disposed || !_active || clearVersion != _clearVersion) return;
            var remaining = obsoleteRemain || index < snapshot.Length;
            if (!remaining) _displayVersion = version;
            if (remaining) { _scheduled = true; _timer.Change(1, Timeout.Infinite); }
            else if (_displayVersion != _version) Schedule();
            else { _scheduled = false; _timer.Change(Timeout.Infinite, Timeout.Infinite); }
        }
    }

    public void Clear()
    {
        var dispatcher = _dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(Clear); return; }
        lock (_gate)
        {
            if (_disposed) return;
            _history.Clear();
            _textCharacters = 0;
            _clearVersion = _displayVersion = ++_version;
            _scheduled = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        BatchUpdating?.Invoke();
        _entries.Clear();
        EntryAdded?.Invoke();
    }

    public (int Count, int TextCharacters, long Version, bool Active, bool Scheduled) State
    {
        get { lock (_gate) return (_history.Count, _textCharacters, _version, _active, _scheduled); }
    }

    private void Schedule()
    {
        if (!_active || _scheduled) return;
        _scheduled = true;
        _timer.Change(200, Timeout.Infinite);
    }

    private void PostFlush(object? state)
    {
        lock (_gate)
        {
            if (_disposed || !_active) { _scheduled = false; return; }
            if (_dispatcher is null || _dispatcher.HasShutdownStarted) { _scheduled = false; return; }
            _dispatcher.BeginInvoke(DispatcherPriority.Background, Flush);
        }
    }

    private static int TextSize(UiLogEntry entry) => entry.Message.Length + (entry.Exception?.Length ?? 0) + entry.SourceContext.Length;
    private static string Preview(string text) => text.Length <= PreviewCharacters ? text : text[..PreviewCharacters] + " [完整内容见日志文件]";

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _active = false; _scheduled = false; _timer.Dispose(); _history.Clear(); _textCharacters = 0; }
    }

}
