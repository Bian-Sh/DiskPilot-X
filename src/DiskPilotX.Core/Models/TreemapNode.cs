namespace DiskPilotX.Core.Models;

public class TreemapNode
{
    public FileEntry Entry { get; set; } = null!;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public string ColorHex { get; set; } = "#2196F3";
    public int Depth { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? Label { get; set; }
}
