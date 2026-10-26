using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DiskPilotX.Core.Models;

public enum ChatSegmentKind { Text, Thinking, Tool }
public enum ChatSegmentStatus { Running, Completed, Failed, Cancelled, WaitingForUser }

/// <summary>一条消息中按实际发生顺序展示的正文、思考或工具过程。</summary>
public sealed class ChatSegment(ChatSegmentKind kind) : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string? _detail;
    private string? _toolName;
    private bool _isExpanded;
    private ChatSegmentStatus _status = ChatSegmentStatus.Running;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ChatSegmentKind Kind { get; } = kind;
    public string? ToolCallId { get; init; }
    public bool IsText => Kind == ChatSegmentKind.Text;
    public bool IsThinking => Kind == ChatSegmentKind.Thinking;
    public bool IsTool => Kind == ChatSegmentKind.Tool;
    public bool IsRunning => Status == ChatSegmentStatus.Running;
    public bool IsFailed => Status == ChatSegmentStatus.Failed;
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool IsSummary { get; set; }
    public bool IsFinal { get; set; }

    public string Text
    {
        get => _text;
        set { if (_text == value) return; _text = value; Notify(); Notify(nameof(HasText)); Notify(nameof(Preview)); }
    }

    public string? ToolName
    {
        get => _toolName;
        set { if (_toolName == value) return; _toolName = value; Notify(); }
    }

    public string? Detail
    {
        get => _detail;
        set { if (_detail == value) return; _detail = value; Notify(); Notify(nameof(HasDetail)); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; Notify(); }
    }

    public ChatSegmentStatus Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; Notify(); Notify(nameof(IsRunning)); Notify(nameof(IsFailed)); }
    }

    public string Preview
    {
        get
        {
            using var reader = new StringReader(Text.Replace("[REDACTED]", string.Empty).Trim());
            var line = (reader.ReadLine() ?? string.Empty).Trim().Trim('*', '#', ' ');
            return line.Length <= 96 ? line : line[..96] + "…";
        }
    }

    /// <summary>只格式化已脱敏的展示数据，保留路径与字符串的 JSON 语义。</summary>
    public static string FormatJson(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            var formatted = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            return ReadableSurrogates(formatted);
        }
        catch (JsonException) { return text; }
    }

    // JSON 编码器仍会转义非 BMP 字符；只还原有效代理对，跳过已转义的反斜杠和控制字符。
    private static string ReadableSurrogates(string json)
    {
        var readable = new StringBuilder(json.Length);
        for (var i = 0; i < json.Length; i++)
        {
            if (json[i] == '\\' && i + 1 < json.Length)
            {
                if (json[i + 1] == '\\')
                {
                    readable.Append(json, i, 2);
                    i++;
                    continue;
                }
                if (i + 11 < json.Length && json[i + 1] == 'u' && json[i + 6] == '\\' && json[i + 7] == 'u'
                    && ushort.TryParse(json.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var high)
                    && ushort.TryParse(json.AsSpan(i + 8, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var low)
                    && char.IsHighSurrogate((char)high) && char.IsLowSurrogate((char)low))
                {
                    readable.Append((char)high).Append((char)low);
                    i += 11;
                    continue;
                }
            }
            readable.Append(json[i]);
        }
        return readable.ToString();
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
