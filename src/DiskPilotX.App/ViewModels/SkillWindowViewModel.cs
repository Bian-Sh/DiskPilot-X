using System.ComponentModel;
using DiskPilotX.App.Localization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DiskPilotX.App.Converters;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.ViewModels;

/// <summary>
/// 按需视图窗口的数据与动作。窗口只负责呈现与收集用户勾选，
/// 一切读取与清理仍由宿主回调完成，源码与 XAML 都不直接操作磁盘。
/// </summary>
public sealed class SkillWindowViewModel : INotifyPropertyChanged, IDisposable
{
    public SkillUiSpec Spec { get; }
    public IReadOnlyList<SkillUiAction> Actions => Spec.Actions;
    public ICommand ActionCommand { get; }

    /// <summary>用户点击「预览清理候选」。</summary>
    public event Action<IReadOnlyList<SkillUiItem>>? PreviewRequested;
    /// <summary>用户点击「回传选中项继续」。</summary>
    public event Action<IReadOnlyList<SkillUiItem>>? ContinueRequested;
    /// <summary>用户点击「关闭」。</summary>
    public event Action? CloseRequested;

    public SkillWindowViewModel(SkillUiSpec spec)
    {
        Spec = spec;
        ActionCommand = new RelayCommand<SkillUiAction>(ExecuteAction);
        foreach (var item in SelectableItems) item.PropertyChanged += OnItemPropertyChanged;
    }

    public IEnumerable<SkillUiItem> SelectableItems =>
        Spec.Sections.SelectMany(section => section.Items).Where(item => item.Selectable).ToArray();

    public IReadOnlyList<SkillUiItem> SelectedItems =>
        SelectableItems.Where(item => item.Selected).ToArray();

    public bool HasSelection => SelectedItems.Count > 0;

    public string SelectionSummary
    {
        get
        {
            var selected = SelectedItems;
            if (selected.Count == 0) return L.Text("SkillSelectionEmpty");
            var bytes = selected.Sum(item => item.SizeBytes);
            return bytes > 0
                ? L.Format("SkillSelectionSizeFormat", selected.Count, FileSizeConverter.FormatSize(bytes))
                : L.Format("SkillSelectionCountFormat", selected.Count);
        }
    }

    /// <summary>收集窗口里所有被勾选的条目，用于回传给助手继续处理。</summary>
    public string BuildSelectionPayload()
    {
        var selected = SelectedItems;
        if (selected.Count == 0) return "（用户未选择任何条目）";
        var lines = selected.Select(item =>
            string.IsNullOrWhiteSpace(item.Path)
                ? $"- {SkillTextExtension.Resolve(item.Title ?? "",Spec.Localizations)}{ValueSuffix(item)}"
                : $"- {SkillTextExtension.Resolve(item.Title ?? "",Spec.Localizations)}{ValueSuffix(item)} → {item.Path}");
        return string.Join("\n", lines);
    }

    private string ValueSuffix(SkillUiItem item) =>
        string.IsNullOrWhiteSpace(item.Value) ? "" : $"（{SkillTextExtension.Resolve(item.Value!,Spec.Localizations)}）";

    private void ExecuteAction(SkillUiAction? action)
    {
        if (action is null) return;
        if (action.IsPreview) PreviewRequested?.Invoke(SelectedItems);
        else if (action.IsContinue) ContinueRequested?.Invoke(SelectedItems);
        else CloseRequested?.Invoke();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SkillUiItem.Selected))
        {
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public void RefreshLocalization() => OnPropertyChanged(nameof(SelectionSummary));

    public void Dispose()
    {
        foreach (var item in SelectableItems) item.PropertyChanged -= OnItemPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
