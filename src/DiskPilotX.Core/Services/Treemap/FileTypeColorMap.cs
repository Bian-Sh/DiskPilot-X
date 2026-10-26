namespace DiskPilotX.Core.Services.Treemap;

public static class FileTypeColorMap
{
    private const string MediaColor = "#42A5F5";
    private const string DocumentsColor = "#66BB6A";
    private const string ExecutablesColor = "#EF5350";
    private const string ArchivesColor = "#FFA726";
    private const string TemporaryColor = "#BDBDBD";
    private const string SystemColor = "#AB47BC";
    private const string SourceCodeColor = "#26C6DA";
    private const string OtherColor = "#8D6E63";
    private const string FolderColor = "#78909C";

    private static readonly Dictionary<string, string> ExtensionColorMap = new()
    {
        // Media (blue)
        [".jpg"] = MediaColor,
        [".jpeg"] = MediaColor,
        [".png"] = MediaColor,
        [".gif"] = MediaColor,
        [".bmp"] = MediaColor,
        [".svg"] = MediaColor,
        [".mp4"] = MediaColor,
        [".mp3"] = MediaColor,
        [".wav"] = MediaColor,
        [".avi"] = MediaColor,
        [".mkv"] = MediaColor,
        [".flac"] = MediaColor,
        [".mov"] = MediaColor,
        [".wmv"] = MediaColor,
        [".webm"] = MediaColor,
        [".webp"] = MediaColor,

        // Documents (green)
        [".pdf"] = DocumentsColor,
        [".doc"] = DocumentsColor,
        [".docx"] = DocumentsColor,
        [".xls"] = DocumentsColor,
        [".xlsx"] = DocumentsColor,
        [".ppt"] = DocumentsColor,
        [".pptx"] = DocumentsColor,
        [".txt"] = DocumentsColor,
        [".csv"] = DocumentsColor,
        [".rtf"] = DocumentsColor,
        [".odt"] = DocumentsColor,
        [".ods"] = DocumentsColor,

        // Executables (red)
        [".exe"] = ExecutablesColor,
        [".dll"] = ExecutablesColor,
        [".msi"] = ExecutablesColor,
        [".bat"] = ExecutablesColor,
        [".cmd"] = ExecutablesColor,
        [".ps1"] = ExecutablesColor,
        [".com"] = ExecutablesColor,
        [".scr"] = ExecutablesColor,

        // Archives (orange)
        [".zip"] = ArchivesColor,
        [".rar"] = ArchivesColor,
        [".7z"] = ArchivesColor,
        [".tar"] = ArchivesColor,
        [".gz"] = ArchivesColor,
        [".bz2"] = ArchivesColor,
        [".xz"] = ArchivesColor,
        [".cab"] = ArchivesColor,

        // Temporary (gray)
        [".tmp"] = TemporaryColor,
        [".log"] = TemporaryColor,
        [".bak"] = TemporaryColor,
        [".cache"] = TemporaryColor,
        [".old"] = TemporaryColor,

        // System (purple)
        [".sys"] = SystemColor,
        [".dat"] = SystemColor,
        [".ini"] = SystemColor,
        [".reg"] = SystemColor,
        [".drv"] = SystemColor,

        // Source Code (cyan)
        [".cs"] = SourceCodeColor,
        [".js"] = SourceCodeColor,
        [".ts"] = SourceCodeColor,
        [".py"] = SourceCodeColor,
        [".java"] = SourceCodeColor,
        [".cpp"] = SourceCodeColor,
        [".c"] = SourceCodeColor,
        [".h"] = SourceCodeColor,
        [".xaml"] = SourceCodeColor,
        [".html"] = SourceCodeColor,
        [".css"] = SourceCodeColor,
        [".json"] = SourceCodeColor,
        [".xml"] = SourceCodeColor,
        [".yaml"] = SourceCodeColor,
        [".yml"] = SourceCodeColor,
        [".md"] = SourceCodeColor,
        [".rs"] = SourceCodeColor,
        [".go"] = SourceCodeColor,
        [".rb"] = SourceCodeColor,
        [".php"] = SourceCodeColor,
    };

    private static readonly List<(string Category, string ColorHex)> LegendItems = new()
    {
        ("Media", MediaColor),
        ("Documents", DocumentsColor),
        ("Executables", ExecutablesColor),
        ("Archives", ArchivesColor),
        ("Temporary", TemporaryColor),
        ("System", SystemColor),
        ("Source Code", SourceCodeColor),
        ("Other", OtherColor),
        ("Folder", FolderColor),
    };

    public static string GetColorHex(string? extension, bool isDirectory)
    {
        if (isDirectory)
            return FolderColor;

        if (string.IsNullOrEmpty(extension))
            return OtherColor;

        return ExtensionColorMap.TryGetValue(extension.ToLowerInvariant(), out var color)
            ? color
            : OtherColor;
    }

    public static List<(string Category, string ColorHex)> GetLegendItems()
    {
        return new List<(string Category, string ColorHex)>(LegendItems);
    }
}
