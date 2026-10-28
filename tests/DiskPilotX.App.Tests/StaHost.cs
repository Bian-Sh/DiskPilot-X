using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DiskPilotX.App.Tests;

/// <summary>
/// 共享的 STA 宿主：Application 是 AppDomain 级单例，同一测试进程里只能 new 一次，
/// 第二次必然抛 InvalidOperationException。所以凡是要真实渲染 WPF 控件的测试，
/// 统一把动作封送到这一个 STA 线程上执行，而不是各自起线程各建一个 Application。
/// </summary>
internal static class StaHost
{
    private static readonly object Gate = new();
    private static Application? _application;
    private static Dispatcher? _dispatcher;

    public static void Run(Action action)
    {
        var dispatcher = EnsureStarted();
        Exception? error = null;
        dispatcher.Invoke(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static Dispatcher EnsureStarted()
    {
        lock (Gate)
        {
            if (_dispatcher is not null) return _dispatcher;
            using var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // 主题里有一部分静态资源挂在应用级资源上，必须在构造面板之前备好。
                application.Resources["VP.AccentButton"] = new Style(typeof(Button));
                _application = application;
                _dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            { IsBackground = true, Name = "WPF 测试宿主" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("WPF 测试宿主线程未能在时限内启动。");
            return _dispatcher!;
        }
    }
}
