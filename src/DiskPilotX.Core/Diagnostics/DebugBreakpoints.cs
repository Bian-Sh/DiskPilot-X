using System.Diagnostics;
using Serilog;

namespace DiskPilotX.Core.Diagnostics;

internal static class DebugBreakpoints
{
    private const string BreakpointsVariable = "DISKPILOT_X_DEBUG_BREAKPOINTS";
    private const string LaunchVariable = "DISKPILOT_X_DEBUG_LAUNCH";

    public static void Hit(string name)
    {
        if (!IsEnabled(name))
            return;

        Log.Debug("Debug breakpoint hit: {Name}", name);

        if (!Debugger.IsAttached && IsLaunchEnabled())
        {
            Debugger.Launch();
        }

        if (Debugger.IsAttached)
        {
            Debugger.Break();
        }
    }

    private static bool IsEnabled(string name)
    {
        var raw = Environment.GetEnvironmentVariable(BreakpointsVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var tokens = raw.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(token =>
            string.Equals(token, "all", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLaunchEnabled()
    {
        var raw = Environment.GetEnvironmentVariable(LaunchVariable);
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
