using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using NSubstitute;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views.SettingsSections;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ModelListInteractionTests
{
    [Fact]
    public void 抓手真实路由事件支持预览提交取消且删除按钮命令绑定正确() => RunSta(() =>
    {
        var saved = new AppSettings { DiscoveredModels = ["model-c", "model-a", "model-b"], ChatModelName = "model-a" };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved);
        using var clients = new ModelHttpClientFactory();
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings));
        var section = new ApiSettingsSection { DataContext = vm };
        var theme = new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) };
        section.Resources.MergedDictionaries.Add(theme);
        var modelScroll = (ScrollViewer)section.FindName("ModelOrderScroll");
        ((Grid)modelScroll.Parent).Children.Remove(modelScroll);
        section.Content = modelScroll;
        var host = new AdornerDecorator { Child = section };
        Layout(host);
        var list = (ItemsControl)section.FindName("ModelOrderList");
        var thumb = Visuals<Thumb>(list).Single(item => item.DataContext as string == "model-b");
        var row = Visuals<Border>(list).Single(item => item.Name == "ModelRow" && item.DataContext as string == "model-b");
        var remove = Visuals<Button>(list).Single(item => item.Name == "RemoveModelButton" && item.DataContext as string == "model-a");
        Assert.Equal(0, remove.Opacity);
        Assert.False(remove.IsHitTestVisible);
        var template = list.ItemTemplate;
        foreach (var property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusWithinProperty })
        {
            var trigger = template.Triggers.OfType<Trigger>().Single(item => item.SourceName == "ModelRow" && item.Property == property);
            Assert.Contains(trigger.Setters.OfType<Setter>(), item => item.TargetName == "RemoveModelButton" && item.Property == UIElement.OpacityProperty && Equals(item.Value, 1d));
            Assert.Contains(trigger.Setters.OfType<Setter>(), item => item.TargetName == "RemoveModelButton" && item.Property == UIElement.IsHitTestVisibleProperty && Equals(item.Value, true));
        }
        Assert.Same(vm.RemoveModelCommand, remove.Command);
        Assert.Equal("model-a", remove.CommandParameter);
        thumb.RaiseEvent(new DragStartedEventArgs(12, 14) { RoutedEvent = Thumb.DragStartedEvent });
        thumb.RaiseEvent(new DragDeltaEventArgs(0, -90) { RoutedEvent = Thumb.DragDeltaEvent });
        Assert.Equal(0.55, row.Opacity);
        Assert.Equal(new[] { "model-c", "model-a", "model-b" }, saved.DiscoveredModels);
        settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
        Assert.NotEmpty(AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!);
        thumb.RaiseEvent(new DragCompletedEventArgs(0, -90, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal(new[] { "model-b", "model-c", "model-a" }, saved.DiscoveredModels);
        settings.Received(1).SaveSettings(saved);
        Assert.Equal(1, row.Opacity);
        Layout(host);
        thumb = Visuals<Thumb>(list).Single(item => item.DataContext as string == "model-b");
        thumb.RaiseEvent(new DragStartedEventArgs(12, 14) { RoutedEvent = Thumb.DragStartedEvent });
        thumb.RaiseEvent(new DragDeltaEventArgs(0, 90) { RoutedEvent = Thumb.DragDeltaEvent });
        thumb.RaiseEvent(new DragCompletedEventArgs(0, 90, true) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal(new[] { "model-b", "model-c", "model-a" }, saved.DiscoveredModels);
        settings.Received(1).SaveSettings(saved);
        remove = Visuals<Button>(list).Single(item => item.Name == "RemoveModelButton" && item.DataContext as string == "model-a");
        remove.Command.Execute(remove.CommandParameter);
        Assert.Equal(new[] { "model-b", "model-c" }, saved.DiscoveredModels);
        Assert.Null(saved.ChatModelName);
    });

    private static void Layout(FrameworkElement element)
    {
        for (var i = 0; i < 3; i++)
        {
            element.Measure(new Size(570, 1400)); element.Arrange(new Rect(0, 0, 570, 1400)); element.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "模型列表控件测试未在时限内完成。");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}