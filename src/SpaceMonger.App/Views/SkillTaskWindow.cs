using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SpaceMonger.Core.Models;
using SpaceMonger.App.Controls;
using SpaceMonger.App.Helpers;
namespace SpaceMonger.App.Views;

/// <summary>清理任务的可重开容器。所有操作通过宿主回调，不执行 XAML 代码。</summary>
public sealed class SkillTaskWindow : Window
{
    private readonly JsonObject fields = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0,10,0,10) };
    private readonly TextBox result = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 160, MaxHeight = 320 };
    private readonly Func<string,JsonObject,CancellationToken,Task<JsonNode>> execute;
    private readonly CancellationTokenSource lifetime = new();
    private bool busy;
    private readonly List<(DataGrid Grid,string Key)> tables = [];
    private readonly List<Button> actions = [];
    public SkillTaskWindow(string title, string? xaml, JsonObject? data, Func<string,JsonObject,CancellationToken,Task<JsonNode>> execute)
    {
        this.execute = execute;
        ThemeInput(result);
        SetResourceReference(FontFamilyProperty,"VP.FontFamily");
        Title = title; Width = 780; Height = 700; MinWidth = 560; MinHeight = 430; ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"VP.BackgroundAltBrush"); SetResourceReference(ForegroundProperty,"VP.TextPrimaryBrush");
        var body = new StackPanel { Margin = new(22) };
        body.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,8) });
        body.Children.Add(new TextBlock { Text = "选择检查条件，先预览真实数据；清理仍需在助手中确认。", TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,18) });
        var values = data ?? new JsonObject();
        var document = string.IsNullOrWhiteSpace(xaml) ? null : SkillUiDocument.Parse(xaml);
        bool HasScope(SkillUiDocument node) => node.Properties.GetValueOrDefault("Name")=="scope_path" || node.Children.Any(HasScope);
        if(document is null || !HasScope(document))
        {
            body.Children.Add(new TextBlock { Text="检查范围", Margin=new(0,6,0,3) });
            var scopeBox=new TextBox { Text=values["scope_path"]?.ToString()??"", Padding=new(8), Margin=new(0,0,0,12) };
            ThemeInput(scopeBox);
            fields["scope_path"]=scopeBox.Text; scopeBox.TextChanged+=(_,_)=>fields["scope_path"]=scopeBox.Text;
            body.Children.Add(scopeBox);
        }
        if (document is not null) body.Children.Add(Build(document, values));
        var buttons = new WrapPanel { Margin = new(0,16,0,0) };
        foreach (var item in new[]{("preview","预览候选"),("continue","交给助手继续"),("close","收起面板")}) buttons.Children.Add(ActionButton(item.Item2,item.Item1));
        body.Children.Add(buttons); body.Children.Add(status); body.Children.Add(result);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var titleBar = new WindowTitleBar { ApplicationToolsVisibility = Visibility.Collapsed };
        titleBar.CloseRequested += (_, _) => Close();
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(titleBar);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        var frame = new Border { Child = layout, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "VP.BackgroundAltBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "VP.BorderLightBrush");
        frame.SetResourceReference(Border.CornerRadiusProperty, "VP.WindowCornerRadius");
        Content = frame;
        WindowChromeHelper.Apply(this, frame, titleBar);
        IsVisibleChanged += (_, _) =>
        {
            // 后台窗口在最大化状态下不能直接 Show；收起时还原以保证可重开。
            if (!IsVisible && WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        };
        Closing += (_,e) => { if (Owner?.IsVisible == true) { e.Cancel = true; Hide(); } else lifetime.Cancel(); };
    }
    private string Value(SkillUiDocument node,string key,JsonObject data,string fallback="")
    {
        if (!node.Properties.TryGetValue(key,out var value)) return fallback;
        return SkillUiDocument.BindingField(value) is { } field ? data[field]?.ToString() ?? "" : value;
    }
    private FrameworkElement Build(SkillUiDocument node,JsonObject data)
    {
        var name = node.Properties.GetValueOrDefault("Name");
        FrameworkElement control;
        switch(node.Type)
        {
            case "StackPanel":
                var stack = new StackPanel { Orientation = Value(node,"Orientation",data) == "Horizontal" ? Orientation.Horizontal : Orientation.Vertical };
                foreach(var child in node.Children) stack.Children.Add(Build(child,data)); control = stack; break;
            case "Grid":
                var grid = new UniformGrid { Columns = int.TryParse(Value(node,"Columns",data),out var columns) ? Math.Clamp(columns,1,4) : 1 };
                foreach(var child in node.Children) grid.Children.Add(Build(child,data)); control = grid; break;
            case "Border":
                var border = new Border { Padding = new(12), BorderThickness = new(1) }; border.SetResourceReference(Border.BorderBrushProperty,"VP.BorderLightBrush"); border.SetResourceReference(Border.CornerRadiusProperty,"VP.CardCornerRadius"); border.SetResourceReference(Border.BackgroundProperty,"VP.SurfaceBrush");
                if(node.Children.Count > 0) border.Child = Build(node.Children[0],data); control = border; break;
            case "TextBlock": control = new TextBlock { Text = Value(node,"Text",data), TextWrapping = TextWrapping.Wrap, FontSize = double.TryParse(Value(node,"FontSize",data),CultureInfo.InvariantCulture,out var size) ? Math.Clamp(size,10,32) : 13, FontWeight = Value(node,"FontWeight",data)=="SemiBold" ? FontWeights.SemiBold : FontWeights.Normal }; break;
            case "TextBox":
                var text = new TextBox { Text = Value(node,"Text",data), MaxLength = int.TryParse(Value(node,"MaxLength",data),out var max) ? Math.Clamp(max,1,4000) : 4000, MinWidth = 120, Padding = new(7) };
                ThemeInput(text);
                if(name is not null) { fields[name] = text.Text; text.TextChanged += (_,_)=>fields[name]=text.Text; } control=text; break;
            case "CheckBox":
                var check = new CheckBox { Content = Value(node,"Content",data), IsChecked = bool.TryParse(Value(node,"IsChecked",data),out var selected)&&selected };
                check.SetResourceReference(StyleProperty,"VP.CheckBox");
                if(name is not null) { fields[name]=check.IsChecked==true; check.Checked+=(_,_)=>fields[name]=true; check.Unchecked+=(_,_)=>fields[name]=false; } control=check; break;
            case "ComboBox":
                var combo = new ComboBox { MinWidth = 140, ItemsSource = node.Children.Select(c=>Value(c,"Content",data)).ToArray() };
                combo.SetResourceReference(StyleProperty,"VP.ComboBox");
                combo.SelectedItem = Value(node,"SelectedValue",data); if(combo.SelectedItem is null && combo.Items.Count > 0) combo.SelectedIndex=0;
                if(name is not null) { fields[name]=combo.SelectedItem?.ToString(); combo.SelectionChanged+=(_,_)=>fields[name]=combo.SelectedItem?.ToString(); } control=combo; break;
            case "Button": control=ActionButton(Value(node,"Content",data),Value(node,"Command",data)); break;
            case "DataGrid":
                var key=SkillUiDocument.BindingField(node.Properties.GetValueOrDefault("ItemsSource")??"");
                var rows = (key is not null ? data[key] as JsonArray : null)?.Select(n=>new { 路径=n?["path"]?.ToString(), 大小=n?["size_bytes"]?.ToString(), 说明=n?["explanation"]?.ToString() }).ToList();
                var table=new DataGrid { ItemsSource=rows, IsReadOnly=true, AutoGenerateColumns=true, CanUserAddRows=false, MaxHeight=260 };
                table.SetResourceReference(StyleProperty,"VP.DataGrid");
                table.AutoGeneratingColumn += (_, args) =>
                {
                    args.Column.Width = args.PropertyName == "大小" ? new DataGridLength(90) : new DataGridLength(args.PropertyName == "路径" ? 2 : 1, DataGridLengthUnitType.Star);
                    if (args.Column is DataGridTextColumn column) column.ElementStyle = (Style)table.FindResource("VP.DataGridText");
                };
                if(key is not null) tables.Add((table,key)); control=table; break;
            default: throw new ArgumentException("不支持的面板控件。");
        }
        control.Margin=new(0,4,8,4); return control;
    }
    private static void ThemeInput(Control control)
    {
        control.SetResourceReference(StyleProperty,"VP.TextBox");
    }
    private Button ActionButton(string title,string command)
    {
        var button=new Button { Content=title, Padding=new(12,7,12,7), Margin=new(0,0,8,0) };
        button.SetResourceReference(StyleProperty,"VP.Button");
        button.Click+=async(_,_)=>await RunAsync(command); actions.Add(button); return button;
    }
    private async Task RunAsync(string command)
    {
        if(command=="close") { Hide(); return; }
        if(busy) return;
        busy=true; foreach(var button in actions)button.IsEnabled=false;
        try
        {
            status.Text=command=="preview"?"正在只读检查…":"正在交给助手…";
            var response=await execute(command,(JsonObject)fields.DeepClone(),lifetime.Token);
            foreach(var (table,key) in tables) if(response[key] is JsonArray items) table.ItemsSource=items.Select(n=>new { 路径=n?["path"]?.ToString(), 大小=n?["size_bytes"]?.ToString(), 说明=n?["description"]?.ToString() }).ToList();
            status.Text=response["ok"]?.GetValue<bool>()==false?"操作未完成，请查看下方说明。":"已完成；关闭面板后仍可从聊天卡片重新打开。";
            if(response["candidates"] is JsonArray candidates)
            {
                var bytes=candidates.Sum(c=>long.TryParse(c?["size_bytes"]?.ToString(),out var b)?b:0);
                result.Text=$"找到 {candidates.Count} 项，共 {SpaceMonger.App.Converters.FileSizeConverter.FormatSize(bytes)}，尚未清理。\n\n"+
                    string.Join("\n\n",candidates.Take(100).Select(c=>c?["path"]+"\n"+c?["description"]+"  "+c?["risk"]))+
                    (candidates.Count>100?"\n\n仅显示前 100 项，请缩小范围查看明细。":"")+
                    (response["warnings"] is JsonArray warnings && warnings.Count>0?"\n\n检查提示：\n"+string.Join("\n",warnings.Select(w=>w?.ToString())):"");
            }
            else result.Text=response["message"]?.ToString()??response["error"]?.ToString()??response["diagnostics"]?.ToJsonString()??"操作已完成。";
        }
        catch(OperationCanceledException) { status.Text="任务已停止。"; }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException) { status.Text="无法完成检查："+ex.Message; }
        finally { busy=false; foreach(var button in actions)button.IsEnabled=true; }
    }
}
