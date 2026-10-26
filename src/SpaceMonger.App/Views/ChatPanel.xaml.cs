using SpaceMonger.App.Localization;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SpaceMonger.App.Converters;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;

namespace SpaceMonger.App.Views;

public partial class ChatPanel : UserControl
{
    private ChatViewModel? viewModel;
    private readonly HashSet<ChatMessage> observedMessages = [];
    private bool followTail = true;
    private bool holdReading;
    private bool scrollQueued;
    public event Action? OpenSettingsRequested;
    public ChatPanel() { InitializeComponent(); if(App.Services?.GetService(typeof(SpaceMonger.App.Services.ThemeManager)) is SpaceMonger.App.Services.ThemeManager manager) manager.ThemeChanged += RefreshMarkdownViews; L.LanguageChanged += ()=> { _=Dispatcher.InvokeAsync(()=> { var offset=MessagesScrollViewer.VerticalOffset; MessagesItemsControl.Items.Refresh(); if(!followTail)MessagesScrollViewer.ScrollToVerticalOffset(offset); }); }; }
    private void RefreshMarkdownViews()
    {
        var pending=new Stack<DependencyObject>();pending.Push(this);
        while(pending.TryPop(out var node))
        {
            if(node is CopilotMarkdownView markdown){if(markdown.IsVisible)markdown.RefreshDocument();continue;}
            for(var index=0;index<VisualTreeHelper.GetChildrenCount(node);index++)pending.Push(VisualTreeHelper.GetChild(node,index));
        }
    }
    public void SetViewModel(ChatViewModel vm)
    {
        if(viewModel is not null) viewModel.Messages.CollectionChanged -= MessagesChanged;
        foreach(var message in observedMessages.ToArray()) Detach(message);
        viewModel=vm; DataContext=vm; vm.RefreshSkills(); vm.Messages.CollectionChanged += MessagesChanged;
        foreach(var message in vm.Messages) Attach(message);
        QueueScroll();
    }
    private void History_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && viewModel is not null) { viewModel.IsHistoryOpen = false; e.Handled = true; }
    }
    public void FocusInput() { InputTextBox.Focus(); InputTextBox.CaretIndex=InputTextBox.Text.Length; }
    private void Attach(ChatMessage message)
    {
        if(!observedMessages.Add(message)) return;
        message.PropertyChanged += MessageChanged; message.Segments.CollectionChanged += SegmentsChanged;
        foreach(var segment in message.Segments) segment.PropertyChanged += MessageChanged;
    }
    private void Detach(ChatMessage message)
    {
        message.PropertyChanged -= MessageChanged; message.Segments.CollectionChanged -= SegmentsChanged;
        foreach(var segment in message.Segments) segment.PropertyChanged -= MessageChanged;
        observedMessages.Remove(message);
    }
    private void MessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if(viewModel is null) return;
        foreach(var message in observedMessages.Where(m=>!viewModel.Messages.Contains(m)).ToArray()) Detach(message);
        foreach(var message in viewModel.Messages) Attach(message);
        QueueScroll();
    }
    private void SegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if(e.NewItems is not null) foreach(ChatSegment segment in e.NewItems) segment.PropertyChanged += MessageChanged;
        if(e.OldItems is not null) foreach(ChatSegment segment in e.OldItems) segment.PropertyChanged -= MessageChanged;
        QueueScroll();
    }
    private void MessageChanged(object? sender, PropertyChangedEventArgs e) => QueueScroll();
    private bool IsNearLatest => MessagesScrollViewer.ScrollableHeight - MessagesScrollViewer.VerticalOffset < 24;
    private void UpdateJumpButton() => JumpButton.Visibility = !followTail && !IsNearLatest && viewModel?.HasMessages == true
        ? Visibility.Visible : Visibility.Collapsed;
    private void QueueScroll()
    {
        if(scrollQueued) return; scrollQueued=true;
        _=Dispatcher.InvokeAsync(() =>
        {
            scrollQueued=false;
            // 布局稳定后重新核对位置；展开或折叠详情不等于离开最新消息。
            if(IsNearLatest) followTail=true;
            if(followTail) MessagesScrollViewer.ScrollToEnd();
            UpdateJumpButton();
        }, DispatcherPriority.Background);
    }
    private void Messages_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if(!ReferenceEquals(e.OriginalSource, MessagesScrollViewer)) return;
        if(IsNearLatest || e.ExtentHeightChange==0 && e.ViewportHeightChange==0) followTail=IsNearLatest;
        else if(followTail) QueueScroll();
        UpdateJumpButton();
    }
    private void Process_ExpansionChanged(object sender,RoutedEventArgs e) { var offset=MessagesScrollViewer.VerticalOffset;holdReading=true;followTail=false;QueueScroll();_=Dispatcher.InvokeAsync(()=>MessagesScrollViewer.ScrollToVerticalOffset(offset),DispatcherPriority.Loaded); }
    private void Messages_PreviewMouseDown(object sender,MouseButtonEventArgs e)=>holdReading=false;
    private void Messages_PreviewMouseWheel(object sender,MouseWheelEventArgs e)=>holdReading=false;
    private void Submit_Click(object sender,RoutedEventArgs e) { if(viewModel?.IsOperationRunning!=true){holdReading=false;followTail=true;QueueScroll();} }
    private void Jump_Click(object sender,RoutedEventArgs e) { holdReading=false; followTail=true; MessagesScrollViewer.ScrollToEnd(); JumpButton.Visibility=Visibility.Collapsed; }
    private void Settings_Click(object sender,RoutedEventArgs e) => OpenSettingsRequested?.Invoke();
    private void OpenSkillTask_Click(object sender,RoutedEventArgs e) { if(sender is FrameworkElement { DataContext: SkillTaskCard card }) viewModel?.OpenSkillTask(card); }
    private void Skills_Click(object sender,RoutedEventArgs e) { if(viewModel is null)return; viewModel.InputText="@"; FocusInput(); }
    private void Skill_Click(object sender,MouseButtonEventArgs e) { if(SkillList.SelectedItem is ChatSkillSuggestion skill) viewModel?.SelectSkillMentionCommand.Execute(skill); FocusInput(); }
    private void Slash_Click(object sender,MouseButtonEventArgs e) { if(SlashList.SelectedItem is ChatCommandSuggestion command) viewModel?.SelectSlashCommandCommand.Execute(command); FocusInput(); }
    private void Input_KeyDown(object sender,KeyEventArgs e)
    {
        if(viewModel is null) return;
        if(viewModel.IsCompletionMenuOpen && e.Key is Key.Up or Key.Down) { viewModel.MoveCompletionSelection(e.Key==Key.Up?-1:1); e.Handled=true; return; }
        if(e.Key is Key.Enter or Key.Tab && viewModel.ConfirmActiveCompletion()) { e.Handled=true; return; }
        if(e.Key==Key.Escape) { viewModel.IsSlashCommandMenuOpen=false; viewModel.IsSkillMentionMenuOpen=false; return; }
        if(e.Key==Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && !viewModel.IsOperationRunning) { holdReading=false; followTail=true; QueueScroll(); viewModel.SubmitOrStopCommand.Execute(null); e.Handled=true; }
    }
    private void Copy_Click(object sender,RoutedEventArgs e)
    {
        if(sender is FrameworkElement {DataContext:ChatMessage message} && !string.IsNullOrWhiteSpace(message.Text))
            try { Clipboard.SetText(message.Text); } catch(System.Runtime.InteropServices.ExternalException) { }
    }
}

