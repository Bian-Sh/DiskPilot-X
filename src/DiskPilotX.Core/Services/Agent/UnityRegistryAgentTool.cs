using System.Text.Json;
using Microsoft.Win32;

namespace DiskPilotX.Core.Services.Agent;

public sealed record RegistryKeySnapshot(
    string Hive,
    string View,
    string Path,
    bool Exists,
    IReadOnlyDictionary<string, object?> Values,
    string? Error = null);

public interface IWindowsRegistryReader
{
    RegistryKeySnapshot ReadKey(RegistryHive hive, RegistryView view, string path);
}

public sealed class WindowsRegistryReader : IWindowsRegistryReader
{
    public RegistryKeySnapshot ReadKey(RegistryHive hive, RegistryView view, string path)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key is null)
            {
                return new RegistryKeySnapshot(hive.ToString(), view.ToString(), path, false, new Dictionary<string, object?>());
            }

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var valueName in key.GetValueNames().Order(StringComparer.OrdinalIgnoreCase).Take(80))
            {
                values[valueName.Length == 0 ? "(default)" : valueName] = NormalizeValue(key.GetValue(valueName));
            }

            return new RegistryKeySnapshot(hive.ToString(), view.ToString(), path, true, values);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RegistryKeySnapshot(hive.ToString(), view.ToString(), path, false, new Dictionary<string, object?>(), ex.Message);
        }
    }

    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            string[] items => items,
            int number => number,
            long number => number,
            byte[] bytes => $"<binary:{bytes.Length} bytes>",
            _ => value.ToString()
        };
    }
}

public sealed class ReadUnityRegistryContextTool : AppCopilotToolBase
{
    private readonly IWindowsRegistryReader _registryReader;

    public ReadUnityRegistryContextTool()
        : this(new WindowsRegistryReader())
    {
    }

    public ReadUnityRegistryContextTool(IWindowsRegistryReader registryReader)
    {
        _registryReader = registryReader;
    }

    public override string Name => "read_unity_registry_context";
    public override string Description => "Read a fixed allowlist of Unity and Unity Hub Windows registry keys for installed editor and Hub context.";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;
    public override JsonElement Schema { get; } = SchemaJson("""
        {"type":"object","properties":{}}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var snapshots = KnownKeys.Select(key =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _registryReader.ReadKey(key.Hive, key.View, key.Path);
        }).ToList();

        return Task.FromResult(Json(new
        {
            ok = true,
            purpose = "corroborate Unity Hub/editor installation context; do not infer Hub project membership from registry",
            keys = snapshots
        }));
    }

    private static readonly IReadOnlyList<(RegistryHive Hive, RegistryView View, string Path)> KnownKeys =
    [
        (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Unity Technologies\Installer"),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Unity Technologies\Installer"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Unity Technologies\Installer"),
        (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Unity Hub"),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Unity Hub"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Unity Hub")
    ];
}
