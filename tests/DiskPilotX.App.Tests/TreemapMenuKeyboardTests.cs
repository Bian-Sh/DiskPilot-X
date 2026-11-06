using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DiskPilotX.App.Controls;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.App.Tests;

public class TreemapMenuKeyboardTests
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;

    [Fact]
    public void 后台真实WPF菜单键方向Enter与Esc闭环且前台不变() => StaHost.Run(() =>
    {
        var resources=Application.Current.Resources;
        var keys=new object[] {typeof(ContextMenu),typeof(MenuItem),"VP.Button"};
        var saved=keys.ToDictionary(k=>k,k=>(Present:resources.Contains(k),Value:resources[k]));
        var theme=new ResourceDictionary {Source=new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml",UriKind.RelativeOrAbsolute)};
        var invisible=new Style(typeof(ContextMenu),(Style)theme[typeof(ContextMenu)]);
        invisible.Setters.Add(new Setter(UIElement.OpacityProperty,0d));
        resources[typeof(ContextMenu)]=invisible;resources[typeof(MenuItem)]=theme[typeof(MenuItem)];resources["VP.Button"]=new Style(typeof(Button));
        var vm=new TreemapViewModel(new SquarifiedTreemapLayout());var view=new TreemapView();view.SetViewModel(vm);
        var window=new Window {Content=view,Width=600,Height=400,Left=-10000,Top=-10000,Opacity=0,
            ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,Title="树图后台键盘验收"};
        var foreground=GetForegroundWindow();ContextMenu? active=null;
        try
        {
            window.Show();window.UpdateLayout();Drain();
            var control=(TreemapControl)view.FindName("Treemap");
            ContextMenu Open()
            {
                RaiseKey(control,Key.Apps);Drain();
                active=(ContextMenu)typeof(TreemapView).GetProperty("ActiveContextMenu",Hidden)!.GetValue(view)!;
                Assert.NotNull(active);Assert.True(active.IsOpen);active.UpdateLayout();return active;
            }
            MenuItem Expand(ContextMenu menu)
            {
                var header=Assert.Single(menu.Items.OfType<MenuItem>());header.ApplyTemplate();
                var popup=(Popup)header.Template.FindName("PART_Popup",header);
                // 真实Popup和输入路由保持启用，只让验收窗口不可见，禁止切到前台。
                ((UIElement)popup.Child).Opacity=0;header.Focus();RaiseKey(header,Key.Right);Drain();
                Assert.True(header.IsSubmenuOpen);Assert.True(popup.IsOpen);return header;
            }
            var menu=Open();var depth=Expand(menu);
            var first=Assert.IsType<MenuItem>(FocusManager.GetFocusedElement(depth));Assert.Same(depth.Items[0],first);
            RaiseKey(first,Key.Up);Drain();
            var last=Assert.IsType<MenuItem>(FocusManager.GetFocusedElement(depth));Assert.Same(depth.Items[6],last);
            RaiseKey(last,Key.Down);Drain();Assert.Same(first,FocusManager.GetFocusedElement(depth));
            RaiseKey(first,Key.Down);Drain();
            var next=Assert.IsType<MenuItem>(FocusManager.GetFocusedElement(depth));Assert.Same(depth.Items[1],next);
            RaiseKey(next,Key.Enter);Drain();
            Assert.Equal(1,vm.DisplayDepth);Assert.False(menu.IsOpen);
            Assert.Single(depth.Items.OfType<MenuItem>().Where(i=>i.IsChecked));Assert.True(next.IsChecked);
            menu=Open();depth=Expand(menu);
            var current=Assert.IsType<MenuItem>(FocusManager.GetFocusedElement(depth));RaiseKey(current,Key.Escape);Drain();
            Assert.False(depth.IsSubmenuOpen);
            if(menu.IsOpen){depth.Focus();RaiseKey(depth,Key.Escape);Drain();}
            Assert.False(menu.IsOpen);Assert.Equal(foreground,GetForegroundWindow());
        }
        finally
        {
            if(active is not null)active.IsOpen=false;window.Close();
            foreach(var key in keys)if(saved[key].Present)resources[key]=saved[key].Value;else resources.Remove(key);
        }
    });

    private static void Drain()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void RaiseKey(UIElement target,Key key)
    {
        var source=PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("键盘目标未连接真实WPF源。");
        var args=new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,key) {RoutedEvent=Keyboard.PreviewKeyDownEvent};
        target.RaiseEvent(args);
        if(!args.Handled) {args.RoutedEvent=Keyboard.KeyDownEvent;target.RaiseEvent(args);}
    }
}
