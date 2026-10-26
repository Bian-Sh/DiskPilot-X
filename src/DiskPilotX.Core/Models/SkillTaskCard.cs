namespace DiskPilotX.Core.Models;

/// <summary>聊天中可重新打开的清理任务入口。</summary>
public sealed record SkillTaskCard(string TaskId, string SkillId, string Title, string Summary);
