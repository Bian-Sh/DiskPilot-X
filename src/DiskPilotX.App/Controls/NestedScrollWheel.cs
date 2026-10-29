using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace DiskPilotX.App.Controls;

/// <summary>宿主嵌套滚轮路由：内部可滚时保留原生步长，边界才交给同窗口外层。</summary>
public static class NestedScrollWheel
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached(
        "Enabled",typeof(bool),typeof(NestedScrollWheel),new PropertyMetadata(false,OnEnabledChanged));
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    private static void OnEnabledChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not DataGrid grid)return;
        if((bool)args.OldValue)grid.PreviewMouseWheel-=OnWheel;
        if((bool)args.NewValue)grid.PreviewMouseWheel+=OnWheel;
    }
    private static void OnWheel(object sender,MouseWheelEventArgs args)
    {
        if((Keyboard.Modifiers&ModifierKeys.Shift)!=0)return;
        var grid=(DataGrid)sender;
        Forward(grid,FindScroll(grid),args);
    }
    public static void Forward(DependencyObject region,ScrollViewer? inner,MouseWheelEventArgs args)
    {
        if(args.Handled || args.Delta==0)return;
        var target=inner;
        if(inner is null || inner.ScrollableHeight<=0 || (args.Delta>0 ? inner.VerticalOffset<=0 : inner.VerticalOffset>=inner.ScrollableHeight))
        {
            target=null;
            for(var parent=VisualTreeHelper.GetParent(region);parent is not null;parent=VisualTreeHelper.GetParent(parent))
                if(parent is ScrollViewer viewer){target=viewer;break;}
        }
        if(target is null)return;
        args.Handled=true;
        target.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice,args.Timestamp,args.Delta) { RoutedEvent=Mouse.MouseWheelEvent,Source=target });
    }
    public static ScrollViewer? FindScroll(DependencyObject root)
    {
        if(root is ScrollViewer viewer)return viewer;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            if(FindScroll(VisualTreeHelper.GetChild(root,i)) is {} child)return child;
        return null;
    }
}
