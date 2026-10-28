using System.Globalization;
using DiskPilotX.App.Localization;

namespace DiskPilotX.App.Tests;

[CollectionDefinition("本地化状态", DisableParallelization = true)]
public sealed class LocalizationStateCollection { }

[Collection("本地化状态")]
public sealed class LocalizationStateTests
{
    [Fact]
    public void 无效语言安全回退而不是崩溃()
    {
        var original = L.CurrentLanguageName;
        try
        {
            var error = Record.Exception(() => L.SetLanguage("not_a_real_culture_123!"));
            Assert.Null(error);
            Assert.False(string.IsNullOrWhiteSpace(L.CurrentLanguageName));
        }
        finally { L.SetLanguage(original); }
    }

    [Fact]
    public void 语言状态读取不依赖调用线程原有文化()
    {
        var original = L.CurrentLanguageName;
        string? observed = null;
        try
        {
            L.SetLanguage("zh-CN");
            var thread = new Thread(() =>
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
                observed = L.CurrentLanguageName;
            });
            thread.Start(); thread.Join();
            Assert.Equal("zh-CN", observed);
        }
        finally { L.SetLanguage(original); }
    }

    [Fact]
    public void 宿主桥通知真实变化并可解除订阅() => StaHost.Run(() =>
    {
        var original = L.GetState().LanguagePreference;
        try
        {
            L.SetLanguage("en-US");
            var bridge = new DiskPilotX.App.Services.DefaultSkillHostBridge("locale-test");
            var observed = new List<string>();
            var token = bridge.SubscribeLocalizationChanged(state => observed.Add(state.CultureName));
            L.SetLanguage("zh-TW"); L.SetLanguage("zh-TW");
            Assert.Equal(new[] { "zh-TW" }, observed);
            Assert.Equal("zh-TW", bridge.GetLocalizationState().ResourceCultureName);
            token.Dispose(); token.Dispose();
            L.SetLanguage("zh-CN");
            Assert.Single(observed);
        }
        finally { L.SetLanguage(original); }
    });

    [Fact]
    public void 自动语言与资源回退各有明确状态() => StaHost.Run(() =>
    {
        var original = L.GetState().LanguagePreference;
        try
        {
            L.SetLanguage("auto");
            Assert.True(L.GetState().IsAuto);
            Assert.Equal(CultureInfo.InstalledUICulture.Name, L.GetState().CultureName);
            L.SetLanguage("zh-HK");
            Assert.Equal("zh-TW", L.GetState().ResourceCultureName);
            L.SetLanguage("fr-FR");
            Assert.Equal("en-US", L.GetState().ResourceCultureName);
            Assert.Equal("fr-FR", CultureInfo.DefaultThreadCurrentUICulture!.Name);
        }
        finally { L.SetLanguage(original); }
    });
}