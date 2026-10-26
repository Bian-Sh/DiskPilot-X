using FluentAssertions;
using DiskPilotX.Core.Services.Scanning;

namespace DiskPilotX.Core.Tests;

public class ScanPathResolverTests
{
    [Fact]
    public void Resolve_ExpandsEnvironmentVariablesBeforeFullPathNormalization()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var resolved = ScanPathResolver.Resolve(@"%USERPROFILE%");

        resolved.Should().Be(Path.GetFullPath(expected));
        resolved.Should().NotContain("%USERPROFILE%");
    }

    [Fact]
    public void Resolve_TrimsAndKeepsAbsoluteDriveRoots()
    {
        ScanPathResolver.Resolve("  C:\\  ").Should().Be(@"C:\");
    }
}
