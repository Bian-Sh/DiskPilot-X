using System.Windows;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Services;
using Border = System.Windows.Controls.Border;

namespace DiskPilotX.App.Helpers;

/// <summary>
/// 把宿主主窗口的「外壳」整套搬到动态弹窗上：
/// 去掉系统标题栏，改挂宿主的 <see cref="WindowTitleBar"/>（拖动 / 最小化 / 最大化 / 关闭全部沿用），
/// 窗口圆角与裁切由 <see cref="WindowChromeHelper"/> 统一处理，
/// 并订阅 <see cref="ThemeManager.ThemeChanged"/>，让磨砂 / 透明材质跟随主题实时刷新。
/// </summary>
internal sealed class HostWindowChrome
{
    private readonly Window _window;
    private readonly ThemeManager? _theme;

    private HostWindowChrome(Window window, ThemeManager? theme)
    {
        _window = window;
        _theme = theme;
    }

    /// <summary>让窗口外壳与宿主保持一致；返回后仍需在布局就绪后自行处理滚动等细节。</summary>
    public static void Apply(Window window, Border frame, WindowTitleBar titleBar)
    {
        WindowChromeHelper.Apply(window, frame, titleBar);
        _ = new HostWindowChrome(window, App.Services?.GetService(typeof(ThemeManager)) as ThemeManager).Attach();
    }

    private HostWindowChrome Attach()
    {
        _window.SourceInitialized += OnSourceInitialized;
        _window.Closed += OnClosed;
        if (_theme is not null) _theme.ThemeChanged += OnThemeChanged;
        return this;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // 明暗模式与磨砂材质都由 ThemeManager.ApplyGlassBackdrop 按当前主题统一下发。
        _theme?.ApplyGlassBackdrop(_window);
    }

    /// <summary>主题切换后磨砂材质要重新下发，否则新建的窗口会停在旧材质上。</summary>
    private void OnThemeChanged() =>
        _window.Dispatcher.BeginInvoke(new Action(() => _theme?.ApplyGlassBackdrop(_window)));

    private void OnClosed(object? sender, EventArgs e)
    {
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Closed -= OnClosed;
        if (_theme is not null) _theme.ThemeChanged -= OnThemeChanged;
    }
}