/// <summary>将连续增量合并到短 UI 节奏，避免每个 token 都重建整段 Markdown。</summary>
public sealed class CopilotMarkdownView : FlowDocumentScrollViewer
{
    public static readonly DependencyProperty MarkdownProperty=DependencyProperty.Register(nameof(Markdown),typeof(string),typeof(CopilotMarkdownView),new PropertyMetadata("",Changed));
    private readonly DispatcherTimer timer;
    private static readonly MarkdownToFlowDocumentConverter Converter=new();
    public string Markdown {get=>(string)GetValue(MarkdownProperty);set=>SetValue(MarkdownProperty,value);}
    public CopilotMarkdownView()
    {
        Background=Brushes.Transparent;BorderThickness=new Thickness(0);Padding=new Thickness(0);IsToolBarVisible=false;IsSelectionEnabled=true;
        VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(45)};
        timer.Tick+=(_,_)=> {timer.Stop(); if(IsVisible)RefreshDocument();};
        Unloaded+=(_,_)=>timer.Stop(); Loaded+=(_,_)=>timer.Start(); IsVisibleChanged+=(_,e)=>{if(e.NewValue is true)timer.Start();};
    }
    public void RefreshDocument()
    {
        var document=(FlowDocument)Converter.Convert(Markdown??"",typeof(FlowDocument),null!,CultureInfo.CurrentCulture);document.PagePadding=new Thickness(0);Document=document;
    }
    private static void Changed(DependencyObject sender,DependencyPropertyChangedEventArgs e) {var view=(CopilotMarkdownView)sender;if(view.timer is not null && !view.timer.IsEnabled)view.timer.Start();}
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        for(var parent=VisualTreeHelper.GetParent(this);parent is not null;parent=VisualTreeHelper.GetParent(parent))
            if(parent is ScrollViewer scroll) {scroll.ScrollToVerticalOffset(scroll.VerticalOffset-e.Delta);e.Handled=true;return;}
        base.OnPreviewMouseWheel(e);
    }
}

public sealed class ProcessLabelConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        var key = value is ChatSegmentStatus status ? "CopilotStatus_" + status : "CopilotTool_" + value;
        var label=L.Text(key); return label==key?value?.ToString()??"":label;
    }
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}

public sealed class JsonDetailConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        var text=value as string??"";try {using var json=JsonDocument.Parse(text);return JsonSerializer.Serialize(json.RootElement,new JsonSerializerOptions{WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping});}catch(JsonException){return text;}
    }
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
