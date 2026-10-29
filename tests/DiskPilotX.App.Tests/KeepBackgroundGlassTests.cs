using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DiskPilotX.App.Helpers;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Models.Theme;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class KeepBackgroundGlassTests
{
    [Fact]
    public void 设置立即保存且配色切换及重新加载不丢失() => Isolated(() =>
    {
        var settings=Substitute.For<ISettingsService>();
        var saved=new AppSettings { ThemeProfile=ThemeProfile.VisionProDark.Clone() };
        settings.LoadSettings().Returns(saved);
        var manager=new ThemeManager(settings);manager.Initialize();
        using var clients=new ModelHttpClientFactory();
        var vm=new SettingsViewModel(settings,new ModelConnectionTester(new ModelClientFactory(clients)),manager);
        var property=typeof(SettingsViewModel).GetProperty("KeepGlassWhenInactive");Assert.NotNull(property);
        property!.SetValue(vm,true);
        using(var json=JsonDocument.Parse(JsonSerializer.Serialize(saved.ThemeProfile))) Assert.True(json.RootElement.GetProperty("KeepGlassWhenInactive").GetBoolean());
        vm.ApplyThemePresetCommand.Execute(ThemeProfile.VisionProLight);
        Assert.True(Assert.IsType<bool>(property.GetValue(vm)));
        var reload=new ThemeManager(settings);reload.Initialize();
        using(var json=JsonDocument.Parse(JsonSerializer.Serialize(reload.CurrentProfile))) Assert.True(json.RootElement.GetProperty("KeepGlassWhenInactive").GetBoolean());
    });

    [Fact]
    public void 后台材质绘制不改变真实激活状态和前台() => StaHost.Run(() =>
    {
        var setter=typeof(AcrylicHelper).GetMethod("SetKeepGlassWhenInactive",[typeof(Window),typeof(bool)]);
        Assert.NotNull(setter);
        var window=new SkillWindow(new SkillUiSpec { Title="后台材质验证" }) { ShowActivated=false };
        var activated=0;window.Activated+=(_,_)=>activated++;
        var before=GetForegroundWindow();
        try
        {
            window.Show();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            Assert.False(window.IsActive);
            var hwnd=new WindowInteropHelper(window).Handle;
            if(!AcrylicHelper.TryEnableAcrylic(window)) return;
            Assert.True(Assert.IsType<bool>(setter!.Invoke(null,[window,true])));
            Assert.False(window.IsActive);Assert.Equal(0,activated);Assert.Equal(before,GetForegroundWindow());
            _=SendMessage(hwnd,0x0086,IntPtr.Zero,IntPtr.Zero);
            Assert.False(window.IsActive);Assert.Equal(0,activated);Assert.Equal(before,GetForegroundWindow());
            Assert.False(Assert.IsType<bool>(setter.Invoke(null,[window,false])));
            Assert.False(window.IsActive);Assert.Equal(before,GetForegroundWindow());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void 主题界面有后台通透开关且磨砂关闭时不可操作()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"src","DiskPilotX.App","Views","SettingsSections","ThemeSettingsSection.xaml")))root=root.Parent;
        Assert.NotNull(root);
        var xml=System.Xml.Linq.XDocument.Load(Path.Combine(root!.FullName,"src","DiskPilotX.App","Views","SettingsSections","ThemeSettingsSection.xaml"));
        var control=Assert.Single(xml.Descendants(),e=>e.Name.LocalName=="CheckBox" && (string?)e.Attribute("IsChecked")=="{Binding KeepGlassWhenInactive}");
        Assert.Contains(control.Ancestors(),e=>(string?)e.Attribute("IsEnabled")=="{Binding GlassEnabled}");
    }
    private static void Isolated(Action action) => StaHost.Run(() =>
    {
        // 主题设置会写入应用级动态画刷；测试必须恢复资源，避免污染其他 STA 线程的控件回归。
        var previous=Application.Current.Resources;
        Application.Current.Resources=new ResourceDictionary();
        try { action(); } finally { Application.Current.Resources=previous; }
    });
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}