using System.Windows;
using DiskPilotX.App.Helpers;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Views;

/// <summary>
/// 按需视图的独立窗口：完整呈现 skill 声明的视图，支持行内动作（如卸载）与多选回传。
/// 只做呈现与收集，不扫描、不删除。
/// 外壳（无系统标题栏 + 宿主标题栏 + 圆角裁切 + 磨砂材质）复用宿主主窗口的同一套实现，
/// 因此不会出现「系统自带标题栏」这种割裂感。
/// </summary>
public sealed partial class SkillWindow : Window
{
    private readonly SkillWindowViewModel viewModel;

    public SkillWindow(SkillUiSpec spec)
    {
        InitializeComponent();
        SetResourceReference(ForegroundProperty, "VP.TextPrimaryBrush");
        Title = SkillTextExtension.Resolve(spec.Title, spec.Localizations);
        viewModel = new SkillWindowViewModel(spec);
        DataContext = viewModel;
        var subscription = L.Subscribe(_ =>
        {
            Title = SkillTextExtension.Resolve(spec.Title, spec.Localizations);
            viewModel.RefreshLocalization();
        });
        Closed += (_, _) => { subscription.Dispose(); viewModel.Dispose(); };
        viewModel.CloseRequested += Close;
        viewModel.ContinueRequested += items => ContinueRequested?.Invoke(spec, items);
        viewModel.PreviewRequested += items => PreviewRequested?.Invoke(spec, items);

        // 与宿主主窗口同构：无系统标题栏，交互交给宿主的 WindowTitleBar。
        HostWindowChrome.Apply(this, Frame, HostTitleBar);
        HostTitleBar.CloseRequested += (_, _) => Close();

        // 首屏会被内容里的勾选框触发 BringIntoView，滚动容器顺势把内容推下去，
        // 结果是顶部的统计行直接看不见。用户真正操作之前一律压住这个请求。
        ContentScroll.PreviewMouseDown += (_, _) => _allowAutoScroll = true;
        ContentScroll.PreviewKeyDown += (_, _) => _allowAutoScroll = true;

        // 行内动作按钮（卸载等）由本方法统一接管。
        AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new System.Windows.RoutedEventHandler(OnItemActionClick));
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() => ContentScroll.ScrollToTop()));
        ContentRendered += (_, _) => ContentScroll.ScrollToTop();

        // 后台窗口在最大化状态下不能被再次 Show；收起时还原以保证可重开。
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible && WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        };
    }

    /// <summary>关掉首屏自动滚动后，用户一旦开始操作就恢复正常行为。</summary>
    private bool _allowAutoScroll;

    private void ContentScroll_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (!_allowAutoScroll) e.Handled = true;
    }

    /// <summary>用户请求把选中条目回传给助手继续处理（如进入清理分析）。</summary>
    public event Action<SkillUiSpec, IReadOnlyList<SkillUiItem>>? ContinueRequested;
    /// <summary>用户请求预览选中条目的清理候选。</summary>
    public event Action<SkillUiSpec, IReadOnlyList<SkillUiItem>>? PreviewRequested;

    /// <summary>用户点击了条目上的行内动作（如游戏卡的「卸载」）。</summary>
    public event Action<SkillUiSpec, SkillUiItem, SkillUiItemAction>? ItemActionRequested;

    /// <summary>
    /// 行内动作按钮写在共享模板里，拿不到窗口的命令，因此统一靠 Button.Click 冒泡到窗口：
    /// 命中的是带 Action 的条目才接管，底栏的视图动作按钮 DataContext 是 SkillUiAction，不会走这条分支。
    /// </summary>
    private void OnItemActionClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (e.OriginalSource is not System.Windows.Controls.Button button) return;
        if (button.DataContext is not SkillUiItem item || item.Action is not { } action) return;

        e.Handled = true;
        ItemActionRequested?.Invoke(viewModel.Spec, item, action);
    }

    /// <summary>窗口里被勾选的条目。</summary>
    public IReadOnlyList<SkillUiItem> SelectedItems => viewModel.SelectedItems;

    /// <summary>把勾选结果整理成可回传给助手的文本。</summary>
    public string BuildSelectionPayload() => viewModel.BuildSelectionPayload();
}
