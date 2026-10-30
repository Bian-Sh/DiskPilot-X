using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace DiskPilotX.Core;

/// <summary>仅用于本地用户数据：中文直接写入 UTF-8，继续转义 HTML 敏感字符。</summary>
public static class UserDataJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = true
    };
}
