using System.Text;

namespace SpaceMonger.Core.Services.Llm;

/// <summary>
/// 模型流协议故障的现场转储。仅在客户端判定"协议错误/流未完整结束"时，
/// 把最近收到的 SSE 原始帧（模型输出：思考、正文、工具参数）写入本地诊断目录，
/// 用于事后定位"模型响应无效或流未完整结束"类故障——此类失败此前只有一句笼统报错，
/// 无法归因到具体校验点，也没有任何原始帧可查。
/// 默认关闭；由宿主启动时调用 <see cref="Configure"/> 指定目录。帧内容为模型输出，
/// 敏感度与本地对话快照同级，仅保存在用户本机。
/// </summary>
public static class LlmStreamDiagnostics
{
    /// <summary>单份转储保留的原始帧上限（字符），只保留流尾部的最后一段。</summary>
    internal const int MaxTailChars = 48 * 1024;

    private const int MaxRetainedFiles = 20;
    private static readonly object Gate = new();
    private static string? directory;

    /// <summary>宿主启动时设置转储目录（通常为应用日志目录）；不调用或传空则完全不落盘。</summary>
    public static void Configure(string? logDirectory) =>
        directory = string.IsNullOrWhiteSpace(logDirectory) ? null : Path.Combine(logDirectory, "llm-streams");

    /// <summary>把一帧原始数据追加到尾部缓冲；超过上限时从头截断，永不抛出。</summary>
    public static void Capture(StringBuilder tail, string payload)
    {
        try
        {
            if (tail.Length >= MaxTailChars) return;
            tail.Append(payload.Replace('\n', ' ')).Append('\n');
            if (tail.Length > MaxTailChars)
            {
                tail.Remove(0, tail.Length - MaxTailChars);
                tail.Insert(0, "…（更早的帧已截断）…\n");
            }
        }
        catch { /* 诊断缓冲永不影响流解析 */ }
    }

    /// <summary>写一份故障现场转储；任何失败都被吞掉，绝不影响主流程。</summary>
    public static void WriteDump(string protocol, Uri? endpoint, string? model, string reason, string? tail)
    {
        try
        {
            lock (Gate)
            {
                if (directory is null) return;
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory,
                    $"llm-stream-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}.txt");
                File.WriteAllText(path, Render(protocol, endpoint, model, reason, tail));
                Prune();
            }
        }
        catch { /* 诊断转储永不影响主流程 */ }
    }

    private static string Render(string protocol, Uri? endpoint, string? model, string reason, string? tail)
    {
        var endpointText = endpoint?.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.SafeUnescaped) ?? "(unknown)";
        var header = $"""
            SpaceMonger 模型流协议故障现场
            时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}
            协议: {protocol}
            端点: {endpointText}
            模型: {model ?? "(unknown)"}
            原因: {reason}
            说明: 以下为故障前最近收到的 SSE data 帧（模型输出）。密钥等凭据不会出现在请求或转储中。
            """;
        return header + "\n\n" + (string.IsNullOrWhiteSpace(tail) ? "(未捕获到任何帧)" : tail.TrimEnd()) + "\n";
    }

    private static void Prune()
    {
        try
        {
            if (directory is null) return;
            var files = Directory.EnumerateFiles(directory, "llm-stream-*.txt")
                .Select(file => new FileInfo(file))
                .OrderByDescending(file => file.CreationTimeUtc)
                .ToArray();
            foreach (var file in files.Skip(MaxRetainedFiles))
            {
                try { file.Delete(); } catch { /* 单个文件删除失败忽略 */ }
            }
        }
        catch { /* 清理失败忽略 */ }
    }
}
