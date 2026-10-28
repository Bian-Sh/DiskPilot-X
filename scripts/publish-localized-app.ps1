param([string]$Purpose = "本地化与聊天排版", [string]$EvidenceDirectory = ".planning/localization-chat-typography")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$evidence = Join-Path $root $EvidenceDirectory
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$time = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'China Standard Time').ToString('yyyy-MM-dd_HH-mm-ss')
# 发布根目录与当前源码工作树分离，遵循主仓库的统一交付目录约定。
$publishRoot = [IO.Path]::GetFullPath('D:/AppData/Visual Studio/Projects/DiskPilot-X/outputs')
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
[pscustomobject]@{
    SourceRoot = $root
    Branch = (& git -C $root branch --show-current)
    BaseCommit = (& git -C $root rev-parse HEAD)
    HasUncommittedChanges = (@(& git -C $root status --porcelain).Count -gt 0)
    OutputRoot = $publishRoot
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'publish-source.json') -Encoding utf8
$packages = @()
foreach ($test in @($true, $false)) {
    $kind = if ($test) { '测试包_非提权' } else { '用户交付包_管理员' }
    $target = Join-Path $publishRoot "${time}_${Purpose}_${kind}"
    if (Test-Path -LiteralPath $target) { throw "拒绝覆盖已有发布目录：$target" }
    & dotnet publish (Join-Path $root 'src/DiskPilotX.App/DiskPilotX.App.csproj') -c Release -r win-x64 `
        --artifacts-path (Join-Path $evidence "build-${time}") -t:Rebuild `
        --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false `
        "-p:DiskPilotTestPackage=$($test.ToString().ToLowerInvariant())" -o $target
    if ($LASTEXITCODE -ne 0) { throw "发布失败：$kind" }
    foreach ($required in @('DiskPilot-X.exe','DiskPilot-X.dll','DiskPilotX.Core.dll','System.Private.CoreLib.dll','skills','zh-CN/DiskPilot-X.resources.dll','zh-TW/DiskPilot-X.resources.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $target $required))) { throw "发布内容缺失：$required" }
    }
    $packages += [pscustomobject]@{ Kind=$kind; Path=$target; Test=$test }
}
$packages | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence "published-packages.json") -Encoding utf8
$packages | Format-Table -AutoSize
