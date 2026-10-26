using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Analysis;

namespace DiskPilotX.Core.Tests;

public class RecommendationPolicyTests
{
    [Fact]
    public void ValidateCandidates_RejectsMissingScanEntry()
    {
        var root = Directory(@"D:\scan");
        Validate(root, [@"D:\scan\missing"]).Recommendations.Should().BeEmpty();
    }

    [Fact]
    public void ValidateCandidates_UsesScannedSizeInsteadOfModelSize()
    {
        var root = Directory(@"D:\scan");
        var entry = Directory(@"D:\scan\cache", 1234);
        root.Children.Add(entry);
        var recommendation = Validate(root, [entry.Path]).Recommendations.Should().ContainSingle().Subject;
        recommendation.Size.Should().Be(1234);
        recommendation.Entry.Should().BeSameAs(entry);
    }

    [Fact]
    public void ValidateCandidates_RejectsScannedSiblingOutsideFocus()
    {
        var root = Directory(@"D:\scan");
        var focus = Directory(@"D:\scan\focus");
        var sibling = Directory(@"D:\scan\focus-other");
        root.Children.AddRange([focus, sibling]);
        Validate(root, [sibling.Path], focus).Recommendations.Should().BeEmpty();
    }

    [Fact]
    public void ValidateCandidates_RejectsFocusAbsentFromScanTree()
    {
        var root = Directory(@"D:\scan");
        var entry = Directory(@"D:\scan\cache");
        root.Children.Add(entry);
        Validate(root, [entry.Path], Directory(@"D:\scan\forged")).Recommendations.Should().BeEmpty();
    }

