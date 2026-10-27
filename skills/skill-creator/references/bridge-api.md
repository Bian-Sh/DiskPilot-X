# API Bridge（context.Host）

> **何时读我**：源码要读注册表、枚举目录、统计大小、查询宿主扫描结果时。**先在这里确认方法名与签名**，再写代码。

宿主在调用 `DiscoverAsync` / `DetectAsync` 时通过 `context.Host` 注入能力桥。源码编译时**只引用** `System.*` 与 `DiskPilotX.Core`（契约），**不含** `Microsoft.Win32.Registry`——一切本机读取都经由本桥，由宿主统一审计与降级。

## 成员一览

| 成员 | 签名 | 说明 |
|---|---|---|
| `SkillId` | `string` | 当前 skill 标识 |
| `Parameters` | `IReadOnlyDictionary<string,string>` | 任务面板表单提交的只读参数（宿主已限量校验） |
| `Log` | `void Log(string message)` | 写宿主诊断日志，用户在控制台可见 |
| `ReadRegistry` | `string? ReadRegistry(string keyPath, string valueName)` | **只读**读注册表字符串值 |
| `EnumerateDirectories` | `IReadOnlyList<string> EnumerateDirectories(string path)` | 枚举子目录 |
| `EnumerateFiles` | `IReadOnlyList<string> EnumerateFiles(string path, string searchPattern = "*")` | 枚举文件 |
| `DirectoryExists` | `bool DirectoryExists(string path)` | 目录是否存在 |
| `FileExists` | `bool FileExists(string path)` | 文件是否存在 |
| `ReadTextFile` | `string? ReadTextFile(string path)` | 读文本文件内容 |
| `GetDirectorySize` | `long GetDirectorySize(string path)` | 递归统计目录占用字节数 |
| `QueryScan` | `IReadOnlyList<SkillHostEntry> QueryScan(string? rootPath = null, int maxCount = 20000)` | 查询宿主已扫描快照 |

## 关键语义

### `ReadRegistry` 的 WOW64 兼容

32 位程序装在 64 位系统上会落到 `WOW6432Node`。宿主已自动处理：**你只需给常规路径**，宿主会依次尝试：

1. `HKLM\<keyPath>`
2. `HKCU\<keyPath>`
3. `HKLM\SOFTWARE\WOW6432Node\<其余路径>`（当 `keyPath` 以 `SOFTWARE\` 开头时）
4. `HKCU\SOFTWARE\WOW6432Node\<其余路径>`

所以写 `SOFTWARE\Valve\Steam` 即可，**不要自己拼 `WOW6432Node`**。键不存在或无权限返回 `null`，不抛异常。

### 所有路径必须是完全限定的绝对路径

`Enumerate*` / `ReadTextFile` / `GetDirectorySize` 会先 `Path.GetFullPath` 校验；相对路径直接返回空集合 / `0`。这样源码无法靠"当前目录"逃逸到意外位置。

### `QueryScan` 返回宿主快照

```csharp
public sealed record SkillHostEntry(string Path, long SizeBytes, string Kind, DateTime LastWriteTime);
```

优先用它，避免源码自己再遍历一遍磁盘（慢且可能权限报错）。`rootPath` 为空表示全量；超 `maxCount` 时按体积降序截断，返回的是**最大的那些**。

---

## 硬约束（违反会被拒绝）

| 禁止 | 原因 |
|---|---|
| 写文件 / 建目录 / 删文件 | 源码只读；清理必须走宿主确认链路 |
| `Process.Start` | 不启进程 |
| `HttpClient` / `Socket` / 任何网络访问 | 不联网 |
| 读凭据文件（密钥、token） | 不读凭据 |
| 同时实现两个契约接口 | 编译期拒绝 |
| 多个顶层公开实现类 | 编译期拒绝 |
| 返回超过 20000 条 | 运行期拒绝 |

检查 `cancellationToken`，在长遍历里周期性 `ThrowIfCancellationRequested()`。

---

## 常用写法：先定位根目录，再枚举

```csharp
public Task<AppDataDiscoveryResult> DiscoverAsync(AppDataDiscoveryContext context, CancellationToken token)
{
    var host = context.Host;
    var root = host.ReadRegistry(@"SOFTWARE\Valve\Steam", "InstallPath")
            ?? host.ReadRegistry(@"SOFTWARE\Valve\Steam", "SteamPath");
    if (string.IsNullOrWhiteSpace(root) || !host.DirectoryExists(root))
    {
        host.Log("未通过注册表定位到客户端安装目录");
        return Task.FromResult(new AppDataDiscoveryResult([], null));
    }
    // 枚举 root 下的内容…
}
```

找不到时**如实返回空结果并 Log 原因**，不要编造路径或返回示例数据。
