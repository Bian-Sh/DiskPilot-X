namespace DiskPilotX.Core.Models;

/// <summary>用户声明的可选档位，不代表上游已验证支持；null 集合表示初始全部勾选。</summary>
public sealed class ModelThinkingSettings
{
    public string ControlMode { get; set; } = "budget";
    public List<string>? SupportedLevels { get; set; }
    public string? SelectedLevel { get; set; }
}
