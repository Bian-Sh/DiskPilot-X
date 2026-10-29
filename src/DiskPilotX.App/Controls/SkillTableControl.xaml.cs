using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DiskPilotX.App.Converters;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Controls;

/// <summary>技能内置表格；列布局与交互由宿主管理，不执行技能提供的 UI 代码。</summary>
public partial class SkillTableControl : UserControl
{
    public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(nameof(Section),
        typeof(SkillUiSection), typeof(SkillTableControl), new PropertyMetadata(null, OnSectionChanged));
    public SkillUiSection? Section { get => (SkillUiSection?)GetValue(SectionProperty); set => SetValue(SectionProperty, value); }
    private SkillUiItem[] subscribed = [];
    private bool batching;
    private bool updatingHeader;
    private IDisposable? localization;

    public SkillTableControl()
    {
        InitializeComponent();
        Table.SizeChanged += (_, args) =>
        {
            if(!args.WidthChanged) return;
            // 最大化达到 MaxWidth 后，WPF 会保留比例列的旧显示宽度；视口变化时重新测量。
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(() =>
            {
                foreach(var column in Table.Columns.Where(column => column.Width.IsStar))
                    column.Width=new DataGridLength(column.Width.Value,DataGridLengthUnitType.Star);
            }));
        };
        Loaded += (_, _) => { BuildColumns(); Subscribe(); localization ??= L.Subscribe(_ => { BuildColumns(); UpdateSelection(); }); };
        Unloaded += (_, _) => { Unsubscribe(); localization?.Dispose(); localization=null; };
    }

    private static void OnSectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control=(SkillTableControl)sender;
        control.Unsubscribe();
        control.Table.ItemsSource=control.Section?.Items;
        control.BuildColumns();
        control.Subscribe();
    }

    private IEnumerable<SkillUiItem> Selectable => Section?.Items.Where(item => item.Selectable).Distinct() ?? [];
    private void Subscribe()
    {
        Unsubscribe();
        subscribed=Selectable.ToArray();
        foreach(var item in subscribed) item.PropertyChanged += ItemChanged;
        UpdateSelection();
    }
    private void Unsubscribe()
    {
        foreach(var item in subscribed) item.PropertyChanged -= ItemChanged;
        subscribed=[];
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs args)
    { if (!batching && args.PropertyName==nameof(SkillUiItem.Selected)) UpdateSelection(); }

    private void SelectAll_Changed(object sender, RoutedEventArgs args)
    {
        if(updatingHeader || Table is null) return;
        var items=Selectable.ToArray();
        if(items.Length==0) return;
        var select=!items.All(item => item.Selected);
        SetSelection(item => select);
    }
    private void Invert_Click(object sender, RoutedEventArgs args) => SetSelection(item => !item.Selected);
    private void SetSelection(Func<SkillUiItem,bool> selected)
    {
        batching=true;
        try { foreach(var item in Selectable) item.Selected=selected(item); }
        finally { batching=false; UpdateSelection(); }
    }
    private void UpdateSelection()
    {
        var items=Selectable.ToArray();
        var selected=items.Where(item => item.Selected).ToArray();
        updatingHeader=true;
        try { SelectAllCheckBox.IsChecked=selected.Length==0 ? false : selected.Length==items.Length ? true : null; }
        finally { updatingHeader=false; }
        SelectAllCheckBox.IsEnabled=items.Length>0;
        var visibility=items.Length>0 ? Visibility.Visible : Visibility.Collapsed;
        SelectionBar.Visibility=InvertButton.Visibility=visibility;
        var bytes=selected.Sum(item => Math.Max(0,item.SizeBytes));
        SelectionText.Text=L.Format("SkillTableSelectionFormat",selected.Length,items.Length) +
            (bytes>0 ? " · " + FileSizeConverter.FormatSize(bytes) : "");
        EmptyHint.Visibility=Section?.Items.Count>0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private string Resolve(string value) => SkillTextExtension.Resolve(value,
        (Window.GetWindow(this)?.DataContext as SkillWindowViewModel)?.Spec.Localizations);

    private void BuildColumns()
    {
        if (Table is null) return;
        Table.Columns.Clear();
        var section=Section;
        if (section is null || !section.IsTable) return;
        if (Selectable.Any()) Table.Columns.Add(SelectionColumn);
        var count=section.ColumnCount;
        var longFirst=section.Items.Any(item => (item.Cells.FirstOrDefault()?.Length ?? 0)>16);
        Table.MinHeight=section.Items.Count>0 ? 0 : 110;
        for(var i=0;i<count;i++)
        {
            var definition=section.TableColumns.Count>i ? SkillUiTableColumn.Normalize(section.TableColumns[i]) :
                new SkillUiTableColumn
                {
                    Header=section.Headers?.ElementAtOrDefault(i) ?? "",
                    Width=i==count-1 ? "2*" : i==0 && count>2 && longFirst ? "1.2*" : "auto",
                    MinWidth=i==count-1 && count>2 ? 180 : i==0 && count>2 && longFirst ? 140 : 64,
                    MaxWidth=i==count-1 ? 1600 : i==0 && count>2 && longFirst ? 480 : 240,
                    Wrap=i==count-1 && count>2
                };
            var textStyle=new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,definition.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap));
            textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));
            textStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,definition.Align switch { "right" => TextAlignment.Right, "center" => TextAlignment.Center, _ => TextAlignment.Left }));
            textStyle.Setters.Add(new Setter(TextBlock.ToolTipProperty,new Binding(nameof(TextBlock.Text)) { RelativeSource=RelativeSource.Self }));
            var header=new TextBlock { Text=Resolve(definition.Header), TextTrimming=TextTrimming.CharacterEllipsis,
                TextAlignment=definition.Align switch { "right" => TextAlignment.Right,"center" => TextAlignment.Center,_ => TextAlignment.Left } };
            header.ToolTip=header.Text;
            Table.Columns.Add(new DataGridTextColumn
            {
                Header=header, Binding=new Binding($"Cells[{i}]") { Converter=new CellText(this) },
                Width=ParseWidth(definition.Width), MinWidth=definition.MinWidth, MaxWidth=definition.MaxWidth,
                ElementStyle=textStyle
            });
        }
        if(section.Items.Any(item => item.HasAction)) Table.Columns.Add(new DataGridTemplateColumn
        { Width=DataGridLength.Auto, MinWidth=80, MaxWidth=200, CellTemplate=(DataTemplate)Resources["TableAction"] });
        Table.HeadersVisibility=Selectable.Any() || section.TableColumns.Any(column => !string.IsNullOrEmpty(column.Header)) || section.Headers is { Count: > 0 }
            ? DataGridHeadersVisibility.Column : DataGridHeadersVisibility.None;
    }
    private static DataGridLength ParseWidth(string width)
    {
        if(width=="auto") return DataGridLength.Auto;
        var star=width.EndsWith('*');
        var value=star ? width[..^1] : width;
        if(value.Length==0) value="1";
        return new DataGridLength(double.Parse(value,CultureInfo.InvariantCulture),star ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel);
    }
    private void Body_SizeChanged(object sender, SizeChangedEventArgs args) =>
        Body.Clip=new RectangleGeometry(new Rect(args.NewSize),9,9);

    private sealed class CellText(SkillTableControl owner) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => owner.Resolve(value as string ?? "");
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
