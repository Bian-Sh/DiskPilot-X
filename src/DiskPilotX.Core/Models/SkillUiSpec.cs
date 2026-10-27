using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DiskPilotX.Core.Models;

/// <summary>
/// 视图呈现面。气泡里永远只放一张入口卡（不内联渲染数据区块），
/// 这里只决定「除入口卡外，是否再自动把完整视图弹到新窗口」。
/// </summary>
public enum SkillUiSurface
{
    /// <summary>气泡内只放醒目入口卡，用户点击后才在新窗口打开完整视图（默认）。</summary>
    Entry = 0,
    /// <summary>气泡内放入口卡，并立即自动弹出新窗口展示完整视图。</summary>
    Auto = 1,

    // ── 旧契约取值别名，保持向后兼容 ──
    /// <summary>旧值：等价于 <see cref="Entry"/>。</summary>
    Bubble = Entry,
    /// <summary>旧值：等价于 <see cref="Auto"/>。</summary>
    Window = Auto,
    /// <summary>旧值：等价于 <see cref="Auto"/>。</summary>
    Both = Auto
}

/// <summary>区块布局类型。宿主据此选择原生模板，全部继承 VisionPro 主题。</summary>
public enum SkillUiSectionKind
{
    /// <summary>海报网格：固定列数的卡片流，适合游戏封面、应用图标。</summary>
    PosterGrid,
    /// <summary>统计行：大数字 + 标签，适合扫描统计与空间占比。</summary>
    StatRow,
    /// <summary>富表格：列头 + 行数据，单元格可带语义色。</summary>
    Table,
    /// <summary>条形图：标签 + 数值条，适合 Top N 大小对比。</summary>
    BarChart,
    /// <summary>纯文本段落。</summary>
    Text
}

public enum ImageLoadState { None, Loading, Loaded, Failed }

/// <summary>
/// 条目的行内动作（海报卡 / 表格行上的按钮）。
/// 技能只声明「这是一个什么动作」以及作用对象，真正的落地执行一律由宿主完成：
/// 源码既不删文件也不起进程，宿主按 kind 调用对应能力。
/// </summary>
public sealed record SkillUiItemAction
{
    /// <summary>动作类型：uninstall = 交给应用自身的卸载程序（Steam 原生卸载）；open_path = 在资源管理器里定位。</summary>
    public string Kind { get; init; } = "uninstall";

    /// <summary>按钮文案。</summary>
    public string Label { get; init; } = "卸载";

    /// <summary>动作目标：Steam 卸载为 appid，open_path 为待定位的路径。</summary>
    public string? Target { get; init; }

    /// <summary>是否要求宿主执行前弹出强确认（卸载类默认要求）。</summary>
    public bool ConfirmRequired { get; init; } = true;

