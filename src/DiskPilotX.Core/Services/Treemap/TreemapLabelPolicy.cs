namespace DiskPilotX.Core.Services.Treemap;

/// <summary>文字使用独立的屏幕空间门槛，不因为内容节点存在就强行显示标题。</summary>
public static class TreemapLabelPolicy
{
    public static float DirectoryHeaderHeight(float width, float height, int depth, float zoom)
    {
        zoom = Math.Max(zoom, 0.000001f);
        float w = width * zoom, h = height * zoom;
        float minWidth = depth <= 1 ? 104f : depth == 2 ? 144f : 176f;
        float minHeight = depth <= 1 ? 52f : depth == 2 ? 80f : 104f;
        float minArea = depth <= 1 ? 6000f : depth == 2 ? 18000f : 28000f;
        return w >= minWidth && h >= minHeight && w * h >= minArea ? 18f / zoom : 0f;
    }

    public static bool ShowFileLabel(float width, float height, float zoom) =>
        width * zoom >= 160f && height * zoom >= 64f && width * height * zoom * zoom >= 20000f;
}
