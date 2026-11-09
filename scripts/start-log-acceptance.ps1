param([Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$EvidenceRoot, [int]$Port = 39249)
$ErrorActionPreference = 'Stop'
$exe = [IO.Path]::GetFullPath((Join-Path $Package 'DiskPilot-X.exe'))
$root = [IO.Path]::GetFullPath($EvidenceRoot)
$outputs = 'D:\AppData\Visual Studio\Projects\DiskPilot-X\outputs\'
if (-not $exe.StartsWith($outputs, [StringComparison]::OrdinalIgnoreCase) -or -not $root.StartsWith($outputs, [StringComparison]::OrdinalIgnoreCase)) { throw '验收只允许统一 outputs 内的包和自建数据目录。' }
if ((Get-Content -LiteralPath (Join-Path $Package '发布验证.json') -Raw -Encoding utf8 | ConvertFrom-Json).Manifest -ne 'asInvoker') { throw '此入口只启动非提权测试包。' }
if (Test-Path -LiteralPath $root) { throw "拒绝覆盖已有验收数据：$root" }
New-Item -ItemType Directory -Path $root | Out-Null
$names = @('DISKPILOT_X_ACCEPTANCE_PIPE','DISKPILOT_X_ACCEPTANCE_PORT','DISKPILOT_X_ACCEPTANCE_SETTINGS','DISKPILOT_X_ACCEPTANCE_LOG_CRASH')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    [Environment]::SetEnvironmentVariable($names[0], '1')
    [Environment]::SetEnvironmentVariable($names[1], "$Port")
    [Environment]::SetEnvironmentVariable($names[2], (Join-Path $root 'settings.json'))
    [Environment]::SetEnvironmentVariable($names[3], '1')
    $process = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 800
    $process = Get-Process -Id $process.Id
    if ($process.Path -ne $exe) { throw '验收进程路径不匹配。' }
    if (-not ('DiskPilotLogToken' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class DiskPilotLogToken {
    [DllImport("advapi32.dll", SetLastError=true)] public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError=true)] public static extern bool GetTokenInformation(IntPtr token, int kind, out int value, int size, out int length);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
"@
    }
    $token = [IntPtr]::Zero
    if (-not [DiskPilotLogToken]::OpenProcessToken($process.Handle, 8, [ref]$token)) { throw '无法读取实际进程权限。' }
    try {
        $elevated = 0; $length = 0
        if (-not [DiskPilotLogToken]::GetTokenInformation($token, 20, [ref]$elevated, 4, [ref]$length)) { throw '无法读取提权标记。' }
        if ($elevated -ne 0) { throw '实际测试进程已提权，不符合自动化验收要求。' }
    } finally { [void][DiskPilotLogToken]::CloseHandle($token) }
    $receipt = [ordered]@{ ProcessId=$process.Id; Path=$process.Path; Elevated=$false; Port=$Port; EvidenceRoot=$root; StartedAt=[DateTimeOffset]::Now.ToString('O') }
    $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'launch.json') -Encoding utf8
    $receipt | ConvertTo-Json
} finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) } }
