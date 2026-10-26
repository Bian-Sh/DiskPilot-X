#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property OutputType=WinExe
#:project ../src/DiskPilotX.App/DiskPilotX.App.csproj

using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;

// 手动后台验收入口：dotnet run --file tests/AskUserPreview.cs。窗口不抢焦点，三分钟自动关闭。
internal static class AskUserPreview
{
    [STAThread]
    private static void Main()
    {
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative)
        });
        var request = UserDecisionRequest.Parse(JsonNode.Parse("""
        {
          "title":"清理偏好确认", "question":"请确认清理方式、目标与数量", "reason":"仅用于验收结构化表单，提交不会执行清理。",
          "fields":[
            {"id":"mode","label":"清理方式","type":"single_select","is_required":true,"options":[{"id":"safe","label":"安全预览","description":"先预览再决定"},{"id":"manual","label":"逐项确认"}],"allow_custom_input":true,"custom_input_placeholder":"也可以填写其他想法","max_length":80},
            {"id":"targets","label":"清理目标","type":"multi_select","is_required":true,"min_selections":1,"options":[{"id":"cache","label":"缓存"},{"id":"logs","label":"日志"}]},
            {"id":"count","label":"保留数量","type":"number","is_required":true,"min_number":0,"max_number":10,"step":2,"default_number":4},
            {"id":"note","label":"补充说明","type":"text","is_multiline":true,"max_length":80}
          ]
        }
        """));
        var vm = new AskUserDialogViewModel(request);
        var result = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), Height = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel();
        panel.Children.Add(new AskUserCard { DataContext = vm, Margin = new Thickness(12) });
        panel.Children.Add(result);
        var window = new Window
        {
            Title = "AskUser 移植验收", Width = 282, Height = 650, ShowActivated = false,
            Background = new SolidColorBrush(Color.FromRgb(18, 27, 43)), Content = panel,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.Closed += (_, _) => vm.Cancel();
        _ = vm.Completion.ContinueWith(task => window.Dispatcher.InvokeAsync(() => result.Text = task.Result.ToJsonString(new() { WriteIndented = true })));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
        timer.Start();
        app.Run(window);
    }
}
