using System.Text.Json;
using FluentAssertions;
using DiskPilotX.Core.Services.Agent;

namespace DiskPilotX.Core.Tests;

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
        var missingPath = Path.Combine(Path.GetTempPath(), "diskpilotx-missing-" + Guid.NewGuid().ToString("N"));
        var arguments = JsonSerializer.SerializeToElement(new { path = missingPath });

        var result = await tool.ExecuteAsync(context, arguments, CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("resolved_path").GetString().Should().Be(Path.GetFullPath(missingPath));
        result.GetProperty("exists").GetBoolean().Should().BeFalse();
        result.GetProperty("can_scan").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("Path does not exist.");
    }


}
