using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DiskPilotX.App.Views;

/// <summary>
/// 设置内容控件：由宿主 <see cref="SettingsWindow"/> 承载。左侧分区导航 + 右侧吸附滚动，
/// 窗口外壳（标题栏 / 圆角 / 磨砂材质）全部交给 SettingsWindow，本控件只负责内容。
/// </summary>
public partial class SettingsPage : UserControl
{
    private Button[]? _navButtons;

    public SettingsPage()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateBottomScrollSpacer();
                UpdateActiveSection();
            }), DispatcherPriority.Loaded);
        };
    }

    public void SetUpdateViewModel(DiskPilotX.App.ViewModels.UpdateViewModel vm) => UpdateSectionControl.DataContext = vm;

    public void NavigateToUpdates()
    {
        // 等设置页完成布局后再定位，确保首次打开也能准确落到更新分区。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateBottomScrollSpacer();
            SnapToSection(UpdateSectionControl);
            SetActiveNavButton(UpdateSectionControl.Name);
        }), DispatcherPriority.Loaded);
    }

    private IReadOnlyList<Button> NavButtons => _navButtons ??= [GeneralNavButton, ThemeNavButton, ApiNavButton, WhitelistNavButton, UpdateNavButton, DisclaimerNavButton];

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sectionName })
            return;

        if (FindName(sectionName) is not FrameworkElement target)
            return;

        SnapToSection(target);
        SetActiveNavButton(sectionName);
    }

    private void SettingsContentPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBottomScrollSpacer();
    }

    private void SettingsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
        {
            UpdateBottomScrollSpacer();
            UpdateActiveSection();
        }
    }

    private void UpdateBottomScrollSpacer()
    {
        if (!SettingsScrollViewer.IsLoaded || SettingsScrollViewer.ViewportHeight <= 0)
            return;

        var themeOffset = GetSectionOffset(DisclaimerSectionControl);
        var contentHeightWithoutSpacer = SettingsContentPanel.ActualHeight - BottomScrollSpacer.ActualHeight;
        var requiredContentHeight = themeOffset + SettingsScrollViewer.ViewportHeight;
        BottomScrollSpacer.Height = Math.Max(0, requiredContentHeight - contentHeightWithoutSpacer);
    }

    private void SnapToSection(FrameworkElement section)
    {
        var targetY = GetSectionOffset(section);
        SettingsScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetY, 0, SettingsScrollViewer.ScrollableHeight));
    }

    private void UpdateActiveSection()
    {
        const double activationOffset = 64;
        var scrollOffset = SettingsScrollViewer.VerticalOffset + activationOffset;
        FrameworkElement activeSection = GeneralSectionControl;

        foreach (var section in new FrameworkElement[] { GeneralSectionControl, ThemeSectionControl, ApiSectionControl, WhitelistSectionControl, UpdateSectionControl, DisclaimerSectionControl })
        {
            if (GetSectionOffset(section) <= scrollOffset)
            {
                activeSection = section;
            }
        }

        SetActiveNavButton(activeSection.Name);
    }

    private double GetSectionOffset(FrameworkElement section)
    {
        return section.TransformToAncestor(SettingsContentPanel).Transform(new Point(0, 0)).Y;
    }

    private void SetActiveNavButton(string sectionName)
    {
        foreach (var button in NavButtons)
        {
            var isActive = string.Equals(button.Tag as string, sectionName, StringComparison.Ordinal);
            if (isActive) button.SetResourceReference(Control.BackgroundProperty, "VP.SurfaceHoverBrush");
            else button.Background = Brushes.Transparent;
            button.SetResourceReference(Control.ForegroundProperty, isActive ? "VP.TextPrimaryBrush" : "VP.TextSecondaryBrush");
            button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Medium;
        }
    }

    public void ReloadSettingsForOpen()
    {
        if (DataContext is not ViewModels.SettingsViewModel vm)
            return;

        vm.HideSaveToast();

        vm.LoadSettings();
    }
}
