using System.Windows;
using System.Windows.Threading;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private DispatcherTimer? _operationToastTimer;

    internal static string? GetFileDeletionBusyMessage(bool aiBusy, bool scanning, bool cleaning) =>
        cleaning ? "正在清理文件，暂时不能再次删除，避免重复操作。等本轮清理结束后再试。"
        : scanning ? "正在扫描文件，暂时不能删除，避免扫描结果不完整。等扫描结束后再试。"
        : aiBusy ? "AI 正在查看文件，暂时不能删除，避免文件变化影响判断。等本轮回复结束后再试。"
        : null;

    private bool TryShowBusyOperationToast(string operation = "删除")
    {
        var main = DataContext as MainViewModel;
        var scanning = main?.IsScanning == true;
        var aiBusy = _chatViewModel?.IsOperationRunning == true || main?.IsCopilotBusy == true
            || _recommendationsViewModel?.IsAnalyzing == true;
        var reason = GetFileDeletionBusyMessage(aiBusy, scanning, _cleanupRunning);
        if (reason is null) return false;
        if (operation != "删除")
            reason = _cleanupRunning ? $"正在清理文件，暂时不能{operation}。等本轮清理结束后再试。"
                : scanning ? $"正在扫描文件，暂时不能{operation}。等扫描结束后再试。"
                : $"AI 正在查看文件，暂时不能{operation}，避免切换或修改文件影响判断。等本轮回复结束后再试。";
        ShowOperationToast(reason);
        return true;
    }

    private void ShowOperationToast(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowOperationToast(message));
            return;
        }
        if (_operationToastTimer is null)
        {
            _operationToastTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _operationToastTimer.Tick += (_, _) =>
            {
                _operationToastTimer.Stop();
                OperationToast.Visibility = Visibility.Collapsed;
            };
            Closed += (_, _) => _operationToastTimer.Stop();
        }
        OperationToastText.Text = message;
        OperationToast.Visibility = Visibility.Visible;
        // 重复触发从本次重新计时；覆盖层不命中鼠标且位于原模态确认下面。
        _operationToastTimer.Stop();
        _operationToastTimer.Start();
    }
}
