using System.Globalization;
using System.Text;
using System.Text.Json;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Skills;

/// <summary>技能自有文案的内存快照；不接收路径，也不读取本机文件。</summary>
public sealed class SkillTextCatalog
{
    public const string DefaultCultureName = "en-US";
    private readonly Dictionary<string, Dictionary<string, string>> tables = new(StringComparer.OrdinalIgnoreCase);

    public SkillTextCatalog(IReadOnlyDictionary<string, Dictionary<string, string>>? localizations)
    {
        if (localizations is null) return;
        foreach (var (name, entries) in localizations)
            if (TryResolveCulture(name, out var culture) && entries is not null)
                tables[culture.Name] = new(entries, StringComparer.Ordinal);
    }

    /// <summary>精确文化优先，其次同文字文化、父文化、英语；缺失时返回原键。</summary>
    public string Get(string key, string? culture)
    {
        ArgumentNullException.ThrowIfNull(key);
        var resolved = ResolveCulture(culture);
        foreach (var name in FallbackCultures(resolved))
            if (tables.TryGetValue(name, out var entries) && entries.TryGetValue(key, out var value))
                return value;
        return key;
    }

    /// <summary>格式化跟随有效请求文化，而非最终命中的文案文化。</summary>
    public string Format(string key, string? culture, params object?[] args) =>
        string.Format(ResolveCulture(culture), Get(key, culture), args);

    /// <summary>绑定键与数据而非提前翻译；窗口语言变化时可重新解析，不需要重新执行技能。</summary>
    public static string Bind(string key, params object?[] args)
    {
        if (SkillUiDocument.LocalizationKey("{loc " + key + "}") is null) throw new ArgumentException("本地化键无效。", nameof(key));
        if (args.Length == 0) return "{loc " + key + "}";
        if (args.Length > 32) throw new ArgumentException("本地化模板参数最多32项。", nameof(args));
        var values = args.Select(value => value switch
        {
            DateTime date => (object)new Dictionary<string,string> { ["date_time"] = date.ToString("O",CultureInfo.InvariantCulture) },
            DateTimeOffset date => new Dictionary<string,string> { ["date_time_offset"] = date.ToString("O",CultureInfo.InvariantCulture) },
            null or string or bool or byte or short or int or long or float or double or decimal => value,
            _ => throw new ArgumentException("模板参数仅允许文本、数字、布尔值与日期。", nameof(args))
        }).ToArray();
        var json = JsonSerializer.SerializeToUtf8Bytes(new BoundText(key, values));
        if (json.Length > 12000) throw new ArgumentException("本地化模板参数超过12KB。", nameof(args));
        return "{loc64 " + Convert.ToBase64String(json) + "}";
    }

    public string Resolve(string value, string? culture) => Resolve(value, culture, 0);
    private sealed record BoundText(string Key, object?[] Args);

    private string Resolve(string value, string? culture, int depth)
    {
        if (depth > 8 || string.IsNullOrEmpty(value)) return value;
        if (SkillUiDocument.LocalizationKey(value) is { } key) return Get(key,culture);
        if (!value.StartsWith("{loc64 ",StringComparison.Ordinal) || !value.EndsWith('}') || value.Length > 16100) return value;
        try
        {
            var bytes = Convert.FromBase64String(value[7..^1]);
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            var name = root.GetProperty("Key").GetString()!;
            if (SkillUiDocument.LocalizationKey("{loc " + name + "}") is null) return value;
            var args = root.GetProperty("Args");
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() > 32) return value;
            object? Read(JsonElement element) => element.ValueKind switch
            {
                JsonValueKind.String => Resolve(element.GetString()!,culture,depth+1),
                JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
                JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null,
                JsonValueKind.Object when element.TryGetProperty("date_time",out var date) => DateTime.Parse(date.GetString()!,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),
                JsonValueKind.Object when element.TryGetProperty("date_time_offset",out var offset) => DateTimeOffset.Parse(offset.GetString()!,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),
                _ => throw new FormatException("不支持的本地化参数类型。")
            };
            var template = Get(name,culture);
            try { return string.Format(ResolveCulture(culture),template,args.EnumerateArray().Select(Read).ToArray()); }
            catch (FormatException) { return template; }
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { return value; }
    }

    private static CultureInfo ResolveCulture(string? name) =>
        TryResolveCulture(name, out var culture) ? culture : CultureInfo.GetCultureInfo(DefaultCultureName);

    internal static bool TryResolveCulture(string? name, out CultureInfo culture)
    {
        culture = CultureInfo.InvariantCulture;
        // 文化标识不是路径；拒绝分隔符、数据流、空白及过长输入。
        if (string.IsNullOrEmpty(name) || name.Length > 85 ||
            name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return false;
        try
        {
            culture = CultureInfo.GetCultureInfo(name);
            return culture.Name.Length > 0;
        }
        catch (CultureNotFoundException) { return false; }
    }

    private static IEnumerable<string> FallbackCultures(CultureInfo culture)
    {
        for (var current = culture; current.Name.Length > 0; current = current.Parent)
        {
            yield return current.Name;
            if (!current.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) continue;
            // 繁体区域共用繁体资源，绝不回落到简体；脚本标识与常用区域互通。
            var traditional = current.Name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
                current.Name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                current.Name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
                current.Name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase);
            if (current.Name.Equals("zh", StringComparison.OrdinalIgnoreCase)) continue;
            yield return traditional ? "zh-Hant" : "zh-Hans";
            yield return traditional ? "zh-TW" : "zh-CN";
        }
        yield return DefaultCultureName;
        yield return "en";
    }
}