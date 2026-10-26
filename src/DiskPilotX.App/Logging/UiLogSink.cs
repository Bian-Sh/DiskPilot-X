using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Serilog.Core;
using Serilog.Events;

namespace DiskPilotX.App.Logging;

public sealed class UiLogSink : ILogEventSink
{
    private const int MaxEntries = 2_000;

    public ObservableCollection<UiLogEntry> Entries { get; } = new();

    public event Action? EntryAdded;

    public void Emit(LogEvent logEvent)
    {
        var entry = new UiLogEntry(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.RenderMessage(CultureInfo.CurrentCulture),
            logEvent.Exception?.ToString());

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Add(entry);
            return;
        }

        dispatcher.BeginInvoke(() => Add(entry));
    }

    public void Clear()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Entries.Clear();
            return;
        }

        dispatcher.BeginInvoke(Entries.Clear);
    }

    private void Add(UiLogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }

        EntryAdded?.Invoke();
    }
}