    [Fact]
    public void ValidateCandidates_RejectsHardProtectedPathEvenWhenFocused()
    {
        var root = Directory(@"C:\");
        var entry = Directory(@"C:\Windows\System32");
        root.Children.Add(entry);
        Validate(root, [entry.Path], entry).Recommendations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateCandidates_RejectsOverlappingParentChildAndDuplicate(bool childFirst)
    {
        var root = Directory(@"D:\scan");
        var parent = Directory(@"D:\scan\cache");
        var child = Directory(@"D:\scan\cache\file.tmp");
        root.Children.Add(parent);
        parent.Children.Add(child);
        string[] paths = childFirst ? [child.Path, parent.Path, child.Path] : [parent.Path, child.Path, parent.Path];
        Validate(root, paths).Recommendations.Should().ContainSingle();
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\kernel32.dll", true)]
    [InlineData(@"C:/WINDOWS/SysWOW64/file.dll", true)]
    [InlineData(@"C:\Windows\Temp\..\WinSxS\file.dll", true)]
    [InlineData(@"C:\Windows\servicing", true)]
    [InlineData(@"C:\Windows\Installer", true)]
    [InlineData(@"C:\Windows\SystemResources", true)]
    [InlineData(@"C:\Windows\Boot", true)]
    [InlineData(@"C:\Program Files\App\cache", true)]
    [InlineData(@"C:\Program Files (x86)\App", true)]
    [InlineData(@"C:\Users\tester\Documents\draft.docx", true)]
    [InlineData(@"C:\Users\tester\Desktop", true)]
    [InlineData(@"C:\Users\tester\Pictures", true)]
    [InlineData(@"C:\Users\tester\Music", true)]
    [InlineData(@"C:\Users\tester\Videos", true)]
    [InlineData(@"C:\Windows\Temp", false)]
    [InlineData(@"C:\Windows\VendorApp\Cache", false)]
    [InlineData(@"D:\work\System32", false)]
    [InlineData(@"C:\Windows\System32-backup", false)]
    public void IsDeletionProtected_UsesHardProtectionAndSegmentBoundaries(string path, bool expected)
    {
        RecommendationEngine.IsDeletionProtected(path).Should().Be(expected);
        var root = Directory(Path.GetPathRoot(path)!);
        var entry = Directory(path);
        root.Children.Add(entry);
        var result = Validate(root, [path], entry);
        result.Recommendations.Should().HaveCount(expected ? 0 : 1);
    }

    [Fact]
    public void ValidateCandidates_RejectsParentContainingHardProtectedDescendant()
    {
        var root = Directory(@"C:\");
        var windows = Directory(@"C:\Windows");
        root.Children.Add(windows);
        windows.Children.Add(Directory(@"C:\Windows\System32"));
        Validate(root, [windows.Path], windows).Recommendations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"recommendations\":{}}")]
    [InlineData("\"```json {\\\"recommendations\\\":[]} ```\"")]
    public void ValidateCandidates_RejectsNonToolArgumentObjects(string json)
    {
        var root = Directory(@"D:\scan");
        using var document = JsonDocument.Parse(json);
        var result = new RecommendationEngine().ValidateCandidates(new ScanSession { RootEntry = root }, document.RootElement);
        result.Recommendations.Should().BeEmpty();
        result.Diagnostics.ParseError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidateCandidates_SkipsMalformedItemsAndUsesConservativeEnumDefaults()
    {
        var root = Directory(@"D:\scan");
        var entry = Directory(@"D:\scan\cache", 42);
        root.Children.Add(entry);
        using var document = JsonDocument.Parse("""
            {"recommendations":[null,3,{"path":"D:\\scan\\cache"},
            {"path":"D:\\scan\\cache","category":"Other","safety_rating":0,"explanation":"错误类型"},
            {"path":"D:\\scan\\cache","category":"999","safety_rating":"0","explanation":"保守回退","size_bytes":-999}]}
            """);
        var result = new RecommendationEngine().ValidateCandidates(new ScanSession { RootEntry = root }, document.RootElement);
        result.Diagnostics.MalformedRecommendationCount.Should().Be(3);
        result.Diagnostics.MissingFieldRecommendationCount.Should().Be(1);
        var recommendation = result.Recommendations.Should().ContainSingle().Subject;
        recommendation.Size.Should().Be(42);
        recommendation.Category.Should().Be(RecommendationCategory.Other);
        recommendation.SafetyRating.Should().Be(SafetyRating.ReviewFirst);
    }

    [Fact]
    public void BuildEvidence_ResolvesFocusToScanEntryAndRejectsMissingFocus()
    {
        var root = Directory(@"D:\scan");
        var focus = Directory(@"D:\scan\cache", 42);
        root.Children.Add(focus);
        var session = new ScanSession { TargetPath = root.Path, RootEntry = root };
        var engine = new RecommendationEngine();
        engine.BuildEvidence(session, Directory(focus.Path, 999999)).Should().Contain("TOTAL_SIZE: 42").And.NotContain("999999");
        engine.BuildEvidence(session, Directory(@"D:\scan\missing")).Should().BeEmpty();
        engine.BuildEvidence(new ScanSession()).Should().BeEmpty();
        using var empty = JsonDocument.Parse("{\"recommendations\":[]}");
        engine.ValidateCandidates(new ScanSession(), empty.RootElement).Recommendations.Should().BeEmpty();
    }

    [Fact]
    public void Whitelist_PreservesExistingPathSemanticsAndRefreshesBetweenCalls()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "diskpilotx-policy-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(temporaryRoot);
        try
        {
            var excludedPath = Path.Combine(temporaryRoot, "secret-cache");
            var allowedPath = Path.Combine(temporaryRoot, "allowed-cache");
            System.IO.Directory.CreateDirectory(excludedPath);
            System.IO.Directory.CreateDirectory(allowedPath);
            var root = Directory(temporaryRoot);
            var excluded = Directory(excludedPath, 100);
            var allowed = Directory(allowedPath, 200);
            root.Children.AddRange([excluded, allowed]);
            var settings = new AppSettings
            {
                CleanupRecommendationWhitelist = [new PathWhitelistEntry { Path = excludedPath }],
                ScanWhitelist = [new PathWhitelistEntry { Path = allowedPath }],
                AiConversationWhitelist = [new PathWhitelistEntry { Path = allowedPath }]
            };
            var service = Substitute.For<ISettingsService>();
            service.LoadSettings().Returns(settings);
            var engine = new RecommendationEngine(service);
            var session = new ScanSession { TargetPath = temporaryRoot, RootEntry = root };
            engine.BuildEvidence(session).Should().NotContain("secret-cache").And.Contain("allowed-cache");
            engine.BuildEvidence(session, excluded).Should().BeEmpty();
            engine.IsRecommendationExcluded(excludedPath).Should().BeTrue();
            engine.IsRecommendationExcluded(temporaryRoot).Should().BeFalse();
            engine.IsRecommendationExcluded(allowedPath).Should().BeFalse();
            var candidates = JsonSerializer.SerializeToElement(new
            {
                recommendations = new[] { excludedPath, allowedPath, temporaryRoot }.Select(path => new
                {
                    path, category = "Other", safety_rating = "Safe", explanation = "待校验"
                })
            });
            engine.ValidateCandidates(session, candidates).Recommendations.Should().ContainSingle().Which.TargetPath.Should().Be(allowedPath);
            engine.ValidateCandidates(session, candidates, excluded).Recommendations.Should().BeEmpty();

            settings.CleanupRecommendationWhitelist.Clear();
            engine.IsRecommendationExcluded(excludedPath).Should().BeFalse();
            engine.ValidateCandidates(session, candidates).Recommendations.Should().HaveCount(2);
            var missing = Path.Combine(temporaryRoot, "missing");
            settings.CleanupRecommendationWhitelist.Add(new PathWhitelistEntry { Path = missing });
            engine.IsRecommendationExcluded(missing).Should().BeFalse();
        }
        finally
        {
            System.IO.Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateCandidates_AppliesFocusAndHardProtectionToUnityFallback()
    {
        var root = Directory(@"C:\");
        var protectedProject = Directory(@"C:\Users\tester\Documents\Game");
        protectedProject.Children.AddRange([
            Directory(protectedProject.Path + @"\Assets"),
            Directory(protectedProject.Path + @"\ProjectSettings"),
            Directory(protectedProject.Path + @"\Library")]);
        var focus = Directory(@"C:\work");
        root.Children.AddRange([protectedProject, focus]);
        using var document = JsonDocument.Parse("{\"recommendations\":[]}");
        var session = new ScanSession { RootEntry = root };
        var engine = new RecommendationEngine();
        engine.ValidateCandidates(session, document.RootElement).Recommendations.Should().BeEmpty();
        engine.ValidateCandidates(session, document.RootElement, protectedProject).Recommendations.Should().BeEmpty();
        engine.ValidateCandidates(session, document.RootElement, focus).Recommendations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\cache")]
    [InlineData(@"C:")]
    [InlineData("C:\\invalid\0")]
    public void IsDeletionProtected_FailsClosedForInvalidOrRelativePaths(string? path)
    {
        RecommendationEngine.IsDeletionProtected(path!).Should().BeTrue();
    }

    [Fact]
    public void ValidateCandidates_NormalizesScanPathButKeepsLocalTargetAndSize()
    {
        var root = Directory(@"D:\scan");
        var entry = Directory(@"D:\scan\Cache", 42);
        root.Children.Add(entry);
        var result = Validate(root, [@"d:/scan/other/../cache/", entry.Path]);
        var recommendation = result.Recommendations.Should().ContainSingle().Subject;
        recommendation.TargetPath.Should().Be(entry.Path);
        recommendation.Size.Should().Be(42);
    }

    [Fact]
    public void UnityFallback_RespectsWhitelistAndModelParentOverlap()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "diskpilotx-policy-tests", Guid.NewGuid().ToString("N"));
        var libraryPath = Path.Combine(temporaryRoot, "Library");
        System.IO.Directory.CreateDirectory(libraryPath);
        try
        {
            var root = Directory(temporaryRoot, 100);
            root.Children.AddRange([
                Directory(Path.Combine(temporaryRoot, "Assets")),
                Directory(Path.Combine(temporaryRoot, "ProjectSettings")),
                Directory(libraryPath, 50)]);
            var service = Substitute.For<ISettingsService>();
            var settings = new AppSettings { CleanupRecommendationWhitelist = [new PathWhitelistEntry { Path = libraryPath }] };
            service.LoadSettings().Returns(settings);
            var engine = new RecommendationEngine(service);
            var session = new ScanSession { TargetPath = temporaryRoot, RootEntry = root };
            using var empty = JsonDocument.Parse("{\"recommendations\":[]}");
            engine.ValidateCandidates(session, empty.RootElement).Recommendations.Should().BeEmpty();
            engine.BuildEvidence(session).Should().NotContain(libraryPath).And.NotContain("UNITY LIBRARY EVIDENCE");

            settings.CleanupRecommendationWhitelist.Clear();
            var candidates = JsonSerializer.SerializeToElement(new
            {
                recommendations = new[] { new { path = temporaryRoot, category = "Other", safety_rating = "Caution", explanation = "待校验父目录" } }
            });
            engine.ValidateCandidates(session, candidates).Recommendations.Should().ContainSingle().Which.TargetPath.Should().Be(temporaryRoot);
        }
        finally
        {
            System.IO.Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static FileEntry Directory(string path, long size = 10) => new()
    {
        Path = path,
        Name = path.Split('\\').Last(),
        IsDirectory = true,
        Size = size
    };

    private static AnalysisResult Validate(FileEntry root, string[] paths, FileEntry? focus = null)
    {
        var response = JsonSerializer.Serialize(new
        {
            recommendations = paths.Select(path => new
            {
                path,
                size_bytes = 999999,
                category = "TemporaryFiles",
                safety_rating = "Safe",
                explanation = "待校验候选"
            })
        });
        using var document = JsonDocument.Parse(response);
        return new RecommendationEngine().ValidateCandidates(new ScanSession { TargetPath = root.Path, RootEntry = root }, document.RootElement, focus);
    }
}
