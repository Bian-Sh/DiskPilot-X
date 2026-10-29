using System.Text.Json;
using System.Runtime.InteropServices;
using DiskPilotX.App.Helpers;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Views;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models.Theme;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private SkillUiSpec? polishSpec;

    /// <summary>只读显示夹具：沿用真实 JSON 契约和聊天入口，不扫描或修改用户数据。</summary>
    internal object AcceptanceSkillUiPolish(string? mode)
    {
        if(!AcceptanceAutomationServer.IsEnabled() || _chatViewModel is null) throw new InvalidOperationException("验收入口未启用。");
        if(mode is "keep-glass-on" or "keep-glass-off")
        {
            if(_settingsViewModel is null) throw new InvalidOperationException("设置模型不可用。");
            _settingsViewModel.KeepGlassWhenInactive=mode=="keep-glass-on";
        }
        else if(mode is "glass-low" or "glass-high" or "glass-mid" or "glass-off" or "glass-dark" or "glass-light")
        {
            if(_settingsViewModel is null) throw new InvalidOperationException("设置模型不可用。");
            if(mode is "glass-dark" or "glass-light") _settingsViewModel.ApplyThemePresetCommand.Execute(mode=="glass-dark" ? ThemeProfile.VisionProDark : ThemeProfile.VisionProLight);
            _settingsViewModel.GlassEnabled=mode!="glass-off";
            _settingsViewModel.GlassOpacityPercent=mode=="glass-high" ? 85 : mode=="glass-mid" ? 50 : 0;
        }
        else if(mode=="settings") ShowSettingsPage();
        else if(mode=="close-settings") HideSettingsPage();
        else if(mode=="chat")
        {
            using var json=JsonDocument.Parse("""
            {"title":"空间占用盘点","subtitle":"只读界面夹具，不是当前磁盘扫描结果","entry_title":"查看项目占用详情","entry_summary":"三项真实数值比例的显示验证","localizations":{"zh-CN":{"chart":"空间占用对比","note":"条形应为 100%、50%、12.5%","a":"项目甲","b":"项目乙","c":"项目丙"},"en-US":{"chart":"Space usage comparison","note":"Expected bars: 100%, 50%, 12.5%","a":"Project A","b":"Project B","c":"Project C"}},"sections":[
              {"kind":"bar_chart","title":"{loc chart}","subtitle":"{loc note}","items":[
                {"title":"{loc a}","metric":8,"value":"8 GB"},{"title":"{loc b}","metric":4,"value":"4 GB"},{"title":"{loc c}","metric":1,"value":"1 GB"}]},
              {"kind":"table","title":"项目明细","headers":["项目","所在盘","缓存构成"],"items":[
                {"title":"项目甲","selectable":true,"size_bytes":8589934592,"cells":["项目甲","E:/","Library 8 GB"]}]}],
              "actions":[{"kind":"close","label":"关闭"}]}
            """);
            polishSpec=SkillUiSpecContract.Parse(json.RootElement);
            var message=new ChatMessage { Sender=ChatSender.Assistant,Text="正文锐利度对照：项目盘点与空间占用。下面的入口标题、摘要和条形图应同样清晰；只有图表内联，表格在完整窗口中查看。" };
            message.Segments.Add(new ChatSegment(ChatSegmentKind.Text) { Text=message.Text,Status=ChatSegmentStatus.Completed });
            _chatViewModel.Messages.Add(message);
            _chatViewModel.AddView(polishSpec);
            ChatPanelViewport.Visibility=Visibility.Visible;
            ClearChatPanelSlideTransform();
            ChatPanelContainer.Width=double.NaN;
            ChatPanelContainer.HorizontalAlignment=HorizontalAlignment.Stretch;
            ChatPanelColumn.Width=new GridLength(620);
        }
        else if(mode=="view")
        {
            if(polishSpec is null) throw new InvalidOperationException("请先建立聊天夹具。");
            _chatViewModel.OpenSkillWindow(polishSpec);
        }
        else if(mode=="task") return AcceptanceLocalizationFixture("task");
        return AcceptanceSkillUiPolishState();
    }

    internal object AcceptanceSkillUiPolishState()
    {
        if(!AcceptanceAutomationServer.IsEnabled()) throw new InvalidOperationException("验收入口未启用。");
        UpdateLayout();
        var texts=Walk(ChatPanelContainer).OfType<TextBlock>().Where(t=>t.Text=="查看项目占用详情").Select(t=>new
        {
            text=t.Text,effectAncestors=EffectAncestors(t),rendering=TextOptions.GetTextRenderingMode(t).ToString(),
            formatting=TextOptions.GetTextFormattingMode(t).ToString(),hitTest=t.IsHitTestVisible
        }).ToArray();
        object[] Bars(DependencyObject parent)=>Walk(parent).OfType<Border>().Where(b=>Equals(b.Tag,"SkillBarFill") && b.IsVisible)
            .Select(b=>(object)new { width=b.ActualWidth,metric=(b.DataContext as SkillUiItem)?.Metric }).ToArray();
        var windows=Application.Current.Windows.OfType<Window>().Where(w=>w.IsVisible && (w is SkillWindow || w is SkillTaskWindow)).Select(w=>new
        {
            hwnd=new WindowInteropHelper(w).Handle.ToInt64(),w.Title,type=w.GetType().Name,
            background=(w.Background as SolidColorBrush)?.Color.ToString(),
            glass=ThemeManager.IsWindowGlassActive(w),opacity=w.Opacity,isActive=w.IsActive,
            keepGlass=AcrylicHelper.IsKeepingGlassWhenInactive(w),
            tint=((w.Content as Border)?.Background as SolidColorBrush)?.Color.ToString(),
            tableSurface=(w.Resources["VP.SkillWindowSurfaceBrush"] as SolidColorBrush)?.Color.ToString(),
            selected=(w.DataContext as SkillWindowViewModel)?.SelectedItems.Count,
            inputs=Walk(w).OfType<TextBox>().Where(t=>!t.IsReadOnly).Select(t=>t.Text).ToArray(),
            branding=Walk(w).OfType<WindowTitleBar>().FirstOrDefault()?.BrandingVisibility.ToString(),
            bars=Bars(w),width=w.ActualWidth,height=w.ActualHeight
        }).ToArray();
        var theme=(App.Services?.GetService(typeof(ThemeManager)) as ThemeManager)?.CurrentProfile;
        return new { mainHwnd=new WindowInteropHelper(this).Handle.ToInt64(),mainActive=IsActive,foregroundHwnd=GlassForegroundWindow().ToInt64(),
            theme=new { theme?.GlassEnabled,theme?.KeepGlassWhenInactive,theme?.GlassOpacity },texts,chatBars=Bars(ChatPanelContainer),windows };
    }
    [DllImport("user32.dll",EntryPoint="GetForegroundWindow")] private static extern IntPtr GlassForegroundWindow();
    private static int EffectAncestors(DependencyObject node)
    {
        var count=0;
        for(DependencyObject? current=node;current is not null;current=VisualTreeHelper.GetParent(current))
            if(current is UIElement { Effect:not null }) count++;
        return count;
    }
}