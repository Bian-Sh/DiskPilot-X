using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Views.SettingsSections;
using DiskPilotX.Core.Enums;

namespace DiskPilotX.App.Tests;

[Collection("本地化状态")]
public sealed class CleanupModeSettingsTests
{
    [Fact]
    public void 清理方式问号与下拉选项随语言即时更新并保留选中值() => StaHost.Run(() =>
    {
        var original = L.GetState().LanguagePreference;
        try
        {
            L.SetLanguage("zh-CN");
            var section = new GeneralSettingsSection();
            using var source = new HwndSource(new HwndSourceParameters("清理方式本地化测试")
                { Width = 650, Height = 260, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = section };
            section.Measure(new Size(650, 260)); section.Arrange(new Rect(0, 0, 650, 260)); section.UpdateLayout(); Pump();
            var input = Assert.IsType<ComboBox>(section.FindName("CleanupModeInput"));
            var help = Assert.IsType<TextBlock>(section.FindName("CleanupModeHelp"));
            Assert.Equal("?", help.Text);
            input.SelectedValue = DeletionMode.PermanentDelete;
            foreach (var (language, label, recycle, permanent) in new[] {
                ("zh-CN", "清理方式", "移入回收站", "永久删除"),
                ("zh-TW", "清理方式", "移至資源回收筒", "永久刪除"),
                ("en-US", "Cleanup mode", "Move to Recycle Bin", "Permanently Delete") })
            {
                L.SetLanguage(language); Pump();
                Assert.Equal(label, L.Text("SettingsDefaultCleanupModeLabel"));
                Assert.Equal(recycle, Assert.IsType<ComboBoxItem>(input.Items[0]).Content);
                Assert.Equal(permanent, Assert.IsType<ComboBoxItem>(input.Items[1]).Content);
                Assert.Equal(DeletionMode.PermanentDelete, input.SelectedValue);
                var tooltip = Assert.IsType<ToolTip>(help.ToolTip);
                var hint = Assert.IsType<TextBlock>(tooltip.Content).Text;
                Assert.Contains(recycle, hint); Assert.Contains(permanent, hint);
                Assert.Equal(150, ToolTipService.GetInitialShowDelay(help));
                Assert.Equal(12000, ToolTipService.GetShowDuration(help));
            }
        }
        finally { L.SetLanguage(original); }
    });

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    [InlineData("en-US")]
    public void 清理方式提示左对齐且两行文字不被裁剪(string language) => StaHost.Run(() =>
    {
        var original = L.GetState().LanguagePreference;
        ToolTip? tooltip = null;
        try
        {
            L.SetLanguage(language);
            var section = new GeneralSettingsSection();
            using var source = new HwndSource(new HwndSourceParameters("清理方式提示布局测试")
                { Width = 650, Height = 260, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = section };
            section.Measure(new Size(650, 260)); section.Arrange(new Rect(0, 0, 650, 260)); section.UpdateLayout(); Pump();
            var help = Assert.IsType<TextBlock>(section.FindName("CleanupModeHelp"));
            tooltip = Assert.IsType<ToolTip>(help.ToolTip);
            var text = Assert.IsType<TextBlock>(tooltip.Content);
            Assert.Equal(TextAlignment.Left, text.TextAlignment);
            Assert.Equal(HorizontalAlignment.Left, tooltip.HorizontalContentAlignment);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.Equal(TextTrimming.None, text.TextTrimming);
            Assert.Equal(2, text.Text.Split('\n').Length);
            tooltip.PlacementTarget = help;
            tooltip.IsOpen = true; Pump(); tooltip.UpdateLayout();
            var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
            var lines = text.Text.Split('\n').Select(line => new FormattedText(line,
                CultureInfo.GetCultureInfo(language), FlowDirection.LeftToRight, typeface,
                text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip)).ToArray();
            var requiredWidth = lines.Max(line => line.WidthIncludingTrailingWhitespace);
            Assert.True(text.ActualWidth + 1 >= requiredWidth, $"文字需要 {requiredWidth}，实际宽度 {text.ActualWidth}");
            Assert.InRange(text.ActualHeight, lines.Sum(line => line.Height) - 2, lines.Sum(line => line.Height) + 2);
            Assert.True(tooltip.ActualWidth >= text.ActualWidth + tooltip.Padding.Left + tooltip.Padding.Right);
            // 窄视口允许换行，不能靠裁剪或省略文字适配。
            text.Measure(new Size(240, double.PositiveInfinity));
            Assert.True(text.DesiredSize.Width <= 240);
            Assert.True(text.DesiredSize.Height > lines.Sum(line => line.Height));
        }
        finally
        {
            if (tooltip is not null) tooltip.IsOpen = false;
            L.SetLanguage(original);
        }
    });

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
