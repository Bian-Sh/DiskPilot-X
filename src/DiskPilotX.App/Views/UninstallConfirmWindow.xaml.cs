using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using DiskPilotX.App.Helpers;

namespace DiskPilotX.App.Views;

/// <summary>
/// 卸载类动作的强确认弹窗。
/// 与宿主动态窗口同构（同一套无系统标题栏外壳 + 危险色边框），并要求用户显式勾选「我知道后果」才放行；
/// 取消或直接关窗一律返回 false，调用方据此不触发任何破坏性动作。
/// </summary>
public sealed partial class UninstallConfirmWindow : Window, INotifyPropertyChanged
{
    private bool _acknowledged;

    public UninstallConfirmWindow(string appName, string appId, string sizeText, string installPath)
    {
        InitializeComponent();
        DataContext = this;
        AppName = appName;
        AppId = appId;
        SizeText = string.IsNullOrWhiteSpace(sizeText) ? "未统计" : sizeText;
        InstallPath = string.IsNullOrWhiteSpace(installPath) ? "Steam 未提供安装目录" : installPath;

        HostWindowChrome.Apply(this, Frame, HostTitleBar);
        HostTitleBar.CloseRequested += (_, _) => Close();
    }

    public string AppName { get; }
    public string AppId { get; }
    public string SizeText { get; }
    public string InstallPath { get; }

    public string SourceLine => "应用编号 " + AppId + " · 通过 Steam 原生卸载执行";
    public string AcknowledgeText => "我确认要卸载《" + AppName + "》，并接受上面列出的后果。";
    public string ConfirmLabel => "卸载《" + AppName + "》";

    /// <summary>用户是否勾了确认；未勾选时卸载按钮不可用。</summary>
    public bool Acknowledged
    {
        get => _acknowledged;
        set { if (_acknowledged == value) return; _acknowledged = value; OnPropertyChanged(); }
    }

    /// <summary>用户按下「卸载」才算通过。</summary>
    public bool Confirmed { get; private set; }

    /// <summary>弹出强确认；只有用户勾选并点了卸载才返回 true。</summary>
    public static bool Confirm(string appName, string appId, string? sizeText, string? installPath, Window owner)
    {
        var window = new UninstallConfirmWindow(appName, appId, sizeText, installPath) { Owner = owner };
        return window.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!Acknowledged) return;
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
