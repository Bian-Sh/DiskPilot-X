using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Views;

/// <summary>折叠时直接替换最新增量，不创建滚动动画或预览定时器。</summary>
public sealed class ThinkingPreviewText : Border
{
    public static readonly DependencyProperty SegmentProperty = DependencyProperty.Register(nameof(Segment), typeof(ChatSegment),
        typeof(ThinkingPreviewText), new PropertyMetadata(null, SegmentChanged));
    public ChatSegment? Segment { get => (ChatSegment?)GetValue(SegmentProperty); set => SetValue(SegmentProperty, value); }
    private readonly TextBlock text = new() { FontSize = 12, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private string previous = string.Empty;

    public ThinkingPreviewText()
    {
        MaxWidth = 180; Height = 18; HorizontalAlignment = HorizontalAlignment.Left;
        text.SetResourceReference(TextBlock.ForegroundProperty, "VP.TextTertiaryBrush");
        Child = text;
        Loaded += (_, _) => { if (Segment is { } segment) segment.PropertyChanged += Changed; UpdateDelta(); RefreshState(); };
        Unloaded += (_, _) => { if (Segment is { } segment) segment.PropertyChanged -= Changed; };
    }

    private static void SegmentChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var view = (ThinkingPreviewText)owner;
        if (view.IsLoaded)
        {
            if (args.OldValue is ChatSegment old) old.PropertyChanged -= view.Changed;
            if (args.NewValue is ChatSegment current) current.PropertyChanged += view.Changed;
        }
        view.previous = string.Empty; view.UpdateDelta(); view.RefreshState();
    }

    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ChatSegment.Text)) UpdateDelta();
        if (args.PropertyName is nameof(ChatSegment.IsExpanded) or nameof(ChatSegment.Status) or nameof(ChatSegment.Text)) RefreshState();
    }

    private void RefreshState() => Visibility = IsLoaded && Segment is { IsRunning: true, IsExpanded: false, HasText: true }
        ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateDelta()
    {
        var content = Segment?.Text ?? string.Empty;
        if (ReferenceEquals(content, previous)) return;
        var start = content.StartsWith(previous, StringComparison.Ordinal) ? previous.Length : 0;
        if (start > 0 && start < content.Length && char.IsLowSurrogate(content[start]) && char.IsHighSurrogate(content[start - 1])) start--;
        start = Math.Max(start, content.Length - 160);
        if (start > 0 && start < content.Length && char.IsLowSurrogate(content[start]) && char.IsHighSurrogate(content[start - 1])) start++;
        text.Text = content[start..].Replace('\r', ' ').Replace('\n', ' ');
        previous = content;
    }
}

/// <summary>有限视口内回收可见文本片段；原始全文保留于模型，不对正文做长度截断。</summary>
public sealed class ThinkingVirtualizedView : ListBox
{
    public static readonly DependencyProperty SegmentProperty = DependencyProperty.Register(nameof(Segment), typeof(ChatSegment),
        typeof(ThinkingVirtualizedView), new PropertyMetadata(null, SegmentChanged));
    public ChatSegment? Segment { get => (ChatSegment?)GetValue(SegmentProperty); set => SetValue(SegmentProperty, value); }
    private readonly ObservableCollection<ThinkingLine> lines = [];
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
    private string previous = string.Empty;
    private bool followTail = true;
    private bool tailQueued;
    private double readingOffset;
    private ScrollViewer? Scroll => GetTemplateChild("PART_ScrollViewer") as ScrollViewer;

    public ThinkingVirtualizedView()
    {
        MaxHeight = 200; BorderThickness = new Thickness(0); Padding = new Thickness(0); Background = Brushes.Transparent;
        Focusable = false; IsTabStop = false;
        SetValue(ScrollViewer.CanContentScrollProperty, true);
        SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        SetValue(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel);
        SetValue(VirtualizingPanel.CacheLengthProperty, new VirtualizationCacheLength(2));
        SetValue(VirtualizingPanel.CacheLengthUnitProperty, VirtualizationCacheLengthUnit.Item);
        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        // 显式持有唯一滚动宿主，避免外层 ScrollViewer 无限测量造成假虚拟化。
        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ScrollViewer");
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.AppendChild(presenter);
        Template = new ControlTemplate(typeof(ListBox)) { VisualTree = scroll };
        var item = new FrameworkElementFactory(typeof(TextBlock));
        item.SetBinding(TextBlock.TextProperty, new Binding(nameof(ThinkingLine.DisplayText)));
        item.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        item.SetValue(TextBlock.FontSizeProperty, 12d);
        item.SetValue(TextBlock.MinHeightProperty, 18d);
        item.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
        item.SetResourceReference(TextBlock.ForegroundProperty, "VP.TextSecondaryBrush");
        ItemTemplate = new DataTemplate { VisualTree = item };
        ItemContainerStyle = new Style(typeof(ListBoxItem));
        ItemContainerStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        ItemContainerStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        ItemContainerStyle.Setters.Add(new Setter(IsHitTestVisibleProperty, false));
        Loaded += (_, _) => { if (Segment is { } segment) segment.PropertyChanged += Changed; RefreshState(); };
        Unloaded += (_, _) => { if (Segment is { } segment) segment.PropertyChanged -= Changed; timer.Stop(); ItemsSource = null; };
        IsVisibleChanged += (_, _) => RefreshState();
        timer.Tick += (_, _) => { timer.Stop(); RefreshContent(); };
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
        // 内层尾随只滚动自己的视口，禁止 BringIntoView 将外层聊天重新拉回底部。
        AddHandler(RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler((_, args) => args.Handled = true));
    }

