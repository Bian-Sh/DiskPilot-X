namespace DiskPilotX.Core.Services.Treemap;

/// <summary>文字使用独立的屏幕空间门槛，不因为内容节点存在就强行显示标题。</summary>
public static class TreemapLabelPolicy
{
    public const float DirectoryTitleScreenHeight = 14f;
    public const float DirectoryTitleFontSize = 10f;
    public const float FileLabelFontSize = 11f;

    /// <summary>标题至多消耗有效分流区域的四分之一；是像素成本，不是标题个数。</summary>
    public static bool HasDirectoryTitleValue(double secondaryShare, double screenHeight) =>
        secondaryShare * screenHeight >= DirectoryTitleScreenHeight * 4;

    public static float DirectoryHeaderHeight(float width, float height, int depth, float zoom)
    {
        zoom = Math.Max(zoom, 0.000001f);
        float w = width * zoom, h = height * zoom;
        // 层级只表达真实关系；文字可读性不能随着“第几个标题”刻板加码。
        return w >= 104f && h >= 52f && w * h >= 6000f ? DirectoryTitleScreenHeight / zoom : 0f;
    }

    public static bool ShowFileLabel(float width, float height, float zoom) =>
        width * zoom >= 64f && height * zoom >= 16f;
}
