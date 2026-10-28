param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Configuration = 'Release',
    [switch]$TestPackage
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号必须为三段数字。' }
$root = Split-Path $PSScriptRoot -Parent
$target = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $target) { throw "拒绝覆盖已有发布目录：$target" }
New-Item -ItemType Directory -Path $target | Out-Null
$artifacts = Join-Path ([IO.Path]::GetTempPath()) ('DiskPilotX-release-build-' + [guid]::NewGuid().ToString('N'))
$source = [ordered]@{ SourceRoot=$root; Branch=(& git -C $root branch --show-current); Commit=(& git -C $root rev-parse HEAD); HasUncommittedChanges=(@(& git -C $root status --porcelain).Count -gt 0); Version=$Version; TestPackage=$TestPackage; Output=$target }
$source | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target '发布来源.json') -Encoding utf8
& dotnet publish (Join-Path $root 'src/DiskPilotX.App/DiskPilotX.App.csproj') -c $Configuration -r win-x64 `
    --artifacts-path $artifacts -t:Rebuild --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false `
    "-p:Version=$Version" "-p:AssemblyVersion=$Version.0" "-p:FileVersion=$Version.0" `
    "-p:DiskPilotTestPackage=$($TestPackage.ToString().ToLowerInvariant())" -o $target
if ($LASTEXITCODE) { throw "发布失败：$LASTEXITCODE" }
foreach ($required in @('DiskPilot-X.exe','DiskPilot-X.dll','DiskPilotX.Core.dll','System.Private.CoreLib.dll','skills')) {
    if (-not (Test-Path -LiteralPath (Join-Path $target $required))) { throw "发布缺失：$required" }
}
# 实际读取apphost的RT_MANIFEST资源，避免仅凭构建参数误判清单。
if (-not ('DiskPilotReleaseManifest' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class DiskPilotReleaseManifest {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] public static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] public static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] public static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr module);
}
"@
}
$module = [DiskPilotReleaseManifest]::LoadLibraryEx((Join-Path $target 'DiskPilot-X.exe'), [IntPtr]::Zero, 2)
if ($module -eq [IntPtr]::Zero) { throw '无法读取发布EXE。' }
try {
    $resource = [DiskPilotReleaseManifest]::FindResource($module, [IntPtr]1, [IntPtr]24)
    if ($resource -eq [IntPtr]::Zero) { throw '发布EXE没有应用清单。' }
    $size = [DiskPilotReleaseManifest]::SizeofResource($module, $resource)
    $pointer = [DiskPilotReleaseManifest]::LockResource([DiskPilotReleaseManifest]::LoadResource($module, $resource))
    $bytes = [byte[]]::new($size)
    [Runtime.InteropServices.Marshal]::Copy($pointer, $bytes, 0, $bytes.Length)
    $manifest = [Text.Encoding]::UTF8.GetString($bytes)
    $level = if ($TestPackage) { 'asInvoker' } else { 'requireAdministrator' }
    if ($manifest -notmatch ('level="' + $level + '"')) { throw "发布实际清单不是$level" }
} finally { [void][DiskPilotReleaseManifest]::FreeLibrary($module) }
$actual = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'DiskPilot-X.exe'))
if ($actual.FileVersion -ne "$Version.0") { throw "实际版本不一致：$($actual.FileVersion)" }
$hash = (Get-FileHash (Join-Path $target 'DiskPilot-X.exe') -Algorithm SHA256).Hash
[pscustomobject]@{ Version=$Version; ActualFileVersion=$actual.FileVersion; Manifest=$level; ExeSHA256=$hash; Folder=$target } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target '发布验证.json') -Encoding utf8
Write-Output $target
