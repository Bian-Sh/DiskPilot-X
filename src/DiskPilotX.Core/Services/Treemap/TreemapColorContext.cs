using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Treemap;

public sealed record TreemapTypeColor(string Key, string ColorHex, long SizeBytes, long FileCount);

/// <summary>一次扫描共用的文件类型配色缓存；无图例、无目录用途分类，不在缩放时重扫。</summary>
public sealed class TreemapColorContext
{
    private const string DirectoryColor = "#303030";
    private const string OtherColor = "#8A8A8A";
    private const int MajorTypeLimit = 12;

    // 常见类型各自固定颜色；动态色板与此表、长尾灰色互不重叠。
    private static readonly Dictionary<string, string> FixedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        [".dll"] = "#9B86C9", [".exe"] = "#D3A46C", [".pdb"] = "#70ADBD",
        [".png"] = "#83B18E", [".jpg"] = "#C39BBD", [".jpeg"] = "#B891AA",
        [".cs"] = "#7DA7CC", [".js"] = "#D1BC70", [".ts"] = "#6DA8C5",
        [".py"] = "#90B377", [".rs"] = "#BF917C", [".cpp"] = "#929DCF",
        [".h"] = "#A6A0CE", [".json"] = "#B4AF7D", [".xaml"] = "#C197AD",
        [".html"] = "#C48A7B", [".css"] = "#8996C3", [".zip"] = "#79B5AF",
        [".7z"] = "#98B79F", [".rar"] = "#A3B689", [".gz"] = "#76A69B",
        [".pdf"] = "#C48491", [".txt"] = "#A8B4CA", [".md"] = "#94AEB9",
        [".docx"] = "#83A4B6", [".xlsx"] = "#7DA58C", [".csv"] = "#ACC29A",
        [".mp4"] = "#B38BC4", [".mkv"] = "#AF97C2", [".mp3"] = "#C28BC0",
        [".wav"] = "#C69FCB", [".svg"] = "#99B5C7", [".webp"] = "#B0C58D",
        [".gif"] = "#C3B58C", [".log"] = "#BCA685", [".tmp"] = "#B9AD97",
        [".cache"] = "#ADAB8F", [".bin"] = "#A59CB0", [".dat"] = "#A5ADBA",
        [".obj"] = "#B99E90", [".lib"] = "#90ACA5", [".sys"] = "#A1A9C4",
        [".msi"] = "#C4A57F", [".pak"] = "#B19EA5", [".vhdx"] = "#96A4AD",
        ["no-extension"] = "#C0B8A7"
    };
    private static readonly string[] MajorPalette =
    [
        "#E09E87", "#8BBFC9", "#B9A2DA", "#BCD281", "#D791B4", "#85B5E0",
        "#D4BD8C", "#91C4AA", "#BA9F89", "#A0AEDF", "#C3A8C5", "#9ABCB5"
    ];
    private readonly Dictionary<FileEntry, DirectoryState> _directories = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> _typeSlots = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _slotColors;
    private readonly IReadOnlyList<TreemapTypeColor> _fileTypes;
    private readonly record struct DirectoryState(int DominantSlot, int TailDominantSlot);
    private readonly record struct Totals(long Size, long Count);

    private TreemapColorContext(FileEntry root, TreemapColorContext? previous = null)
    {
        var types = new Dictionary<string, Totals>(StringComparer.OrdinalIgnoreCase);
        // 第一遍只缓存目录。千万文件规模不为每个文件额外保存配色状态或父子副本。
        var pending = new Stack<FileEntry>();
        pending.Push(root);
        while (pending.TryPop(out var item))
        {
            var entry = item;
            if (!entry.IsDirectory)
            {
                CountFile(entry, types);
                continue;
            }
            _directories.Add(entry, new DirectoryState(0, 0));
            foreach (var child in entry.Children)
            {
                if (child.IsDirectory) pending.Push(child);
                else CountFile(child, types);
            }
        }

        var major = new List<KeyValuePair<string, Totals>>(MajorTypeLimit);
        if (previous is not null)
        {
            // 清理投影只重统计存活项，不提升原本的长尾或重分配既有类型的颜色。
            major.AddRange(types.Where(type => previous._typeSlots.ContainsKey(type.Key))
                .OrderByDescending(type => type.Value.Size).ThenBy(type => type.Key, StringComparer.Ordinal));
        }
        else
        {
            // 有界插入选择前十二名；同字节按键名稳定决胜，避免全量扩展名排序。
            foreach (var type in types)
            {
                int index = 0;
                while (index < major.Count && (major[index].Value.Size > type.Value.Size ||
                    (major[index].Value.Size == type.Value.Size && StringComparer.Ordinal.Compare(major[index].Key, type.Key) < 0))) index++;
                if (index >= MajorTypeLimit) continue;
                major.Insert(index, type);
                if (major.Count > MajorTypeLimit) major.RemoveAt(MajorTypeLimit);
            }
        }
        _slotColors = new string[major.Count + 1];
        _slotColors[major.Count] = OtherColor;
        var legend = new List<TreemapTypeColor>(major.Count + 1);
        int paletteIndex = 0;
        for (int slot = 0; slot < major.Count; slot++)
        {
            var type = major[slot];
            _typeSlots.Add(type.Key, slot);
            string color = previous is not null ? previous._slotColors[previous._typeSlots[type.Key]]
                : FixedColors.TryGetValue(type.Key, out var fixedColor) ? fixedColor : MajorPalette[paletteIndex++];
            _slotColors[slot] = color;
            legend.Add(new(type.Key, color, type.Value.Size, type.Value.Count));
        }
        long tailSize = 0, tailCount = 0;
        foreach (var type in types)
            if (!_typeSlots.ContainsKey(type.Key)) { tailSize += type.Value.Size; tailCount += type.Value.Count; }
        if (tailCount > 0) legend.Add(new("other-types", OtherColor, tailSize, tailCount));
        _fileTypes = legend.AsReadOnly();

        if (!root.IsDirectory) return;
        // 第二遍后序聚合，只有当前深度保留至多十三槽数组，完成即释放，不保存每目录字典。
        var frames = new Stack<AggregateFrame>();
        frames.Push(new(root, _slotColors.Length));
        while (frames.TryPeek(out var frame))
        {
            if (frame.NextChild < frame.Entry.Children.Count)
            {
                var child = frame.Entry.Children[frame.NextChild++];
                if (child.IsDirectory) frames.Push(new(child, _slotColors.Length));
                else if (child.Size > 0)
                {
                    int slot = GetSlot(child);
                    frame.Sizes[slot] += child.Size;
                    if (frame.IsTail(child.Size, frame.NextChild - 1)) frame.TailSizes![slot] += child.Size;
                }
                continue;
            }
            int dominant = Dominant(frame.Sizes, _slotColors.Length - 1);
            int tail = frame.TailSizes is null ? dominant : Dominant(frame.TailSizes, dominant);
            _directories[frame.Entry] = new(dominant, tail);
            frames.Pop();
            if (frames.TryPeek(out var parent))
            {
                bool tailChild = parent.IsTail(frame.Entry.Size, parent.NextChild - 1);
                for (int slot = 0; slot < frame.Sizes.Length; slot++)
                {
                    parent.Sizes[slot] += frame.Sizes[slot];
                    if (tailChild) parent.TailSizes![slot] += frame.Sizes[slot];
                }
            }
        }
    }

    public static TreemapColorContext Create(FileEntry root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new(root);
    }

    /// <summary>宿主已完成清理后的模型投影；刷新统计但保持本次扫描中的类型颜色。</summary>
    public TreemapColorContext Refresh(FileEntry root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new(root, this);
    }

    /// <summary>仅供统计校验和诊断使用；不绑定到用户界面。</summary>
    public IReadOnlyList<TreemapTypeColor> FileTypes => _fileTypes;

    public void Apply(IReadOnlyList<TreemapNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        foreach (var node in nodes)
        {
            var entry = node.Entry;
            bool directoryTile = entry.IsDirectory && node.AggregatedCount == 0;
            string content = entry.IsDirectory
                ? _directories.TryGetValue(entry, out var directory)
                    ? _slotColors[node.AggregatedCount > 0 ? directory.TailDominantSlot : directory.DominantSlot] : OtherColor
                : _slotColors[GetSlot(entry)];
            node.ColorHex = directoryTile ? DirectoryColor : content;
            node.ContentColorHex = content;
        }
    }

    public static string GetFallbackFileColor(FileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return FixedColors.TryGetValue(GetExtensionKey(entry), out var color) ? color : OtherColor;
    }

    private int GetSlot(FileEntry entry) => _typeSlots.TryGetValue(GetExtensionKey(entry), out int slot) ? slot : _slotColors.Length - 1;

    private static string GetExtensionKey(FileEntry entry)
    {
        string extension = string.IsNullOrWhiteSpace(entry.Extension) ? System.IO.Path.GetExtension(entry.Name) : entry.Extension;
        return extension.Length == 0 ? "no-extension" : extension.ToLowerInvariant();
    }

    private static void CountFile(FileEntry entry, Dictionary<string, Totals> types)
    {
        if (entry.Size <= 0) return;
        string key = GetExtensionKey(entry);
        types.TryGetValue(key, out var old);
        types[key] = new(old.Size + entry.Size, old.Count + 1);
    }

    private static int Dominant(long[] sizes, int fallback)
    {
        long maximum = 0; int slot = fallback;
        for (int i = 0; i < sizes.Length; i++)
            if (sizes[i] > maximum) { maximum = sizes[i]; slot = i; }
        return slot;
    }

    // 与布局的“前128项、同大小保留原顺序”一致；只缓存边界，不复制海量尾部文件引用。
    private static (long Size, int Index)? TailThreshold(FileEntry entry)
    {
        int limit = SquarifiedTreemapLayout.MaxChildrenPerDir;
        if (entry.Children.Count <= limit) return null;
        var largest = new PriorityQueue<int, (long Size, int Order)>();
        bool overflow = false;
        for (int i = 0; i < entry.Children.Count; i++)
        {
            long size = entry.Children[i].Size;
            if (size <= 0) continue;
            largest.Enqueue(i, (size, -i));
            if (largest.Count > limit) { largest.Dequeue(); overflow = true; }
        }
        if (!overflow) return null;
        largest.TryPeek(out int index, out var priority);
        return (priority.Size, index);
    }

    private sealed class AggregateFrame
    {
        public FileEntry Entry { get; }
        public int NextChild { get; set; }
        public long[] Sizes { get; }
        public long[]? TailSizes { get; }
        private (long Size, int Index)? Threshold { get; }
        public AggregateFrame(FileEntry entry, int slots)
        {
            Entry = entry; Sizes = new long[slots]; Threshold = TailThreshold(entry);
            if (Threshold is not null) TailSizes = new long[slots];
        }
        public bool IsTail(long size, int index) => Threshold is { } threshold
            && (size < threshold.Size || size == threshold.Size && index > threshold.Index);
    }
}