    private static void SegmentChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var view = (ThinkingVirtualizedView)owner;
        if (view.IsLoaded)
        {
            if (args.OldValue is ChatSegment old) old.PropertyChanged -= view.Changed;
            if (args.NewValue is ChatSegment current) current.PropertyChanged += view.Changed;
        }
        view.previous = string.Empty; view.lines.Clear(); view.followTail = true; view.readingOffset = 0;
        view.RefreshState();
    }

    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ChatSegment.IsExpanded) or nameof(ChatSegment.Status)) RefreshState();
        else if (args.PropertyName == nameof(ChatSegment.Text) && IsActive) timer.Start();
    }

    private bool IsActive => IsLoaded && IsVisible && Segment is { IsExpanded: true };
    private void RefreshState()
    {
        if (!IsActive) { timer.Stop(); ItemsSource = null; return; }
        var restore = ItemsSource is null;
        var savedOffset = readingOffset;
        ItemsSource = lines;
        RefreshContent();
        if (restore && !followTail) _ = Dispatcher.InvokeAsync(() => { if (IsActive) Scroll?.ScrollToVerticalOffset(savedOffset); }, DispatcherPriority.Loaded);
    }

    private void RefreshContent()
    {
        if (!IsActive) return;
        var content = Segment!.Text;
        if (ReferenceEquals(content, previous)) return;
        var start = previous.Length;
        if (!content.StartsWith(previous, StringComparison.Ordinal)) { lines.Clear(); start = 0; }
        else if (lines.Count > 0) start -= lines[^1].Text.Length;
        var replaceTail = lines.Count > 0;
        while (start < content.Length)
        {
            var end = Math.Min(start + 256, content.Length);
            var newline = content.IndexOf('\n', start, end - start);
            if (newline >= 0) end = newline + 1;
            else if (end < content.Length && (char.IsHighSurrogate(content[end - 1]) && char.IsLowSurrogate(content[end])
                || content[end - 1] == '\r' && content[end] == '\n')) end++;
            var part = content[start..end];
            if (replaceTail) { lines[^1].Text = part; replaceTail = false; }
            else lines.Add(new ThinkingLine(part));
            start = end;
        }
        previous = content;
        if (followTail) QueueTail();
    }

    private void QueueTail()
    {
        if (tailQueued || lines.Count == 0) return;
        tailQueued = true;
        _ = Dispatcher.InvokeAsync(() =>
        {
            tailQueued = false;
            if (IsActive && followTail && lines.Count > 0) { ScrollIntoView(lines[^1]); Scroll?.ScrollToEnd(); }
        }, DispatcherPriority.Loaded);
    }

    private void Scrolled(object sender, ScrollChangedEventArgs args)
    {
        if (Scroll is not { } scroll || !ReferenceEquals(args.OriginalSource, scroll) || !IsActive) return;
        if (args.ExtentHeightChange != 0 || args.ViewportHeightChange != 0) { if (followTail) QueueTail(); }
        else if (args.VerticalChange != 0) followTail = scroll.ScrollableHeight - scroll.VerticalOffset < 16;
        if (!followTail) readingOffset = scroll.VerticalOffset;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs args)
    {
        if (Scroll is { } scroll && (args.Delta > 0 ? scroll.VerticalOffset > 0 : scroll.VerticalOffset < scroll.ScrollableHeight))
        {
            followTail = args.Delta < 0 && scroll.ScrollableHeight - (scroll.VerticalOffset - args.Delta) < 16;
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset - args.Delta); args.Handled = true; return;
        }
        // 到达内层边界后将滚轮交给外层消息区，不争抢阅读位置。
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer outer) { outer.ScrollToVerticalOffset(outer.VerticalOffset - args.Delta); args.Handled = true; return; }
        base.OnPreviewMouseWheel(args);
    }

    private sealed class ThinkingLine(string text) : INotifyPropertyChanged
    {
        private string content = text;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Text
        {
            get => content;
            set
            {
                if (content == value) return;
                content = value;
                PropertyChanged?.Invoke(this, new(nameof(DisplayText)));
            }
        }
        public string DisplayText => content.TrimEnd('\r', '\n');
        public override string ToString() => content;
    }
}
