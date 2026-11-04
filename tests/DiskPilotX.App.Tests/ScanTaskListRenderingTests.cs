using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;
namespace DiskPilotX.App.Tests;
[Collection("聊天滚动控件")]
public sealed class ScanTaskListRenderingTests
{
    [Fact] public void 路径解析不静默丢弃第七个范围()
    {
        var args=new JsonObject { ["paths"]=new JsonArray(Enumerable.Range(0,9).Select(i=>JsonValue.Create(@"D:\样本"+i)).ToArray()) };
        var method=typeof(DiskPilotX.App.MainWindow).GetMethod("ReadPaths",BindingFlags.Static|BindingFlags.NonPublic)!;
        Assert.Equal(9,Assert.IsType<List<string>>(method.Invoke(null,[args])).Count);
    }
    [Fact] public void 新用户消息后的扫描不能附加到上一轮气泡()
    {
        var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(new AppSettings());
        var router=Substitute.For<IAiSkillRouter>();router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        var vm=new ChatViewModel(new DiskAssistantService(_=>throw new InvalidOperationException(),[],new AssistantWorkspace(),settings),settings,router);
        var old=new ChatMessage{Sender=DiskPilotX.Core.Enums.ChatSender.Assistant,Text="旧回复"};vm.Messages.Add(old);
        vm.Messages.Add(new(){Sender=DiskPilotX.Core.Enums.ChatSender.User,Text="新的扫描请求"});
        vm.AddScanSnapshot(new(Guid.NewGuid(),Guid.NewGuid(),@"D:\新范围",DateTimeOffset.Now,0,0,0));
        Assert.Empty(old.ScanSnapshots);Assert.Single(vm.Messages.Last().ScanSnapshots);
        Assert.Equal(DiskPilotX.Core.Enums.ChatSender.Assistant,vm.Messages.Last().Sender);
    }
    [Fact] public void 扫描行自然高度至上限并且失效后没有地图按钮() => StaHost.Run(() =>
    {
        var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(new AppSettings());
        var router=Substitute.For<IAiSkillRouter>();router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        var vm=new ChatViewModel(new DiskAssistantService(_=>throw new InvalidOperationException(),[],new AssistantWorkspace(),settings),settings,router);
        var panel=new ChatPanel();panel.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml",UriKind.Relative)});
        using var source=new HwndSource(new HwndSourceParameters("扫描列表回归"){Width=450,Height=700,WindowStyle=unchecked((int)0x80000000)}){RootVisual=panel};
        panel.SetViewModel(vm);var message=new ChatMessage{Sender=DiskPilotX.Core.Enums.ChatSender.Assistant,Text="分批扫描"};vm.Messages.Add(message);
        ScanSnapshotReference Entry(int n)=>new(Guid.NewGuid(),Guid.NewGuid(),@"D:\样本"+n,DateTimeOffset.Now,1,1,0);
        var first=Entry(0);first.SetState("available");message.ScanSnapshots.Add(first);Layout(panel);
        var list=Assert.Single(Visuals<ListBox>(panel).Where(c=>c.Name=="ScanSnapshotList"));var one=list.ActualHeight;
        Assert.InRange(one,1,100);Assert.Single(Visuals<Button>(list).Where(b=>b.IsVisible));
        first.SetState("released");Layout(panel);Assert.Empty(Visuals<Button>(list).Where(b=>b.IsVisible));
        for(var i=1;i<20;i++){var entry=Entry(i);entry.SetState("pending");message.ScanSnapshots.Add(entry);}Layout(panel);
        Assert.InRange(list.ActualHeight,one+1,240);Assert.Equal(20,list.Items.Count);
        Assert.Contains(Visuals<ScrollViewer>(list),scroll=>scroll.ScrollableHeight>0);
    });
    private static void Layout(ChatPanel panel)
    {
        panel.Measure(new Size(450,700));panel.Arrange(new Rect(0,0,450,700));panel.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);panel.UpdateLayout();
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject p) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(p);i++){var c=VisualTreeHelper.GetChild(p,i);if(c is T t)yield return t;foreach(var t2 in Visuals<T>(c))yield return t2;}}
}
