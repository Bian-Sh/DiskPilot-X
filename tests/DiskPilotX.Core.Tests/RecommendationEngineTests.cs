using FluentAssertions;
using NSubstitute;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using System.Net;
using System.Text.Json;

namespace DiskPilotX.Core.Tests;

public class RecommendationEngineTests
{
    [Fact]
    public void ValidateCandidates_AcceptsStructuredToolArgumentsAndLocalMetadata()
    {
        var entry = Directory(@"C:\Users\tester\AppData\Local\npm-cache\_cacache", 2_028_726_426);
        var root = Directory(@"C:\");
        AddChild(root, entry);
        var result = new RecommendationEngine().ValidateCandidates(Session(root), Candidates(entry.Path, "PackageManagerCache"));

        result.Diagnostics.ParseError.Should().BeNull();
        result.Diagnostics.ParsedRecommendationCount.Should().Be(1);
        var recommendation = result.Recommendations.Should().ContainSingle().Subject;
        recommendation.TargetPath.Should().Be(entry.Path);
        recommendation.Entry.Should().BeSameAs(entry);
        recommendation.Size.Should().Be(entry.Size);
        recommendation.Category.Should().Be(RecommendationCategory.PackageManagerCache);
        recommendation.SafetyRating.Should().Be(SafetyRating.Safe);
        recommendation.Id.Should().Be("1");
        result.Diagnostics.RawResponsePath.Should().BeNull();
        result.Diagnostics.ResponsePreview.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"C:\Windows\assembly\temp", "Safe", SafetyRating.ReviewFirst)]
    [InlineData(@"C:\Windows\VendorApp\Cache", "Safe", SafetyRating.ReviewFirst)]
    [InlineData(@"C:\Windows\Temp", "Caution", SafetyRating.Caution)]
    [InlineData(@"C:\Windows\Prefetch", "ReviewFirst", SafetyRating.ReviewFirst)]
    public void ValidateCandidates_DowngradesWindowsAdjacentSafetyWithoutUpgradingCaution(string path, string safety, SafetyRating expected)
    {
        var root = Directory(@"C:\");
        AddChild(root, Directory(path, 87_258_544));
        var result = new RecommendationEngine().ValidateCandidates(Session(root), Candidates(path, safety: safety));
        result.Diagnostics.ProtectedFilteredCount.Should().Be(0);
        var recommendation = result.Recommendations.Should().ContainSingle().Subject;
        recommendation.SafetyRating.Should().Be(expected);
        recommendation.Explanation.Should().Contain("System-adjacent location");
    }

    [Fact]
    public void ValidateCandidates_FiltersHardProtectedWindowsPathInFullDriveAnalysis()
    {
        var path = @"C:\Windows\System32\kernel32.dll";
        var root = Directory(@"C:\");
        AddChild(root, new FileEntry { Path = path, Name = "kernel32.dll", Size = 1_048_576 });
        var result = new RecommendationEngine().ValidateCandidates(Session(root), Candidates(path));
        result.Diagnostics.ProtectedFilteredCount.Should().Be(1);
        result.Recommendations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnityLibrary_RequiresBothProjectMarkers(bool unityProject)
    {
        var root = Directory(@"D:\");
        var project = Directory(@"D:\Game");
        var library = Directory(@"D:\Game\Library", 2_147_483_648);
        AddChild(root, project);
        AddChild(project, Directory(@"D:\Game\Assets"));
        if (unityProject)
        {
            AddChild(project, Directory(@"D:\Game\ProjectSettings"));
        }
        AddChild(project, library);
        var engine = new RecommendationEngine();
        var result = engine.ValidateCandidates(Session(root), JsonSerializer.SerializeToElement(new { recommendations = Array.Empty<object>() }));
        var unity = RecommendationEngine.BuildUnityLibraryRecommendations(root);

        if (!unityProject)
        {
            result.Recommendations.Should().BeEmpty();
            unity.Should().BeEmpty();
            engine.BuildEvidence(Session(root)).Should().NotContain("UNITY LIBRARY EVIDENCE");
            return;
        }

        unity.Should().ContainSingle().Which.Entry.Should().BeSameAs(library);
        var recommendation = result.Recommendations.Should().ContainSingle().Subject;
        recommendation.TargetPath.Should().Be(library.Path);
        recommendation.Category.Should().Be(RecommendationCategory.BuildCache);
        recommendation.SafetyRating.Should().Be(SafetyRating.ReviewFirst);
        recommendation.Entry.Should().BeSameAs(library);
        recommendation.Explanation.Should().Contain("skill/AI risk review");
        engine.BuildEvidence(Session(root)).Should().Contain("UNITY LIBRARY EVIDENCE").And.Contain("Assets + ProjectSettings");
    }

    [Fact]
    public void BuildEvidence_HandlesDeepLargeTreesWithBoundedMetadata()
    {
        var root = Directory(@"C:\");
        var current = root;
        for (var depth = 0; depth < 2_000; depth++)
        {
            var next = Directory($@"C:\deep\folder-{depth}", 10_000 - depth);
            AddChild(current, next);
            current = next;
        }
        for (var index = 0; index < 10_000; index++)
        {
            AddChild(root, new FileEntry
            {
                Path = $@"C:\many\file-{index}.bin", Name = $"file-{index}.bin", Extension = ".bin",
                Size = 1_048_576 + index, LastModified = DateTime.Today
            });
        }

        var metadata = new RecommendationEngine().BuildEvidence(Session(root));
        metadata.Split("file-", StringSplitOptions.None).Length.Should().BeLessThan(250);
        metadata.Should().Contain("TOTAL_FILES: 10000");
        metadata.Length.Should().BeLessThan(50_000);
        new RecommendationEngine().ValidateCandidates(Session(root), Candidates(current.Path))
            .Recommendations.Should().ContainSingle().Which.Entry.Should().BeSameAs(current);
    }

    [Fact]
    public void BuildEvidence_BoundsFingerprintFilesAndKeepsContentRiskEvidence()
    {
        var root = Directory(@"D:\");
        var cache = Directory(@"D:\Temp", 3000);
        AddChild(root, cache);
        for (var index = 0; index < 2100; index++)
        {
            AddChild(cache, new FileEntry
            {
                Path = $@"D:\Temp\report-{index}.docx", Name = $"report-{index}.docx", Extension = ".docx",
                Size = 1024, LastModified = new DateTime(2024, 1, 1).AddDays(index)
            });
        }
        var evidence = new RecommendationEngine().BuildEvidence(Session(root));
        evidence.Should().Contain("files:2000+100 more").And.Contain("user_content:100%").And.Contain("std_temp_location:no");
        evidence.Length.Should().BeLessThan(20_000);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EvidenceAndValidation_LoadCleanupWhitelistOncePerCall(bool buildEvidence)
    {
        var root = Directory(@"C:\");
        for (var index = 0; index < 500; index++)
        {
            var dir = Directory($@"C:\Temp\dir-{index}", 1024);
            AddChild(root, dir);
            AddChild(dir, new FileEntry { Path = dir.Path + @"\file.tmp", Name = "file.tmp", Extension = ".tmp", Size = 1024 });
        }
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(new AppSettings());
        var engine = new RecommendationEngine(settings);
        if (buildEvidence)
        {
            engine.BuildEvidence(Session(root)).Should().Contain("TOTAL_FILES: 500");
        }
        else
        {
            engine.ValidateCandidates(Session(root), JsonSerializer.SerializeToElement(new
            {
                recommendations = root.Children.Select(entry => new { path = entry.Path, category = "TemporaryFiles", safety_rating = "Safe", explanation = "缓存" })
            })).Recommendations.Should().HaveCount(500);
        }
        settings.Received(1).LoadSettings();
    }

    private static FileEntry Directory(string path, long size = 0) => new()
    {
        Path = path, Name = path.Split('\\').Last(), IsDirectory = true, Size = size
    };

    private static ScanSession Session(FileEntry root) => new() { TargetPath = root.Path, RootEntry = root };

    private static JsonElement Candidates(string path, string category = "TemporaryFiles", string safety = "Safe") =>
        JsonSerializer.SerializeToElement(new
        {
            recommendations = new[] { new { path, category, safety_rating = safety, explanation = "待校验候选" } }
        });

    private static void AddChild(FileEntry parent, FileEntry child)
    {
        child.Parent = parent;
        child.Depth = parent.Depth + 1;
        parent.Children.Add(child);
    }

}
