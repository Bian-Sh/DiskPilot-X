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
    /// <summary>与名称分开绘制的弱化容量标注。</summary>
    public string? SizeLabel { get; set; }
    /// <summary>长列表尾部的聚合数量；交互仍指向真实父目录，不创建虚假文件。</summary>
    public int AggregatedCount { get; set; }
    public long? AggregatedSize { get; set; }
    public int Depth { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? Label { get; set; }
}
