using System.Windows;
using DiskPilotX.App.Helpers;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Views;

/// <summary>
/// 设置的独立弹窗。外壳（无系统标题栏 + 宿主标题栏 + 圆角裁切 + 磨砂材质）复用宿主主窗口的同一套实现，
/// 因此配色与材质跟宿主保持一致。非模态：改主题时主窗口可实时预览。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        SetResourceReference(ForegroundProperty, "VP.TextPrimaryBrush");
        HostWindowChrome.Apply(this, Frame, HostTitleBar);
        HostTitleBar.CloseRequested += (_, _) => Close();
    }

    /// <summary>承载设置内容的控件；宿主与验收入口通过它访问分区导航与滚动。</summary>
    public SettingsPage Settings => SettingsControl;

    public void SetViewModels(SettingsViewModel? settings, UpdateViewModel? update)
    {
        if (settings is not null) SettingsControl.DataContext = settings;
        if (update is not null) SettingsControl.SetUpdateViewModel(update);
    }

    public void ReloadSettingsForOpen() => SettingsControl.ReloadSettingsForOpen();
}
