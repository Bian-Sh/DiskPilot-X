using System.Windows;
using System.Windows.Controls;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Services.Scanning;

namespace DiskPilotX.App.Tests;

public class ScanDecisionFeedbackTests
{
    [Fact]
    public void 决策反馈显示能力理由和未知而非虚假ETA() => StaHost.Run(()=>
    {
        var prior=L.CurrentLanguageName;
        try
        {
            L.SetLanguage("zh-CN");
            var cap=new ScanVolumeCapability("hash","NTFS",true,false,"volume_open_failed","卷访问失败",5);
            var execution=new ScanExecutionInfo("D:/目标",ScanMethod.DirectoryTraversal,ScanExecutionStatus.Running)
            {Decision=new("adaptive-v1",ScanMethod.DirectoryTraversal,"volume_open_failed","已提权但打开卷失败，Win32错误5；使用目录遍历。",cap)};
            var text=ScanProgressFormatter.FormatDecision(execution);
            Assert.Contains("已提权",text);Assert.Contains("NTFS",text);Assert.Contains("错误5",text);Assert.Contains("暂无可靠",text);
            Assert.DoesNotContain("0.0–0.0",text);
        }
        finally{L.SetLanguage(prior);}
    });
    [Fact]
    public void 遮罩原因换行且计数更新不擦掉解释() => StaHost.Run(()=>
    {
        var style=new Style(typeof(Button));style.Seal();Application.Current.Resources["VP.Button"]=style;
        var view=new TreemapView();view.SetScanningState(true,"正在扫描","已发现40项","普通遍历：索引访问失败，错误5。\n预计成本未知。");
        var block=(TextBlock)view.FindName("ScanDecisionText");
        Assert.Equal(TextWrapping.Wrap,block.TextWrapping);Assert.Contains("错误5",block.Text);Assert.True(block.MaxWidth<=620);
    });
    [Fact]
    public void 候选成本区间来源明确且最终路线不是计划路线() => StaHost.Run(()=>
    {
        var prior=L.CurrentLanguageName;
        try
        {
            L.SetLanguage("zh-CN");var cap=new ScanVolumeCapability("hash","NTFS",true,true,"ok","可用");
            var execution=new ScanExecutionInfo("D:/目标",ScanMethod.DirectoryTraversal,ScanExecutionStatus.Running)
            {Decision=new("adaptive-v1",ScanMethod.Mft,"mft_fallback","MFT失败，已回退遍历。",cap,new(20000,40000,3,"历史"),new(5000,10000,2,"阶段历史"))};
            var text=ScanProgressFormatter.FormatDecision(execution);Assert.StartsWith("普通目录遍历",text);Assert.Contains("20.0",text);Assert.Contains("近期实测",text);
        }
        finally{L.SetLanguage(prior);}
    });
}
