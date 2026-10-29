using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskPilotX.App.Services;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Models.Theme;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class SkillWindowTransparencyTests
{
    [Fact]
    public void 材质只作用单层背景并按每窗结果回退() => Isolated(() =>
    {
        var settings=Substitute.For<ISettingsService>();
        var profile=ThemeProfile.VisionProLight.Clone();profile.GlassEnabled=true;profile.GlassOpacity=0;
        settings.LoadSettings().Returns(new AppSettings { ThemeProfile=profile });
        var manager=new ThemeManager(settings);manager.Initialize();
        var item=new SkillUiItem { Title="项目甲",Cells=["项目甲","8 GB"],Selectable=true,Selected=true };
        var window=new SkillWindow(new SkillUiSpec { Title="透明主题验收",Sections=[new SkillUiSection { Kind=SkillUiSectionKind.Table,Headers=["项目","大小"],Items=[item] }] });
        try
        {
            var frame=(Border)window.Content;
            var active=manager.ApplySkillWindowTheme(window,frame);
            var first=Assert.IsType<SolidColorBrush>(frame.Background).Color.A;
            Assert.Equal(1,window.Opacity);Assert.False(window.AllowsTransparency);Assert.Null(frame.Clip);
            if(active)
            {
                Assert.Equal(0,Assert.IsType<SolidColorBrush>(window.Background).Color.A);
                Assert.InRange(first,20,22);
                Assert.InRange(Assert.IsType<SolidColorBrush>(window.Resources["VP.SkillWindowSurfaceBrush"]).Color.A,1,40);
            }
            else Assert.Equal(255,Assert.IsType<SolidColorBrush>(window.Background).Color.A);
            profile=profile.Clone();profile.GlassOpacity=0.85;manager.ApplyTheme(profile);
            var high=manager.ApplySkillWindowTheme(window,frame);
            if(active && high) Assert.True(Assert.IsType<SolidColorBrush>(frame.Background).Color.A>first);
            profile=profile.Clone();profile.GlassEnabled=false;manager.ApplyTheme(profile);
            Assert.False(manager.ApplySkillWindowTheme(window,frame));
            Assert.Equal(255,Assert.IsType<SolidColorBrush>(window.Background).Color.A);
            Assert.Equal(0,Assert.IsType<SolidColorBrush>(frame.Background).Color.A);
            Assert.Equal(255,Assert.IsType<SolidColorBrush>(window.Resources["VP.SkillWindowSurfaceBrush"]).Color.A);
            Assert.Equal(1,window.Opacity);Assert.True(item.Selected);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void 表格跟随窗口局部材质而不使用实色底板() => Isolated(() =>
    {
        var window=new SkillWindow(new SkillUiSpec { Title="材质底板",Sections=[] });
        try
        {
            var tint=new SolidColorBrush(Color.FromArgb(18,255,255,255));
            window.Resources["VP.SkillWindowSurfaceBrush"]=tint;
            var table=new DiskPilotX.App.Controls.SkillTableControl { Section=new SkillUiSection { Kind=SkillUiSectionKind.Table,Headers=["项目"],Items=[new() { Cells=["项目甲"] }] } };
            var frame=(Border)window.Content;frame.Child=table;
            frame.Measure(new Size(800,600));frame.Arrange(new Rect(0,0,800,600));frame.UpdateLayout();
            Assert.Same(tint,Assert.IsType<Border>(table.FindName("Container")).Background);
            Assert.Equal(1,table.Opacity);
        }
        finally { window.Close(); }
    });

    private static void Isolated(Action action) => StaHost.Run(() =>
    {
        var previous=Application.Current.Resources;
        Application.Current.Resources=new ResourceDictionary();
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml",UriKind.Relative) });
        try { action(); } finally { Application.Current.Resources=previous; }
    });
}