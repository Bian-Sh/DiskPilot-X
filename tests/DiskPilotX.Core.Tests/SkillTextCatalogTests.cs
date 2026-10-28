using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.Core.Tests;

public sealed class SkillTextCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "skill-text-" + Guid.NewGuid().ToString("N"));
    private CleanupSkillStore Store => new(Path.Combine(root, "user"), [Path.Combine(root, "builtin")]);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true
    };

    private static CleanupSkillPackage Package(Dictionary<string, Dictionary<string, string>>? localizations = null) => new()
    {
        Id = "sample-text", Title = "示例", Description = "文案目录验证", SkillMarkdown = "# 示例",
        Source = "// 只读示例", Localizations = localizations
    };

    [Fact]
    public void CatalogUsesExactThenParentThenEnglishThenKey()
    {
        var catalog = new SkillTextCatalog(new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new() { ["only-en"] = "English", ["title"] = "English title" },
            ["en-US"] = new() { ["title"] = "US title", ["only-default"] = "Default" },
            ["fr"] = new() { ["title"] = "Titre", ["parent"] = "Parent" },
            ["fr-CA"] = new() { ["title"] = "Titre canadien" }
        });
        Assert.Equal("Titre canadien", catalog.Get("title", "fr-CA"));
        Assert.Equal("Parent", catalog.Get("parent", "fr-CA"));
        Assert.Equal("Default", catalog.Get("only-default", "fr-CA"));
        Assert.Equal("English", catalog.Get("only-en", "fr-CA"));
        Assert.Equal("US title", catalog.Get("title", "de-DE"));
        Assert.Equal("missing", catalog.Get("missing", "fr-CA"));
        Assert.Equal("Title", catalog.Get("Title", "fr-CA"));
    }

    [Theory]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-SG", "zh-CN")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("zh-HK", "zh-TW")]
    [InlineData("zh-MO", "zh-Hant")]
    public void CatalogMapsChineseScriptsWithoutCrossingThem(string requested, string resource)
    {
        var catalog = new SkillTextCatalog(new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new() { ["label"] = "Default" },
            [resource] = new() { ["label"] = "目标文案" }
        });
        Assert.Equal("目标文案", catalog.Get("label", requested));
    }

    [Fact]
    public void CatalogExactChineseCultureOverridesScriptAndParent()
    {
        var catalog = new SkillTextCatalog(new Dictionary<string, Dictionary<string, string>>
        {
            ["zh"] = new() { ["parent"] = "中文" },
            ["zh-Hant"] = new() { ["title"] = "繁體", ["script"] = "繁體共用" },
            ["zh-TW"] = new() { ["title"] = "臺灣" },
            ["zh-CN"] = new() { ["simplified-only"] = "仅简体" }
        });
        Assert.Equal("臺灣", catalog.Get("title", "zh-TW"));
        Assert.Equal("繁體共用", catalog.Get("script", "zh-TW"));
        Assert.Equal("中文", catalog.Get("parent", "zh-TW"));
        Assert.Equal("simplified-only", catalog.Get("simplified-only", "zh-TW"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../zh-CN")]
    [InlineData("not a culture!")]
    public void InvalidCultureSafelyUsesEnglishForLookupAndFormatting(string? culture)
    {
        var catalog = new SkillTextCatalog(new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new() { ["count"] = "Count: {0:N1}" }
        });
        Assert.Equal("Count: 1,234.5", catalog.Format("count", culture, 1234.5));
    }

    [Fact]
    public void FormatUsesRequestedCultureEvenWhenTextFallsBack()
    {
        var catalog = new SkillTextCatalog(new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new() { ["count"] = "Count: {0:N1}" }
        });
        Assert.Equal("Count: " + 1234.5.ToString("N1", CultureInfo.GetCultureInfo("de-DE")),
            catalog.Format("count", "de-DE", 1234.5));
    }

    [Fact]
    public void CatalogNeedsNoFilesAndSnapshotsItsInput()
    {
        Dictionary<string, Dictionary<string, string>> tables = new() { ["EN-us"] = new() { ["title"] = "Original" } };
        var catalog = new SkillTextCatalog(tables);
        tables["EN-us"]["title"] = "Changed";
        Assert.Equal("Original", catalog.Get("title", "en-US"));
        Assert.Equal("legacy title", new SkillTextCatalog(null).Get("legacy title", "zh-CN"));
    }

    [Fact]
    public void LocalizationsRoundTripAndTextEditsInvalidateDigest()
    {
        var first = Store.Save(Package(new() { ["zh-CN"] = new() { ["title"] = "旧文案" } }));
        Assert.Equal("旧文案", Store.Load(first.Id).Localizations!["zh-CN"]["title"]);
        var second = Store.Save(first with { Localizations = new() { ["zh-CN"] = new() { ["title"] = "新文案" } } }, true);
        Assert.NotEqual(first.Digest, second.Digest);
        Assert.Equal("新文案", Store.Load(first.Id).Localizations!["zh-CN"]["title"]);
    }

    [Fact]
    public void NoLocalizationsPreservesLegacySerializedShapeAndDigest()
    {
        var package = Package();
        // 独立构造旧模型序列化输入，避免与新字段共享摘要实现而掩盖回归。
        var legacyJson = JsonSerializer.Serialize(new
        {
            package.Id, package.Title, package.Description, package.Version, package.HostProtocolVersion,
            package.SkillMarkdown, package.Rules, package.References, package.UiXaml, package.Source, Digest = ""
        }, JsonOptions);
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(legacyJson)));
        var saved = Store.Save(package);
        Assert.Equal(expected, saved.Digest);
        Assert.Equal(legacyJson, JsonSerializer.Serialize(package, JsonOptions));
        Assert.Null(Store.Load(saved.Id).Localizations);
        Assert.DoesNotContain("localizations", File.ReadAllText(ManifestPath(saved)));
    }

    [Fact]
    public void TamperedVersionedLocalizationsCannotBeLoaded()
    {
        var saved = Store.Save(Package(new() { ["en-US"] = new() { ["title"] = "Original" } }));
        var path = ManifestPath(saved);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["localizations"]!["en-US"]!["title"] = "Tampered";
        File.WriteAllText(path, json.ToJsonString(JsonOptions));
        Assert.Contains("摘要", Assert.Throws<InvalidDataException>(() => Store.Load(saved.Id)).Message);
    }

    [Fact]
    public void BuiltInReadsOnlyItsI18nAndTextEditsChangeDigest()
    {
        var directory = CreateBuiltIn();
        File.WriteAllText(Path.Combine(directory, "i18n", "zh-CN.json"), "{\"title\":\"内置文案\"}");
        var first = Store.LoadReadOnly("sample-text");
        Assert.Equal("内置文案", new SkillTextCatalog(first.Localizations).Get("title", "zh-Hans"));
        File.WriteAllText(Path.Combine(directory, "i18n", "zh-CN.json"), "{\"title\":\"内置新文案\"}");
        Assert.NotEqual(first.Digest, Store.LoadReadOnly("sample-text").Digest);
    }

    [Fact]
    public void BuiltInWithoutI18nStillLoads()
    {
        var directory = CreateBuiltIn();
        Directory.Delete(Path.Combine(directory, "i18n"));
        Assert.Null(Store.LoadReadOnly("sample-text").Localizations);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"title\":1}")]
    [InlineData("{\"title\":null}")]
    [InlineData("{\"title\":\"one\",\"title\":\"two\"}")]
    public void BuiltInMalformedJsonHasFileAndCultureDiagnostic(string json)
    {
        var directory = CreateBuiltIn();
        File.WriteAllText(Path.Combine(directory, "i18n", "en-US.json"), json);
        var error = Assert.Throws<InvalidDataException>(() => Store.LoadReadOnly("sample-text"));
        Assert.Contains("i18n/en-US.json", error.Message);
        Assert.Contains("文案", error.Message);
    }

    [Fact]
    public void BuiltInRejectsOversizedFileAndTooManyCultures()
    {
        var directory = CreateBuiltIn();
        var file = Path.Combine(directory, "i18n", "en-US.json");
        File.WriteAllText(file, new string(' ', 128 * 1024 + 1));
        Assert.Throws<InvalidDataException>(() => Store.LoadReadOnly("sample-text"));
        File.Delete(file);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures).Take(33))
            File.WriteAllText(Path.Combine(directory, "i18n", culture.Name + ".json"), "{}");
        Assert.Throws<InvalidDataException>(() => Store.LoadReadOnly("sample-text"));
    }

    [Fact]
    public void BuiltInRejectsUnexpectedNestedDirectory()
    {
        var directory = CreateBuiltIn();
        Directory.CreateDirectory(Path.Combine(directory, "i18n", "nested"));
        Assert.Throws<InvalidDataException>(() => Store.LoadReadOnly("sample-text"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuiltInRejectsReparsePointsForDirectoryOrFile(bool fileLink)
    {
        var directory = CreateBuiltIn();
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "en-US.json");
        File.WriteAllText(outsideFile, "{\"title\":\"secret\"}");
        if (fileLink)
            File.CreateSymbolicLink(Path.Combine(directory, "i18n", "en-US.json"), outsideFile);
        else
        {
            Directory.Delete(Path.Combine(directory, "i18n"));
            Directory.CreateSymbolicLink(Path.Combine(directory, "i18n"), outside);
        }
        Assert.Throws<IOException>(() => Store.LoadReadOnly("sample-text"));
    }

    [Theory]
    [InlineData("../en-US")]
    [InlineData("en_US")]
    [InlineData("en-US:stream")]
    public void StoreRejectsUnsafeCultureNames(string culture)
    {
        Assert.Contains("文化", Assert.Throws<ArgumentException>(() =>
            CleanupSkillStore.Validate(Package(new() { [culture] = new() { ["title"] = "Text" } }))).Message);
    }

    [Fact]
    public void StoreRejectsDuplicateCultureNamesAndInvalidEntries()
    {
        Assert.Throws<ArgumentException>(() => CleanupSkillStore.Validate(Package(new()
        {
            ["en-US"] = new(), ["EN-us"] = new()
        })));
        foreach (var entries in new Dictionary<string, string>[]
        {
            new() { [""] = "Text" }, new() { [new string('k', 257)] = "Text" },
            new() { ["title"] = new string('v', 16385) }, new() { ["title"] = null! },
            new() { ["bad\0key"] = "Text" }, new() { ["title"] = "bad\0value" },
            Enumerable.Range(0, 1025).ToDictionary(i => "key" + i, _ => "Text")
        })
            Assert.Throws<ArgumentException>(() => CleanupSkillStore.Validate(Package(new() { ["en-US"] = entries })));
    }

    [Fact]
    public void StoreRejectsOversizedCatalogButAllowsMultilineText()
    {
        CleanupSkillStore.Validate(Package(new() { ["en-US"] = new() { ["title"] = "line\nline\tvalue" } }));
        var entries = Enumerable.Range(0, 20).ToDictionary(i => "key" + i, _ => new string('v', 8192));
        Assert.Throws<ArgumentException>(() => CleanupSkillStore.Validate(Package(new() { ["en-US"] = entries })));
        var tables = new Dictionary<string, Dictionary<string, string>>();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures).Take(33)) tables[culture.Name] = new();
        Assert.Throws<ArgumentException>(() => CleanupSkillStore.Validate(Package(tables)));
    }

    [Fact]
    public void DetectorContextKeepsOldArgumentPositionsAndAcceptsLocalizationSnapshot()
    {
        IReadOnlyDictionary<string, string> parameters = new Dictionary<string, string> { ["scope"] = "cache" };
        var previous = new CleanupSkillDetectionContext("sample-text", root, [], parameters);
        Assert.Same(parameters, previous.Parameters);
        Assert.Null(previous.Localization);
        var localization = SkillLocalizationState.FromCulture(CultureInfo.GetCultureInfo("zh-TW"));
        var localized = new CleanupSkillDetectionContext("sample-text", root, [], parameters, localization);
        Assert.Same(localization, localized.Localization);
    }

    [Fact]
    public void AggregateLocalizationBytesAreBoundedAcrossCultures()
    {
        var tables = new Dictionary<string, Dictionary<string, string>>();
        foreach (var culture in new[] { "en-US", "fr-FR", "de-DE", "ja-JP", "zh-CN", "zh-TW" })
            tables[culture] = Enumerable.Range(0, 12).ToDictionary(i => "key" + i, _ => new string('v', 8192));
        Assert.Contains("512 KiB", Assert.Throws<ArgumentException>(() => CleanupSkillStore.Validate(Package(tables))).Message);
    }
    private string ManifestPath(CleanupSkillPackage saved) => Path.Combine(Store.UserRoot, saved.Id, "versions", saved.Digest, "manifest.json");

    private string CreateBuiltIn()
    {
        var directory = Path.Combine(root, "builtin", "sample-text");
        Directory.CreateDirectory(Path.Combine(directory, "i18n"));
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), "# 示例\n内置技能。");
        return directory;
    }

    public void Dispose()
    {
        // 仅清理本测试自己创建的唯一临时目录。
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}