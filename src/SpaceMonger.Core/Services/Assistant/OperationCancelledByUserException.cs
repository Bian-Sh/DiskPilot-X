namespace SpaceMonger.Core.Services.Assistant;

/// <summary>用户主动中断了某个助手操作（如扫描）。刻意不继承 OperationCanceledException，
/// 避免与运行级取消（聊天停止）混淆：前者应作为工具结果反馈给模型，后者直接终止整个运行。</summary>
public sealed class OperationCancelledByUserException(string message) : Exception(message);
