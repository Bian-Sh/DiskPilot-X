using System.Text.Json;
using System.Xml.Linq;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Models.Theme;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;

namespace DiskPilotX.App.Tests;

[Collection("CLI 指纹状态")]
public sealed class SettingsPagePolishTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 自动对比度在旧配置和所有材质下始终开启(bool glass)
    {
        var profile = JsonSerializer.Deserialize<ThemeProfile>($$"""{"AutoTextContrast":false,"GlassEnabled":{{glass.ToString().ToLowerInvariant()}}}""")!;
        Assert.True(profile.AutoTextContrast);
        profile.AutoTextContrast = false;
        Assert.True(profile.AutoTextContrast);
        Assert.True(new ThemeProfile().AutoTextContrast);
        Assert.All(ThemeProfile.BuiltInPresets, preset => Assert.True(preset.AutoTextContrast));
        Assert.True(JsonDocument.Parse(JsonSerializer.Serialize(profile)).RootElement.GetProperty("AutoTextContrast").GetBoolean());
    }

    [Fact]
    public void 设置导航与正文均按常规主题AI白名单免责声明排序()
    {
        var document = Load("Views/SettingsPage.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var expected = new[] { "GeneralSectionControl", "ThemeSectionControl", "ApiSectionControl", "WhitelistSectionControl", "DisclaimerSectionControl" };
        Assert.Equal(expected, document.Descendants().Where(e => e.Name.LocalName == "Button" && e.Attribute("Tag") != null).Select(e => (string?)e.Attribute("Tag")));
        Assert.Equal(expected, document.Descendants().Where(e => e.Name.NamespaceName.EndsWith("SettingsSections")).Select(e => (string?)e.Attribute(x + "Name")));
        Assert.Equal("0,18,0,0", (string?)document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "ApiSectionControl").Attribute("Margin"));
        var theme = Load("Views/SettingsSections/ThemeSettingsSection.xaml");
        Assert.DoesNotContain(theme.Descendants(), e => ((string?)e.Attribute("IsChecked"))?.Contains("AutoTextContrast") == true);
    }

    [Fact]
    public void 仅两组白名单共用紧凑模板且删除支持悬停和键盘焦点()
    {
        var document = Load("Views/SettingsSections/WhitelistSettingsSection.xaml");
        var lists = document.Descendants().Where(e => e.Name.LocalName == "ItemsControl").ToArray();
        Assert.Equal(2, lists.Length);
        Assert.All(lists, e => Assert.Equal("{StaticResource WhitelistEntryTemplate}", (string?)e.Attribute("ItemTemplate")));
        Assert.DoesNotContain(document.Descendants(), e => e.Attributes().Any(a => a.Value.Contains("AiConversationWhitelist") || a.Value.Contains("NewAiWhitelistPath")));
        var triggers = document.Descendants().Where(e => e.Name.LocalName == "Trigger").ToArray();
        Assert.Contains(triggers, e => (string?)e.Attribute("Property") == "IsMouseOver");
        Assert.Contains(triggers, e => (string?)e.Attribute("Property") == "IsKeyboardFocusWithin");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var template = document.Descendants().Single(e => (string?)e.Attribute(x + "Key") == "WhitelistEntryTemplate");
        Assert.DoesNotContain(template.Descendants(), e => e.Name.LocalName == "StackPanel");
        Assert.Equal(new[] { "1", "2" }, template.Descendants().Where(e => e.Name.LocalName == "TextBox").Select(e => (string?)e.Attribute("Grid.Column")));
        var row = template.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "WhitelistRow");
        Assert.Equal("34", (string?)row.Attribute("MinHeight"));
    }

    [Fact]
    public void 白名单只在条目之间画线而不会和分组分隔线叠加()
    {
        var document = Load("Views/SettingsSections/WhitelistSettingsSection.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var row = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "WhitelistRow");
        Assert.Equal("0,1,0,0", (string?)row.Attribute("BorderThickness"));
        var firstRow = document.Descendants().Single(e => e.Name.LocalName == "DataTrigger" && ((string?)e.Attribute("Binding"))?.Contains("(ItemsControl.AlternationIndex)") == true);
        Assert.Equal("0", (string?)firstRow.Attribute("Value"));
        Assert.Contains("ElementName=WhitelistRow", (string?)firstRow.Attribute("Binding"));
        Assert.Contains(firstRow.Descendants(), e => (string?)e.Attribute("TargetName") == "WhitelistRow" && (string?)e.Attribute("Property") == "BorderThickness" && (string?)e.Attribute("Value") == "0");
        Assert.All(document.Descendants().Where(e => e.Name.LocalName == "ItemsControl"), e => Assert.Equal(int.MaxValue.ToString(), (string?)e.Attribute("AlternationCount")));
        Assert.Single(document.Descendants().Where(e => e.Name.LocalName == "Separator"));
    }

    [Fact]
    public void 加号在标题工具栏最右侧且新增输入框默认收起()
    {
        var document = Load("Views/SettingsSections/WhitelistSettingsSection.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var headers = document.Descendants().Where(e => (string?)e.Attribute("Tag") == "WhitelistHeader").ToArray();
        Assert.Equal(2, headers.Length);
        foreach (var header in headers)
        {
            var buttons = header.Descendants().Where(e => e.Name.LocalName == "Button").ToArray();
            Assert.Equal(3, buttons.Length);
            Assert.Equal("WhitelistAdd_Click", (string?)buttons[2].Attribute("Click"));
            Assert.NotNull(buttons[2].Attribute("ToolTip"));
            Assert.Contains(buttons[2].Descendants(), e => (string?)e.Attribute("Text") == "+");
            Assert.Equal("{Binding CopyWhitelistCommand}", (string?)buttons[0].Attribute("Command"));
            Assert.Equal("{Binding PasteWhitelistCommand}", (string?)buttons[1].Attribute("Command"));
        }
        var composers = document.Descendants().Where(e => ((string?)e.Attribute(x + "Name"))?.EndsWith("WhitelistComposer") == true).ToArray();
        Assert.Equal(2, composers.Length);
        Assert.All(composers, e => Assert.Equal("Collapsed", (string?)e.Attribute("Visibility")));
        Assert.DoesNotContain(document.Descendants(), e => (string?)e.Attribute("Content") == "{loc:Loc SettingsWhitelistAdd}");
    }

    [Fact]
    public void 文件夹图标仍在输入框内且说明使用问号悬停帮助和加号空态()
    {
        var document = Load("Views/SettingsSections/WhitelistSettingsSection.xaml");
        var browse = document.Descendants().Where(e => e.Name.LocalName == "Button" && ((string?)e.Attribute("Command"))?.Contains("Browse") == true).ToArray();
        Assert.Equal(2, browse.Length);
        foreach (var icon in browse)
        {
            var field = icon.Parent!.Parent!;
            Assert.Equal("Border", field.Name.LocalName);
            Assert.Single(field.Descendants().Where(e => e.Name.LocalName == "TextBox"));
            Assert.NotNull(icon.Attribute("ToolTip"));
        }
        var help = document.Descendants().Where(e => (string?)e.Attribute("Text") == "?").ToArray();
        Assert.Equal(2, help.Length);
        Assert.All(help, e =>
        {
            Assert.Equal("Help", (string?)e.Attribute("Cursor"));
            var tooltipText = Assert.Single(e.Descendants().Where(child => child.Name.LocalName == "TextBlock"));
            Assert.Equal("Wrap", (string?)tooltipText.Attribute("TextWrapping"));
            Assert.Equal("None", (string?)tooltipText.Attribute("TextTrimming"));
            Assert.Equal("320", (string?)tooltipText.Attribute("MaxWidth"));
        });
        foreach (var key in new[] { "SettingsWhitelistDescription", "SettingsWhitelistScanHint", "SettingsWhitelistCleanupHint" })
            Assert.DoesNotContain(document.Descendants().Where(e => !e.Ancestors().Any(a => a.Name.LocalName == "ToolTip")), e => (string?)e.Attribute("Text") == "{loc:Loc " + key + "}");
        Assert.Equal(2, document.Descendants().Count(e => (string?)e.Attribute("Text") == "{loc:Loc SettingsWhitelistEmptyHint}"));
        Assert.Equal(2, document.Descendants().Count(e => (string?)e.Attribute("KeyDown") == "WhitelistPath_KeyDown"));
        var resources = Load("Localization/Strings.zh-CN.resx");
        Assert.Contains("＋", resources.Descendants("data").Single(e => (string?)e.Attribute("name") == "SettingsWhitelistEmptyHint").Element("value")!.Value);
    }

    [Fact]
    public void 白名单提示使用简短白话且复制粘贴使用配置名称()
    {
        var resources = Load("Localization/Strings.zh-CN.resx");
        var expected = new Dictionary<string, string>
        {
            ["SettingsWhitelistScanHint"] = "在此处配置的路径不会被扫描。",
            ["SettingsWhitelistCleanupHint"] = "在此处配置的路径不会进入推荐清理。",
            ["SettingsWhitelistCopyJson"] = "复制白名单配置",
            ["SettingsWhitelistPasteJson"] = "粘贴白名单配置"
        };
        foreach (var (key, value) in expected)
            Assert.Equal(value, resources.Descendants("data").Single(e => (string?)e.Attribute("name") == key).Element("value")!.Value);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("cleanup")]
    [InlineData("ai")]
    public void 白名单新增去重和删除仍立即持久化(string kind)
    {
        var saved = new AppSettings();
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(saved);
        using var clients = new ModelHttpClientFactory();
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings));
        var folder = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        Action add = kind switch
        {
            "scan" => () => { vm.NewScanWhitelistPath = folder; vm.AddScanWhitelistPathCommand.Execute(null); },
            "cleanup" => () => { vm.NewCleanupWhitelistPath = folder; vm.AddCleanupWhitelistPathCommand.Execute(null); },
            _ => () => { vm.NewAiWhitelistPath = folder; vm.AddAiWhitelistPathCommand.Execute(null); }
        };
        add(); add();
        var entries = kind switch { "scan" => vm.ScanWhitelist, "cleanup" => vm.CleanupRecommendationWhitelist, _ => vm.AiConversationWhitelist };
        var entry = Assert.Single(entries);
        var persisted = kind switch { "scan" => saved.ScanWhitelist, "cleanup" => saved.CleanupRecommendationWhitelist, _ => saved.AiConversationWhitelist };
        Assert.Single(persisted);
        vm.RemoveWhitelistEntryCommand.Execute(entry);
        Assert.Empty(entries);
        Assert.Empty(kind switch { "scan" => saved.ScanWhitelist, "cleanup" => saved.CleanupRecommendationWhitelist, _ => saved.AiConversationWhitelist });
        settings.Received().SaveSettings(saved);
    }

    [Fact]
    public void 免责声明按板块展示并明确AI密钥与敏感信息边界()
    {
        var document = Load("Views/SettingsSections/DisclaimerSettingsSection.xaml");
        foreach (var key in new[] { "SettingsDisclaimerUsageTitle", "SettingsDisclaimerRiskTitle", "SettingsDisclaimerAiTitle", "SettingsDisclaimerAiPrivacy", "SettingsDisclaimerAiSensitive" })
            Assert.Contains(document.Descendants(), e => (string?)e.Attribute("Text") == "{loc:Loc " + key + "}");
        foreach (var language in new[] { "Strings.resx", "Strings.zh-CN.resx", "Strings.zh-TW.resx" })
        {
            var resources = Load("Localization/" + language);
            foreach (var key in new[] { "SettingsDisclaimerAiPrivacy", "SettingsDisclaimerAiSensitive" })
                Assert.False(string.IsNullOrWhiteSpace(resources.Descendants("data").Single(e => (string?)e.Attribute("name") == key).Element("value")?.Value));
        }
    }

    private static XDocument Load(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(directory.FullName, "src/DiskPilotX.App", relativePath);
            if (File.Exists(file)) return XDocument.Load(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException("未找到设置页源文件：" + relativePath);
    }
}
