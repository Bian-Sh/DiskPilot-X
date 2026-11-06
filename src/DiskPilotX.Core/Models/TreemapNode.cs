namespace DiskPilotX.Core.Models;

public class TreemapNode
{
    public FileEntry Entry { get; set; } = null!;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public string ColorHex { get; set; } = "#2196F3";
    /// <summary>非空目录折叠时仍显示内容占用色，标题带单独保留中性色。</summary>
    public string? ContentColorHex { get; set; }
    /// <summary>标题容量；绘制时紧接名称并用括号包裹，名称截断不影响容量完整性。</summary>
    public string? SizeLabel { get; set; }
    /// <summary>长列表尾部的聚合数量；交互仍指向真实父目录，不创建虚假文件。</summary>
    public int AggregatedCount { get; set; }
    public long? AggregatedSize { get; set; }
    /// <summary>只有被选为可见容器的目录才绘制常态外框；透明分组仍参与真实路径命中。</summary>
    public bool ShowDirectoryFrame { get; set; } = true;
    /// <summary>布局确定的实际标题高度；null 保留手工节点的兼容测量行为。</summary>
    public float? HeaderHeight { get; set; }
    /// <summary>没有展开后代的内容叶子；透明的已展开目录不能用纯色覆盖内部细节。</summary>
    public bool IsContentLeaf { get; set; } = true;
    public int Depth { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? Label { get; set; }
}
