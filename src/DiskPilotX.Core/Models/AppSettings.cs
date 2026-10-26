using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models.Theme;
using DiskPilotX.Core.Services.Llm;

namespace DiskPilotX.Core.Models;

public class AppSettings
{
    public byte[]? EncryptedApiKey { get; set; }
    public bool IsApiKeyValid { get; set; } = false;
    public DeletionMode DeletionMode { get; set; } = DeletionMode.MoveToRecycleBin;
    public string? LastScanPath { get; set; }
    public string? AnthropicBaseUrl { get; set; }
    public string? ChatModelName { get; set; }
    /// <summary>模型目录的完整获取地址；留空时沿用接口格式默认路径。</summary>
    public string? ModelsUrl { get; set; }
    /// <summary>按 CLI 身份保存最后成功获取的版本号。</summary>
    public Dictionary<string, string> CliIdentityVersions { get; set; } = [];
    public bool EnableThinking { get; set; } = false;
    public string Language { get; set; } = "zh-CN";

    // ── 模型接入扩展（字符串持久化，读取时容错解析；空值回落历史默认） ──
    /// <summary>接口格式："anthropic-messages" | "openai-responses" | "openai-chat-completions"。</summary>
    public string? ApiFormat { get; set; }
    /// <summary>模拟的 CLI 客户端指纹："none" | "claude-code" | "codex" | "grok"。</summary>
    public string? CliIdentity { get; set; }
    /// <summary>思考等级："off" | "minimal" | "low" | "medium" | "high"。</summary>
    public string? ThinkingLevel { get; set; }
    /// <summary>代理模式："system" | "direct" | "custom"。</summary>
    public string? ProxyMode { get; set; }
    public string? ProxyHost { get; set; }
    public int? ProxyPort { get; set; }
    public string? ProxyUsername { get; set; }
    public byte[]? EncryptedProxyPassword { get; set; }
    /// <summary>最近一次发现的模型 ID 缓存，供聊天与设置界面下拉选择。</summary>
    public List<string> DiscoveredModels { get; set; } = [];

    /// <summary>Serialized theme profile (or null = use default).</summary>
    public ThemeProfile? ThemeProfile { get; set; }

    public List<PathWhitelistEntry> ScanWhitelist { get; set; } = [];
    public List<PathWhitelistEntry> CleanupRecommendationWhitelist { get; set; } = [];
    public List<PathWhitelistEntry> AiConversationWhitelist { get; set; } = [];
}

public class PathWhitelistEntry
{
    public string Path { get; set; } = string.Empty;
    public string? Description { get; set; }
}
