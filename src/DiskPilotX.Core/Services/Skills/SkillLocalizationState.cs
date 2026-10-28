using System.Globalization;

namespace DiskPilotX.Core.Services.Skills;

/// <summary>宿主只读语言快照：用户偏好、有效文化及实际资源语言彼此分开。</summary>
public sealed record SkillLocalizationState(string LanguagePreference, string CultureName, string ResourceCultureName)
{
    public bool IsAuto => string.Equals(LanguagePreference, "auto", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<string> SupportedLanguages => Array.AsReadOnly(new[] { "en-US", "zh-CN", "zh-TW" });

    public static SkillLocalizationState FromCulture(CultureInfo culture) => new(culture.Name, culture.Name, ResourceCulture(culture));

    public static string ResourceCulture(CultureInfo culture)
    {
        var name = culture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) || name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) || name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
                ? "zh-TW" : "zh-CN";
        return "en-US";
    }
}

/// <summary>旧宿主的空订阅及真正宿主的可释放订阅共用同一生命周期契约。</summary>
public sealed class SkillLocalizationSubscription(Action? unsubscribe = null) : IDisposable
{
    private Action? unsubscribe = unsubscribe;
    public void Dispose() => Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
}
