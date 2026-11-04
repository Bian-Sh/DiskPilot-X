using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views.SettingsSections;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ModelListScrollTests
{
    [Theory]
    [InlineData(48, 0, 120)]
    [InlineData(48, 1, -120)]
    [InlineData(48, 0.5, 120)]
    [InlineData(48, 0.5, -120)]
    [InlineData(1, 0, 120)]
    [InlineData(1, 0, -120)]
    public void 模型列表复用更新说明的边界滚轮路由(int count, double position, int delta) => StaHost.Run(() =>
    {
        var saved = new AppSettings { DiscoveredModels = Enumerable.Range(0, count).Select(i => $"模型{i}").ToList() };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved);
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(new ModelHttpClientFactory())), new ThemeManager(settings));
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) });
        var section = new ApiSettingsSection { DataContext = vm };
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 500 }); content.Children.Add(section); content.Children.Add(new Border { Height = 900 });
        var outer = new ScrollViewer { Content = content, Width = 900, Height = 400 };
        using var source = new HwndSource(new HwndSourceParameters("模型列表滚轮回归") { Width = 900, Height = 400 }) { RootVisual = outer };
        void Layout() { outer.Measure(new Size(900,400)); outer.Arrange(new Rect(0,0,900,400)); outer.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle); }
        Layout(); var inner = (ScrollViewer)section.FindName("ModelOrderScroll");
        if (count > 1) Assert.True(inner.ScrollableHeight > 0); else Assert.Equal(0, inner.ScrollableHeight);
        inner.ScrollToVerticalOffset(position * inner.ScrollableHeight); outer.ScrollToVerticalOffset(400); Layout();
        var before = inner.VerticalOffset; var outerBefore = outer.VerticalOffset;
        var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        ((UIElement)inner.Content).RaiseEvent(e);
        if (!e.Handled) ((UIElement)inner.Content).RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent });
        Layout();
        if (position == 0.5) { Assert.True(delta > 0 ? inner.VerticalOffset < before : inner.VerticalOffset > before); Assert.Equal(outerBefore, outer.VerticalOffset); }
        else { Assert.Equal(before, inner.VerticalOffset); Assert.True(delta > 0 ? outer.VerticalOffset < outerBefore : outer.VerticalOffset > outerBefore); }
    });
}
