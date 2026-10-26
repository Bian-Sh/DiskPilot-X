using FluentAssertions;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Whitelist;

namespace DiskPilotX.Core.Tests;

public sealed class PathWhitelistMatcherTests : IDisposable
{
    private readonly string _root;
    private readonly PathWhitelistMatcher _matcher = new();

    public PathWhitelistMatcherTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diskpilotx-whitelist-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void IsExcluded_MatchesSamePathAndChildPaths()
    {
        var protectedDir = System.IO.Path.Combine(_root, "Protected");
        var childFile = System.IO.Path.Combine(protectedDir, "child.txt");
        Directory.CreateDirectory(protectedDir);
        File.WriteAllText(childFile, "x");

        var whitelist = new[] { new PathWhitelistEntry { Path = protectedDir } };

        _matcher.IsExcluded(protectedDir, whitelist).Should().BeTrue();
        _matcher.IsExcluded(childFile, whitelist).Should().BeTrue();
        _matcher.IsExcluded(_root, whitelist).Should().BeFalse();
    }

    [Fact]
    public void IsExcluded_IsCaseInsensitiveOnWindows()
    {
        var protectedDir = System.IO.Path.Combine(_root, "CaseDir");
        Directory.CreateDirectory(protectedDir);

        var upperPath = protectedDir.ToUpperInvariant();
        var lowerPath = protectedDir.ToLowerInvariant();

        _matcher.IsExcluded(lowerPath, [new PathWhitelistEntry { Path = upperPath }])
            .Should().Be(OperatingSystem.IsWindows());
    }

    [Fact]
    public void IsExcluded_KeepsInvalidOrMissingEntriesButDoesNotMatchThem()
    {
        var existing = System.IO.Path.Combine(_root, "Existing");
        Directory.CreateDirectory(existing);

        var whitelist = new[]
        {
            new PathWhitelistEntry { Path = System.IO.Path.Combine(_root, "Missing") },
            new PathWhitelistEntry { Path = "<>|" }
        };

        _matcher.IsExcluded(existing, whitelist).Should().BeFalse();
    }

    [Fact]
    public void MergeEntries_DeduplicatesAndMergesDescription()
    {
        var path = System.IO.Path.Combine(_root, "Merge");
        Directory.CreateDirectory(path);

        var result = _matcher.MergeEntries(
            [new PathWhitelistEntry { Path = path, Description = "old" }],
            [new PathWhitelistEntry { Path = path.ToUpperInvariant(), Description = "new" }]);

        result.Should().ContainSingle();
        result[0].Description.Should().Be("new");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