    public bool IsUninstall => string.Equals(Kind, "uninstall", StringComparison.OrdinalIgnoreCase);
    public bool IsOpenPath => string.Equals(Kind, "open_path", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 视图条目：海报网格 / 统计行 / 表格行 / 条形图 / 文本的通用数据单元。
/// 各区块按自身 Kind 取用所需字段，未使用的字段保持为空。
/// </summary>
public sealed class SkillUiItem : INotifyPropertyChanged
{
    private string _imageUrl = string.Empty;
    private bool _hasImage;
    private ImageLoadState _imageState = ImageLoadState.None;
    private bool _selected;

    public string? Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Value { get; init; }
    public string? ValueLabel { get; init; }

    /// <summary>语义色徽章文本（如 Safe / Caution / 危险）。</summary>
    public string? Badge { get; init; }

    /// <summary>条目对应的本地绝对路径；供后续清理分析定位。</summary>
    public string? Path { get; init; }

    /// <summary>Steam appid 等应用标识；宿主据此确定性构造海报地址。</summary>
    public string? AppId { get; init; }

    /// <summary>数值（条形图用；按区块内最大值归一）。</summary>
    public double Metric { get; init; }

    /// <summary>占用字节数（可选）；用于汇总用户勾选的条目总量。</summary>
    public long SizeBytes { get; init; }

    /// <summary>是否允许用户勾选（用于回传选中项）。</summary>
    public bool Selectable { get; init; }

    /// <summary>表格单元格（Table 区块的每行）。</summary>
    public IReadOnlyList<string> Cells { get; init; } = [];

    /// <summary>行内动作（如游戏卡上的「卸载」）；为空则不渲染按钮。</summary>
    public SkillUiItemAction? Action { get; init; }

    public bool HasAction => Action is not null;

    /// <summary>本行的单元格数量，供表格行均分列宽。</summary>
    public int ColumnCount => Cells.Count;

    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
    }

    public string ImageUrl
    {
        get => _imageUrl;
        set { if (_imageUrl == value) return; _imageUrl = value; OnPropertyChanged(); }
    }

    public bool HasImage
    {
        get => _hasImage;
        private set { if (_hasImage == value) return; _hasImage = value; OnPropertyChanged(); }
    }

    public ImageLoadState ImageState
    {
        get => _imageState;
        set { if (_imageState == value) return; _imageState = value; OnPropertyChanged(); }
    }

    /// <summary>设置图片源并标记可显示；URL 为空则隐藏图片区。</summary>
    public void SetImageUrl(string? url)
    {
        ImageUrl = url ?? string.Empty;
        HasImage = !string.IsNullOrWhiteSpace(url);
        ImageState = HasImage ? ImageLoadState.Loading : ImageLoadState.None;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>视图的一个区块，决定一片区域的布局方式。</summary>
public sealed record SkillUiSection
{
    public SkillUiSectionKind Kind { get; init; }
    public string? Title { get; init; }
    public string? Subtitle { get; init; }

    /// <summary>PosterGrid 的每行列数（1~6）。</summary>
    public int Columns { get; init; } = 3;

    /// <summary>Table 的列头；为空则不渲染表头。</summary>
    public IReadOnlyList<string>? Headers { get; init; }

    public IReadOnlyList<SkillUiItem> Items { get; init; } = [];

    public bool IsPosterGrid => Kind == SkillUiSectionKind.PosterGrid;
    public bool IsStatRow => Kind == SkillUiSectionKind.StatRow;
    public bool IsTable => Kind == SkillUiSectionKind.Table;
    public bool IsBarChart => Kind == SkillUiSectionKind.BarChart;
    public bool IsText => Kind == SkillUiSectionKind.Text;
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    /// <summary>表格列数：优先取列头数量，其次取首行单元格数量。</summary>
    public int ColumnCount => Headers?.Count ?? Items.FirstOrDefault()?.Cells.Count ?? 1;

    /// <summary>条形图归一化基准；无数据或全为 0 时返回 0（调用方按 0 宽度处理）。</summary>
    public double MaxMetric => Items.Count == 0 ? 0 : Items.Max(item => item.Metric);
}

/// <summary>视图动作（窗口底部按钮）。</summary>
public sealed class SkillUiAction
{
    /// <summary>preview = 预览清理候选；continue = 回传选中项并交助手继续；close = 关闭窗口。</summary>
    public string Kind { get; init; } = "close";
    public string Label { get; init; } = "关闭";

    public bool IsPreview => string.Equals(Kind, "preview", StringComparison.OrdinalIgnoreCase);
    public bool IsContinue => string.Equals(Kind, "continue", StringComparison.OrdinalIgnoreCase);
    public bool IsClose => string.Equals(Kind, "close", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 按需构建的视图契约（仿 openui / x-hub 扩展思路）：模型声明「我要一个什么样的界面」，
/// 宿主用原生 WPF 控件渲染，全部继承 VisionPro 主题；并为气泡提供入口卡，
/// 用户感兴趣时可在新窗口中深入观测、按需把操作结果回传。
/// </summary>
public sealed record SkillUiSpec
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public SkillUiSurface Surface { get; init; } = SkillUiSurface.Entry;

    /// <summary>气泡内入口卡标题；留空则回退到 Title。</summary>
    public string? EntryTitle { get; init; }

    /// <summary>气泡内入口卡摘要；留空则回退到 Subtitle。</summary>
    public string? EntrySummary { get; init; }

    /// <summary>呈现主题；当前仅支持继承宿主 VisionPro 主题。</summary>
    public string Theme { get; init; } = "visionpro";

    public IReadOnlyList<SkillUiSection> Sections { get; init; } = [];

    public IReadOnlyList<SkillUiAction> Actions { get; init; } = [new()];

    /// <summary>是否除入口卡外，再立即自动弹出新窗口展示完整视图。</summary>
    public bool AutoOpensWindow => Surface == SkillUiSurface.Auto;

    public string EffectiveEntryTitle => string.IsNullOrWhiteSpace(EntryTitle) ? Title : EntryTitle!;

    public string? EffectiveEntrySummary => string.IsNullOrWhiteSpace(EntrySummary) ? Subtitle : EntrySummary;

    public int SectionCount => Sections.Count;

    /// <summary>
    /// 去重后的数据条目数。同一份视图里，一个游戏常常同时出现在海报网格和明细表里，
    /// 直接相加会把「10 个游戏」显示成「22 项」——按 path（无 path 时按标题）合并。
    /// stat_row / text 是标签与说明，不算数据条目。
    /// </summary>
    public int ItemCount => SummarizeItems().Count;

    public int SelectableItemCount => SummarizeItems().Values.Count(item => item.Selectable);

    public long TotalSizeBytes => SummarizeItems().Values.Sum(item => item.SizeBytes);

    private sealed record ItemSummary(long SizeBytes, bool Selectable);

    private Dictionary<string, ItemSummary> SummarizeItems()
    {
        var map = new Dictionary<string, ItemSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Sections
            .Where(section => section.Kind is SkillUiSectionKind.PosterGrid or SkillUiSectionKind.Table or SkillUiSectionKind.BarChart)
            .SelectMany(section => section.Items))
        {
            var key = ItemKey(item);
            if (key.Length == 0) continue;
            map[key] = map.TryGetValue(key, out var existing)
                ? new ItemSummary(Math.Max(existing.SizeBytes, item.SizeBytes), existing.Selectable || item.Selectable)
                : new ItemSummary(item.SizeBytes, item.Selectable);
        }
        return map;
    }

    private static string ItemKey(SkillUiItem item) =>
        !string.IsNullOrWhiteSpace(item.Path) ? "p:" + item.Path
        : !string.IsNullOrWhiteSpace(item.Title) ? "t:" + item.Title
        : string.Empty;

    /// <summary>
    /// 两份视图是否在讲同一件事（条目重叠过半）。技能源码已经返回一份视图、
    /// 模型又补了一次 render_view 时，用它挡住气泡里的第二张入口卡。
    /// </summary>
    public bool DescribesSameContent(SkillUiSpec other)
    {
        var mine = SummarizeItems().Keys;
        var theirs = other.SummarizeItems().Keys;
        if (mine.Count == 0 || theirs.Count == 0)
            return string.Equals(EffectiveEntryTitle, other.EffectiveEntryTitle, StringComparison.Ordinal);
        var shared = mine.Count(theirs.Contains);
        return shared * 2 >= Math.Min(mine.Count, theirs.Count);
    }

    /// <summary>
    /// 内容指纹：同一份视图被重复投递到同一个气泡时（例如技能源码返回 view 之后
    /// 模型又补了一次 render_view），用它去重，气泡里只留一张入口卡。
    /// </summary>
    public string Signature
    {
        get
        {
            var builder = new System.Text.StringBuilder(Title);
            foreach (var section in Sections)
            {
                builder.Append('|').Append(section.Kind).Append(':').Append(section.Title);
                foreach (var item in section.Items)
                    builder.Append('>').Append(item.Title).Append('~').Append(item.Path).Append('~').Append(item.Value);
            }
            return builder.ToString();
        }
    }
}

/// <summary>
/// 从模型工具参数解析并校验视图契约；非法区块被忽略、超量截断，保证宿主只渲染安全内容。
/// </summary>
public static class SkillUiSpecContract
{
    private const int MaxSections = 8;
    private const int MaxItemsPerSection = 60;
    private const int MaxColumns = 6;
    private const int MaxActions = 3;

    private static readonly HashSet<string> KnownKinds = new(StringComparer.OrdinalIgnoreCase)
        { "poster_grid", "stat_row", "table", "bar_chart", "text" };

    private static readonly HashSet<string> KnownActions = new(StringComparer.OrdinalIgnoreCase)
        { "preview", "continue", "close" };

    /// <summary>支持的条目行内动作：卸载走应用自身卸载程序，定位只打开资源管理器。</summary>
    private static readonly HashSet<string> KnownItemActions = new(StringComparer.OrdinalIgnoreCase)
        { "uninstall", "open_path" };

    public static SkillUiSpec Parse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return new() { Title = "视图" };
        var sections = new List<SkillUiSection>();
        if (payload.TryGetProperty("sections", out var sectionsEl) && sectionsEl.ValueKind == JsonValueKind.Array)
            foreach (var sectionEl in sectionsEl.EnumerateArray())
            {
                if (sections.Count >= MaxSections) break;
                var section = ParseSection(sectionEl);
                if (section is not null) sections.Add(section);
            }
        return new SkillUiSpec
        {
            Title = TrimTo(GetString(payload, "title"), 80) ?? "视图",
            Subtitle = TrimTo(GetString(payload, "subtitle"), 160),
            Surface = ParseSurface(GetString(payload, "surface")),
            EntryTitle = TrimTo(GetString(payload, "entry_title"), 80),
            EntrySummary = TrimTo(GetString(payload, "entry_summary"), 200),
            Sections = sections,
            Actions = ParseActions(payload)
        };
    }

    private static SkillUiSection? ParseSection(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var kindStr = GetString(el, "kind");
        if (kindStr is null || !KnownKinds.Contains(kindStr)) return null;
        var kind = ParseKind(kindStr);
        return new SkillUiSection
        {
            Kind = kind,
            Title = TrimTo(GetString(el, "title"), 80),
            Subtitle = TrimTo(GetString(el, "subtitle"), 160),
            Columns = kind == SkillUiSectionKind.PosterGrid ? Math.Clamp(ReadInt(el, "columns", 3), 1, MaxColumns) : 0,
            Headers = ReadStrings(el, "headers", MaxColumns),
            Items = ParseItems(el, kind)
        };
    }

    private static IReadOnlyList<SkillUiItem> ParseItems(JsonElement el, SkillUiSectionKind kind)
    {
        if (!el.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array) return [];
        var items = new List<SkillUiItem>();
        foreach (var itemEl in itemsEl.EnumerateArray())
        {
            if (items.Count >= MaxItemsPerSection) break;
            var item = ParseItem(itemEl, kind);
            if (item is not null) items.Add(item);
        }
        return items;
    }

    private static SkillUiItem? ParseItem(JsonElement el, SkillUiSectionKind kind)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var item = new SkillUiItem
        {
            Title = TrimTo(GetString(el, "title"), 120),
            Subtitle = TrimTo(GetString(el, "subtitle"), 200),
            Value = TrimTo(GetString(el, "value"), 48),
            ValueLabel = TrimTo(GetString(el, "value_label"), 48),
            Badge = TrimTo(GetString(el, "badge"), 32),
            Path = TrimTo(GetString(el, "path"), 1024),
            AppId = TrimTo(GetString(el, "appid"), 32),
            Metric = ReadDouble(el, "metric"),
            SizeBytes = ReadInt64(el, "size_bytes"),
            Selectable = ReadBool(el, "selectable"),
            Cells = ReadStrings(el, "cells", MaxColumns),
            Action = ParseItemAction(el)
        };
        // 仅海报网格自动补封面地址；表格行不需要图片。
        if (kind == SkillUiSectionKind.PosterGrid)
        {
            var explicitUrl = GetString(el, "image_url");
            if (!string.IsNullOrWhiteSpace(explicitUrl)) item.SetImageUrl(TrimTo(explicitUrl, 2048));
            else if (!string.IsNullOrWhiteSpace(item.AppId)) item.SetImageUrl(SteamPosterUrl(item.AppId!));
        }
        return item;
    }

    private static SkillUiSectionKind ParseKind(string kind) => kind.ToLowerInvariant() switch
    {
        "poster_grid" => SkillUiSectionKind.PosterGrid,
        "stat_row" => SkillUiSectionKind.StatRow,
        "table" => SkillUiSectionKind.Table,
        "bar_chart" => SkillUiSectionKind.BarChart,
        _ => SkillUiSectionKind.Text
    };

    private static SkillUiSurface ParseSurface(string? surface) => (surface ?? "entry").Trim().ToLowerInvariant() switch
    {
        // 旧取值 window/both 都曾是「立即开窗」，统一收敛到 Auto。
        "auto" or "window" or "both" => SkillUiSurface.Auto,
        _ => SkillUiSurface.Entry
    };

    private static IReadOnlyList<SkillUiAction> ParseActions(JsonElement el)
    {
        if (!el.TryGetProperty("actions", out var actionsEl) || actionsEl.ValueKind != JsonValueKind.Array)
            return [new()];
        var actions = new List<SkillUiAction>();
        foreach (var actionEl in actionsEl.EnumerateArray())
        {
            if (actions.Count >= MaxActions) break;
            var kind = GetString(actionEl, "kind");
            if (kind is null || !KnownActions.Contains(kind)) continue;
            actions.Add(new SkillUiAction
            {
                Kind = kind.ToLowerInvariant(),
                Label = TrimTo(GetString(actionEl, "label"), 24) ?? DefaultLabel(kind)
            });
        }
        return actions.Count > 0 ? actions : [new()];
    }

    /// <summary>解析条目的行内动作；未知类型一律忽略，保证宿主只执行认识的动作。</summary>
    private static SkillUiItemAction? ParseItemAction(JsonElement el)
    {
        if (!el.TryGetProperty("action", out var actionEl) || actionEl.ValueKind != JsonValueKind.Object) return null;
        var kind = GetString(actionEl, "kind");
        if (kind is null || !KnownItemActions.Contains(kind)) return null;
        kind = kind.ToLowerInvariant();
        var uninstall = string.Equals(kind, "uninstall", StringComparison.Ordinal);
        return new SkillUiItemAction
        {
            Kind = kind,
            Label = TrimTo(GetString(actionEl, "label"), 16) ?? (uninstall ? "卸载" : "打开位置"),
            // Steam 卸载的目标就是 appid；条目本身已有 appid 时允许省略 target。
            Target = TrimTo(GetString(actionEl, "target"), 64) ?? TrimTo(GetString(el, "appid"), 32),
            // 只认显式写 confirm=false 的跳过确认，缺省一律要求强确认。
            ConfirmRequired = !actionEl.TryGetProperty("confirm", out var confirmEl) || confirmEl.ValueKind != JsonValueKind.False
        };
    }

    private static string DefaultLabel(string kind) => kind.ToLowerInvariant() switch
    {
        "preview" => "预览清理候选",
        "continue" => "回传选中项继续",
        _ => "关闭"
    };

    private static string? GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static IReadOnlyList<string> ReadStrings(JsonElement el, string name, int max)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        var list = new List<string>();
        foreach (var e in arr.EnumerateArray())
        {
            if (list.Count >= max) break;
            if (e.ValueKind == JsonValueKind.String) list.Add(TrimTo(e.GetString(), 120) ?? "");
        }
        return list;
    }

