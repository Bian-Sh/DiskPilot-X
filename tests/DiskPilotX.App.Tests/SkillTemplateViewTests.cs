using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Skills;
namespace DiskPilotX.App.Tests;
[Collection("聊天滚动控件")]
public sealed class SkillTemplateViewTests
{
    [Fact] public void 结果模板切换语言不重建数据且路径参数完整() => StaHost.Run(() =>
    {
        var old=L.GetState().LanguagePreference;
        var path=@"D:\Projects\真实工程";
        var token=SkillTextCatalog.Bind("subtitle",17,path);
        var item=new SkillUiItem { Title="{loc name}",Subtitle=token,Selectable=true,Selected=true,Path=path,Cells=[token] };
        var spec=new SkillUiSpec { Title="{loc name}",Subtitle=token,
            Localizations=new() { ["en-US"]=new() { ["name"]="Inventory",["subtitle"]="{0} items at {1}" },["zh-TW"]=new() { ["name"]="盤點",["subtitle"]="{1}：{0} 個項目" } },
            Sections=[new SkillUiSection { Kind=SkillUiSectionKind.Table,Headers=["{loc name}"],Items=[item] }] };
        L.SetLanguage("en-US");var window=new SkillWindow(spec);
        using var host=new HwndSource(new HwndSourceParameters("模板语言验收") { Width=1000,Height=780 }) { RootVisual=(Visual)window.Content };
        try
        {
            var content=(FrameworkElement)window.Content;content.Measure(new Size(1000,780));content.Arrange(new Rect(0,0,1000,780));content.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind);
            Assert.Contains(Visuals<TextBlock>(content),t=>t.Text=="17 items at "+path);
            L.SetLanguage("zh-TW");Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind);
            Assert.Contains(Visuals<TextBlock>(content),t=>t.Text==path+"：17 個項目");
            Assert.Equal("盤點",window.Title);Assert.True(item.Selected);Assert.Equal(path,item.Path);
            Assert.Equal(token,item.Cells[0]);
            var vm=(DiskPilotX.App.ViewModels.SkillWindowViewModel)window.DataContext;
            Assert.Contains("盤點",vm.BuildSelectionPayload());
            Assert.DoesNotContain("{loc",vm.BuildSelectionPayload());
        }
        finally { host.RootVisual=null;window.Close();L.SetLanguage(old); }
    });

    [Fact] public void 多选分析成功提示包含翻译后的标题而不是模板键() => StaHost.Run(() =>
    {
        var old=L.GetState().LanguagePreference;L.SetLanguage("zh-TW");
        try
        {
            var items=new[] { new SkillUiItem { Title="{loc row}",Path=@"D:\a" },new SkillUiItem { Title=SkillTextCatalog.Bind("row"),Path=@"D:\b" } };
            var tables=new Dictionary<string,Dictionary<string,string>> { ["zh-TW"]=new() { ["row"]="快取項目" } };
            var method=typeof(DiskPilotX.App.MainWindow).GetMethod("BuildBatchPrompt",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
            var parameters=method.GetParameters().Length==5
                ? new object?[] { items,false,new DiskPilotX.App.Services.AssistantAnalysisBatch(),"evidence",tables }
                : new object?[] { items,false,new DiskPilotX.App.Services.AssistantAnalysisBatch(),"evidence" };
            var prompt=(string)method.Invoke(null,parameters)!;
            Assert.Contains("快取項目",prompt);Assert.DoesNotContain("{loc",prompt);
            Assert.Contains(@"D:\a",prompt);Assert.Contains(@"D:\b",prompt);
        }
        finally { L.SetLanguage(old); }
    });


    [Fact] public void 技能统计说明支持较长英文自动换行()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"src","DiskPilotX.App","Views","SkillUiTemplates.xaml")))directory=directory.Parent;
        Assert.NotNull(directory);
        var template=System.Xml.Linq.XDocument.Load(Path.Combine(directory!.FullName,"src","DiskPilotX.App","Views","SkillUiTemplates.xaml"));
        var label=template.Descendants().Single(node=>node.Attribute("Text")?.Value=="{loc:SkillText ValueLabel}");
        Assert.Equal("Wrap",label.Attribute("TextWrapping")?.Value);
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T match)yield return match;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Visuals<T>(VisualTreeHelper.GetChild(root,i)))yield return item;
    }
}
