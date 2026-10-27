using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Serilog;
using System.Windows;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Diagnostics;

internal sealed class AcceptanceAutomationServer : IDisposable
{
    private readonly MainWindow _window;
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;
    private Task? _serverTask;

    private AcceptanceAutomationServer(MainWindow window, int port)
    {
        _window = window;
        _port = port;
    }

    public static AcceptanceAutomationServer? StartIfEnabled(MainWindow window)
    {
        if (!IsEnabled())
            return null;

        var server = new AcceptanceAutomationServer(window, GetPort());
        server._serverTask = Task.Run(server.RunAsync);
        return server;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Best effort shutdown for acceptance-only diagnostics.
        }

        _cts.Dispose();
    }

    internal static bool IsEnabled()
    {
        var value = DiskPilotX.Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_PIPE")
            ?? DiskPilotX.Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SERVER");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetPort()
    {
        var configured = DiskPilotX.Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_PORT");
        return int.TryParse(configured, out var port) && port is > 0 and < 65536
            ? port
            : 39187;
    }

    private async Task RunAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                await using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };

                var requestJson = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                var responseJson = await HandleRequestAsync(requestJson).ConfigureAwait(false);
                await writer.WriteLineAsync(responseJson.AsMemory(), _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Acceptance automation server request failed");
            }
        }
    }

    private async Task<string> HandleRequestAsync(string? requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
            return JsonSerializer.Serialize(new AcceptanceResponse(false, "empty request", null));

        try
        {
            var request = JsonSerializer.Deserialize<AcceptanceRequest>(requestJson, JsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.Command))
                return JsonSerializer.Serialize(new AcceptanceResponse(false, "missing command", null), JsonOptions);

            var payload = await _window.Dispatcher.InvokeAsync(() => Execute(request)).Task.ConfigureAwait(false);
            return JsonSerializer.Serialize(new AcceptanceResponse(true, null, payload), JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new AcceptanceResponse(false, ex.Message, null), JsonOptions);
        }
    }

    private object? Execute(AcceptanceRequest request)
    {
        return request.Command.Trim().ToLowerInvariant() switch
        {
            "state" => _window.GetAcceptanceState(),
            "treemap_pointer" => _window.AcceptanceTreemapPointer(request.X, request.Y, request.Text),
            "treemap_wheel" => _window.AcceptanceTreemapWheel(request.X, request.Y, request.Delta),
            "ask_user" => _window.AcceptanceAskUser(request.Text),
            "show_skill" => _window.AcceptanceShowSkill(request.Text),
            "capture" => _window.AcceptanceCapture(request.Path,request.Text),
            "copilot_width" => _window.AcceptanceSetCopilotWidth(request.X ?? 360),
            "theme" => _window.AcceptanceSetTheme(request.Text),
            "scan" => ExecuteScan(),
            "navigate" => ExecuteNavigate(request.Path),
            "back" => ExecuteBack(),
            "forward" => ExecuteForward(),
            "up" => ExecuteUp(),
            "edit" => ExecuteEditMode(),
            "blur" => ExecuteBlur(),
            "click_coord" => ExecuteClickCoord(request),
            "drag_coord" => ExecuteDragCoord(request),
            "cursor_pos" => ExecuteCursorPos(),
            "type_text" => ExecuteTypeText(request),
            _ => throw new InvalidOperationException("Unknown acceptance command: " + request.Command),
        };
    }

    private object ExecuteScan()
    {
        if (_window.DataContext is MainViewModel vm && vm.ScanCommand.CanExecute(null))
            vm.ScanCommand.Execute(null);

        return _window.GetAcceptanceState();
    }

    private object ExecuteNavigate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("navigate requires path");

        _window.AcceptanceNavigateToPath(path);
        return _window.GetAcceptanceState();
    }

    private object ExecuteBack()
    {
        _window.AcceptanceNavigateBack();
        return _window.GetAcceptanceState();
    }

    private object ExecuteForward()
    {
        _window.AcceptanceNavigateForward();
        return _window.GetAcceptanceState();
    }

    private object ExecuteUp()
    {
        _window.AcceptanceNavigateUp();
        return _window.GetAcceptanceState();
    }

    private object ExecuteEditMode()
    {
        _window.AcceptanceSwitchToEditMode();
        return _window.GetAcceptanceState();
    }

    private object ExecuteBlur()
    {
        _window.AcceptanceBlurAddressBar();
        return _window.GetAcceptanceState();
    }

    private object ExecuteCursorPos()
    {
        GetCursorPos(out var pt);
        return new { X = pt.X, Y = pt.Y };
    }

    private object ExecuteClickCoord(AcceptanceRequest request)
    {
        if (!request.X.HasValue || !request.Y.HasValue)
            throw new InvalidOperationException("click_coord requires x and y");

        var screenX = request.X.Value;
        var screenY = request.Y.Value;
        var button = request.Button ?? "left";

        GetCursorPos(out var before);
        SetCursorPos(screenX, screenY);
        Thread.Sleep(30);

        var downFlag = button is "right" or "r" ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
        var upFlag = button is "right" or "r" ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;

        mouse_event(downFlag, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(30);
        mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);

        GetCursorPos(out var after);
        return new { before = new { X = before.X, Y = before.Y }, target = new { X = screenX, Y = screenY }, after = new { X = after.X, Y = after.Y }, moved = (after.X == screenX && after.Y == screenY) };
    }

    private object ExecuteDragCoord(AcceptanceRequest request)
    {
        if (!request.X.HasValue || !request.Y.HasValue || !request.X2.HasValue || !request.Y2.HasValue)
            throw new InvalidOperationException("drag_coord requires x, y, x2, y2");

        var button = request.Button ?? "left";
        uint downFlag = button is "middle" or "m" ? MOUSEEVENTF_MIDDLEDOWN
            : button is "right" or "r" ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
        uint upFlag = button is "middle" or "m" ? MOUSEEVENTF_MIDDLEUP
            : button is "right" or "r" ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;

        SetCursorPos(request.X.Value, request.Y.Value);
        Thread.Sleep(30);
        mouse_event(downFlag, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(30);
        const int steps = 8;
        for (int i = 1; i <= steps; i++)
        {
            int ix = request.X.Value + (request.X2.Value - request.X.Value) * i / steps;
            int iy = request.Y.Value + (request.Y2.Value - request.Y.Value) * i / steps;
            SetCursorPos(ix, iy);
            Thread.Sleep(25);
        }
        mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        return new { from = new { request.X, request.Y }, to = new { request.X2, request.Y2 }, button };
    }

    private object ExecuteTypeText(AcceptanceRequest request)
    {
        if (string.IsNullOrEmpty(request.Text))
            throw new InvalidOperationException("type_text requires text");

        // Send each character via keybd_event (Unicode via VK_PACKET / SendInput with KEYBDINPUT)
        foreach (var ch in request.Text)
        {
            SendUnicodeChar(ch);
            Thread.Sleep(5);
        }

        return new { typed = request.Text };
    }

    private static void SendUnicodeChar(char ch)
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = 0;
        inputs[0].u.ki.wScan = ch;
        inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
        inputs[0].u.ki.time = 0;
        inputs[0].u.ki.dwExtraInfo = UIntPtr.Zero;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = 0;
        inputs[1].u.ki.wScan = ch;
        inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        inputs[1].u.ki.time = 0;
        inputs[1].u.ki.dwExtraInfo = UIntPtr.Zero;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public MOUSEKEYBDINPUT u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct MOUSEKEYBDINPUT { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record AcceptanceRequest(string Command, string? Path, string? Text, string? Button, int? X, int? Y, int? Delta, int? X2, int? Y2);

    private sealed record AcceptanceResponse(bool Ok, string? Error, object? Data);
}
