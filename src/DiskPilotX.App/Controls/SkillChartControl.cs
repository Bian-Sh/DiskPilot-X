using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using DiskPilotX.App.Converters;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using Path=System.Windows.Shapes.Path;

namespace DiskPilotX.App.Controls;

/// <summary>宿主声明式图表。聊天和结果窗口复用，不接收可执行绘图代码。</summary>
public sealed partial class SkillChartControl:UserControl
{
    public static readonly DependencyProperty SectionProperty=DependencyProperty.Register(nameof(Section),typeof(SkillUiSection),typeof(SkillChartControl),new PropertyMetadata(null,OnAppearanceChanged));
    public SkillUiSection? Section {get=>(SkillUiSection?)GetValue(SectionProperty);set=>SetValue(SectionProperty,value);}
    private static readonly DependencyProperty AccentProperty=DependencyProperty.Register("Accent",typeof(Brush),typeof(SkillChartControl),new PropertyMetadata(Brushes.DodgerBlue,OnAppearanceChanged));
    private static readonly DependencyProperty ThemeBackgroundProperty=DependencyProperty.Register("ThemeBackground",typeof(Brush),typeof(SkillChartControl),new PropertyMetadata(Brushes.White,OnAppearanceChanged));
    private IDisposable? language;
    private bool compact=true;
    private bool rendering;
    public SkillChartControl()
    {
        Background=Brushes.Transparent;UseLayoutRounding=true;
        TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this,TextRenderingMode.ClearType);
        SetResourceReference(AccentProperty,"VP.AccentBrush");SetResourceReference(ThemeBackgroundProperty,"VP.BackgroundBrush");
        Loaded+=(_,_)=>{language??=L.Subscribe(_=>Render());Render();OnMotionLoaded();};
        Unloaded+=(_,_)=>{language?.Dispose();language=null;OnMotionUnloaded();};
        SizeChanged+=(_,_)=>{var next=ActualWidth<440;if(compact!=next){compact=next;Render();}};
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if(args.Property==SkillTextExtension.CatalogProperty)Render();
    }
    private static void OnAppearanceChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        var control=(SkillChartControl)target;control.Render();
        if(args.Property==SectionProperty && control.IsLoaded)control.StartReveal();
    }
    private string Resolve(string? value)=>SkillTextExtension.Resolve(value??"",SkillTextExtension.GetCatalog(this)??(Window.GetWindow(this)?.DataContext as SkillWindowViewModel)?.Spec.Localizations);
    private static TextBlock Label(string text,string brush="VP.TextPrimaryBrush",double size=12)
    {
        var result=new TextBlock { Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap };
        result.SetResourceReference(TextBlock.ForegroundProperty,brush);return result;
    }
    private void Render()
    {
        if(rendering)return;
        rendering=true;
        try
        {
            ResetRenderedMotion();
            if(Section is not { IsChart:true } section){Content=null;return;}
            var items=section.Items.Where(item=>double.IsFinite(item.Metric)&&item.Metric>0).ToArray();
            if(items.Length==0)
            {
                Content=new Border { Padding=new Thickness(16,24,16,24),Child=new TextBlock { Text=L.Text("SkillChartEmpty"),Tag="SkillChartEmpty",TextWrapping=TextWrapping.Wrap } };
                ((TextBlock)((Border)Content).Child).SetResourceReference(TextBlock.ForegroundProperty,"VP.TextSecondaryBrush");return;
            }
            if(section.IsBarChart){RenderBars(items);return;}
            // 先按最大值缩放再求和，防止多个有限大数相加溢出。
            var scale=items.Max(item=>item.Metric);var total=items.Sum(item=>item.Metric/scale);
            var canvas=new Canvas { Width=176,Height=176,Background=Brushes.Transparent };
            var legend=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
            var layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition { Width=compact ? new GridLength(1,GridUnitType.Star):new GridLength(196) });
            if(!compact)layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });if(compact)layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            var picture=new Grid { Width=176,Height=176,HorizontalAlignment=compact ? HorizontalAlignment.Center:HorizontalAlignment.Left,Margin=new Thickness(0,0,0,compact?16:0) };
            picture.Children.Add(canvas);layout.Children.Add(picture);
            Grid.SetColumn(legend,compact?0:1);Grid.SetRow(legend,compact?1:0);layout.Children.Add(legend);
            var start=0d;var culture=CultureInfo.GetCultureInfo(L.GetState().CultureName);
            for(var index=0;index<items.Length;index++)
            {
                var item=items[index];var fraction=(item.Metric/scale)/total;var end=Math.Min(1,start+fraction);
                var title=Resolve(item.Title);var value=string.IsNullOrWhiteSpace(item.Value)?item.Metric.ToString("G4",culture):Resolve(item.Value);
                var percentage=fraction.ToString("P1",culture);var hint=$"{title} · {value} · {percentage}";var brush=(SolidColorBrush)Palette(index);
                var final=Slice(start,end,section.Kind==SkillUiSectionKind.DonutChart);
                var offset=new TranslateTransform();
                var path=new Path { Data=final,Fill=brush,ToolTip=hint,Tag="SkillChartSlice",DataContext=item,RenderTransform=offset,IsHitTestVisible=false };
                var hit=new Path {Data=final,Fill=Brushes.Transparent,ToolTip=hint,Tag="SkillChartHit",DataContext=item};
                AutomationProperties.SetName(hit,hint);canvas.Children.Add(path);canvas.Children.Add(hit);RevealSlice(path,hit,start,end,section.Kind==SkillUiSectionKind.DonutChart);
                var row=new Grid { Margin=new Thickness(0,0,0,10),ToolTip=hint,Tag="SkillChartLegend",DataContext=item,Background=Brushes.Transparent };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(18) });row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
                row.Children.Add(new Border { Width=8,Height=8,CornerRadius=new CornerRadius(4),Background=brush,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,4,6,0) });
                var name=Label(title);name.Margin=new Thickness(0,0,12,0);Grid.SetColumn(name,1);row.Children.Add(name);
                var numbers=new StackPanel { HorizontalAlignment=HorizontalAlignment.Right };
                var number=Label(value);number.TextAlignment=TextAlignment.Right;numbers.Children.Add(number);
                var percent=Label(percentage,"VP.TextSecondaryBrush",11);percent.TextAlignment=TextAlignment.Right;numbers.Children.Add(percent);
                Grid.SetColumn(numbers,2);row.Children.Add(numbers);legend.Children.Add(row);
                var angle=(start+end)*Math.PI-Math.PI/2;
                AttachHover(hit,row,brush,offset,Math.Cos(angle)*4,Math.Sin(angle)*4);start=end;
            }
            if(section.Kind==SkillUiSectionKind.DonutChart)
            {
                var sum=scale*total;
                var text=double.IsFinite(sum)?sum.ToString("G4",culture):$"{total.ToString("G4",culture)} × {scale.ToString("G4",culture)}";
                var center=new StackPanel { VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Width=94,IsHitTestVisible=false };
                var totalLabel=Label(L.Text("SkillChartTotal"),"VP.TextSecondaryBrush",11);totalLabel.TextAlignment=TextAlignment.Center;center.Children.Add(totalLabel);
                var number=Label(text,size:17);number.FontWeight=FontWeights.SemiBold;number.TextAlignment=TextAlignment.Center;center.Children.Add(number);picture.Children.Add(center);
            }
            Content=layout;
        }
        finally {rendering=false;}
    }
    private void RenderBars(SkillUiItem[] items)
    {
        var panel=new StackPanel();
        foreach(var item in items)
        {
            var row=new Grid { Margin=new Thickness(0,0,0,12),DataContext=item };
            row.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });row.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var title=Label(Resolve(item.Title),"VP.TextSecondaryBrush");title.Margin=new Thickness(0,0,12,0);title.ToolTip=title.Text;row.Children.Add(title);
            var value=Label(string.IsNullOrWhiteSpace(item.Value)?item.Metric.ToString("G4",CultureInfo.GetCultureInfo(L.GetState().CultureName)):Resolve(item.Value));Grid.SetColumn(value,1);row.Children.Add(value);
            var fill=new Border { HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(4),Tag="SkillBarFill",DataContext=item };
            var brush=(SolidColorBrush)Palette(0);fill.Background=brush;RevealBar(fill);
            var track=new Border { Height=8,CornerRadius=new CornerRadius(4),Margin=new Thickness(0,6,0,0),Child=fill,ToolTip=$"{title.Text} · {value.Text}" };
            track.SetResourceReference(Border.BackgroundProperty,"VP.SurfaceBrush");Grid.SetRow(track,1);Grid.SetColumnSpan(track,2);row.Children.Add(track);
            var binding=new MultiBinding { Converter=new RatioToLengthConverter() };
            binding.Bindings.Add(new Binding(nameof(SkillUiItem.Metric)) { Source=item });binding.Bindings.Add(new Binding("Section.MaxMetric") { Source=this });binding.Bindings.Add(new Binding(nameof(ActualWidth)) { Source=track });
            fill.SetBinding(WidthProperty,binding);AttachHover(row,null,brush);panel.Children.Add(row);
        }
        Content=panel;
    }
    private Brush Palette(int index)
    {
        var accent=(GetValue(AccentProperty) as SolidColorBrush)?.Color??Colors.DodgerBlue;
        if(index==0)return new SolidColorBrush(accent);
        var background=(GetValue(ThemeBackgroundProperty) as SolidColorBrush)?.Color??Colors.White;
        var dark=(background.R*.299+background.G*.587+background.B*.114)<128;
        // 等距错开的色相避免相邻扇区混色；主题切换时重建，但不改变条目和窗口状态。
        var hue=(index*137.508+210)%360/60;var value=dark?.94:.78;var saturation=dark?.55:.67;
        var chroma=value*saturation;var x=chroma*(1-Math.Abs(hue%2-1));var m=value-chroma;
        var rgb=(int)hue switch {0=>(chroma,x,0d),1=>(x,chroma,0d),2=>(0d,chroma,x),3=>(0d,x,chroma),4=>(x,0d,chroma),_=>(chroma,0d,x)};
        return new SolidColorBrush(Color.FromRgb((byte)Math.Round((rgb.Item1+m)*255),(byte)Math.Round((rgb.Item2+m)*255),(byte)Math.Round((rgb.Item3+m)*255)));
    }
    private static Geometry Slice(double start,double end,bool donut)
    {
        const double radius=86;const double inner=53;var center=new Point(88,88);
        if(end-start>=1-1e-14)
        {
            var circle=new GeometryGroup { FillRule=FillRule.EvenOdd };circle.Children.Add(new EllipseGeometry(center,radius,radius));
            if(donut)circle.Children.Add(new EllipseGeometry(center,inner,inner));circle.Freeze();return circle;
        }
        Point At(double fraction,double r){var angle=fraction*2*Math.PI-Math.PI/2;return new Point(88+r*Math.Cos(angle),88+r*Math.Sin(angle));}
        var data=new StreamGeometry();using(var context=data.Open())
        {
            var large=end-start>.5;
            context.BeginFigure(At(start,radius),true,true);
            context.ArcTo(At(end,radius),new Size(radius,radius),0,large,SweepDirection.Clockwise,true,false);
            if(donut){context.LineTo(At(end,inner),true,false);context.ArcTo(At(start,inner),new Size(inner,inner),0,large,SweepDirection.Counterclockwise,true,false);}
            else context.LineTo(center,true,false);
        }
        data.Freeze();return data;
    }
}