    private static int ReadInt(JsonElement el, string name, int fallback) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : fallback;

    private static long ReadInt64(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;

    private static double ReadDouble(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;

    private static bool ReadBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? TrimTo(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>Steam 竖版海报的确定性 URL（appid → library_600x900_2x.jpg）。</summary>
    public static string SteamPosterUrl(string appId) => $"https://steamcdn-a.akamaihd.net/steam/apps/{appId}/library_600x900_2x.jpg";

    /// <summary>
    /// 统一规整：海报网格里凡是有 appid 但没显式封面的条目，一律按 appid 补齐确定性封面地址。
    /// 模型走 JSON 解析时会在 <see cref="Parse"/> 里补，但技能源码直接构造 <see cref="SkillUiItem"/>
    /// 这条路径绕过了解析器，宿主必须在这里统一兜住，否则窗口里的海报全是灰底。
    /// </summary>
    public static SkillUiSpec Normalize(SkillUiSpec spec)
    {
        foreach (var section in spec.Sections)
        {
            if (section.Kind != SkillUiSectionKind.PosterGrid) continue;
            foreach (var item in section.Items)
                if (!item.HasImage && !string.IsNullOrWhiteSpace(item.AppId)) item.SetImageUrl(SteamPosterUrl(item.AppId!));
        }
        return spec;
    }
}
