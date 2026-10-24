using System.Text.Json;
using FluentAssertions;
using SpaceMonger.Core.Services.Agent;

namespace SpaceMonger.Core.Tests;

public class AgentProposalTests
{
    [Fact]
    public async Task ResolvePathTool_ExpandsEnvironmentVariablesAndReportsScanability()
    {
        var tool = new ResolvePathTool();
        var context = new AgentContext(null, null, null, null, false);
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var arguments = JsonSerializer.SerializeToElement(new { path = @"%USERPROFILE%" });

        var result = await tool.ExecuteAsync(context, arguments, CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("resolved_path").GetString().Should().Be(Path.GetFullPath(expected));
        result.GetProperty("exists").GetBoolean().Should().BeTrue();
        result.GetProperty("is_directory").GetBoolean().Should().BeTrue();
        result.GetProperty("can_scan").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ResolvePathTool_ReturnsNotScannableForMissingPath()
    {
        var tool = new ResolvePathTool();
        var context = new AgentContext(null, null, null, null, false);
        var missingPath = Path.Combine(Path.GetTempPath(), "spacemonger-missing-" + Guid.NewGuid().ToString("N"));
        var arguments = JsonSerializer.SerializeToElement(new { path = missingPath });

        var result = await tool.ExecuteAsync(context, arguments, CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("resolved_path").GetString().Should().Be(Path.GetFullPath(missingPath));
        result.GetProperty("exists").GetBoolean().Should().BeFalse();
        result.GetProperty("can_scan").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("Path does not exist.");
    }

    [Fact]
    public async Task ReadUnityRegistryContextTool_ReadsFixedUnityRegistryAllowlist()
    {
        var reader = new FakeRegistryReader();
        var tool = new ReadUnityRegistryContextTool(reader);
        var context = new AgentContext(null, null, null, null, false);
        var arguments = JsonSerializer.SerializeToElement(new { });

        var result = await tool.ExecuteAsync(context, arguments, CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("purpose").GetString().Should().Contain("do not infer Hub project membership");
        reader.ReadPaths.Should().Contain(@"Software\Unity Technologies\Installer");
        reader.ReadPaths.Should().Contain(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Unity Hub");
        result.GetProperty("keys").GetArrayLength().Should().Be(6);
    }

    private sealed class FakeRegistryReader : IWindowsRegistryReader
    {
        public List<string> ReadPaths { get; } = [];

        public RegistryKeySnapshot ReadKey(Microsoft.Win32.RegistryHive hive, Microsoft.Win32.RegistryView view, string path)
        {
            ReadPaths.Add(path);
            return new RegistryKeySnapshot(hive.ToString(), view.ToString(), path, true, new Dictionary<string, object?>
            {
                ["DisplayName"] = "Unity Hub"
            });
        }
    }
}
