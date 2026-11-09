param([string]$TestPackage, [string]$EvidenceRoot, [int]$Port = 39252)
$ErrorActionPreference = 'Stop'
$launch = & "$PSScriptRoot/start-log-acceptance.ps1" -Package $TestPackage -EvidenceRoot $EvidenceRoot -Port $Port | ConvertFrom-Json
$null = & "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command log_fixture -Text '{"count":1,"length":32}'
$watch = [Diagnostics.Stopwatch]::StartNew()
do {
    $state = & "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command log_state | ConvertFrom-Json
    if ($watch.Elapsed.TotalSeconds -gt 10) { throw '退出前日志夹具超时。' }
    if ($state.data.running) { Start-Sleep -Milliseconds 50 }
} while ($state.data.running)
$null = & "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command log_crash -Text exit
$process = Get-Process -Id $launch.ProcessId -ErrorAction SilentlyContinue
if ($process -and -not $process.WaitForExit(10000)) { throw '正常退出超时。' }
$files = @(Get-ChildItem -LiteralPath (Join-Path $EvidenceRoot 'logs') -Filter 'app-*.tsv')
$text = ($files | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
if (-not $text.Contains('验收日志 0 ') -or -not $text.Contains('Application exit begin with code 0')) { throw '正常退出未刷新最后日志。' }
$result = [pscustomobject]@{ ProcessId=$launch.ProcessId; Exited=$true; FinalLogFlushed=$true; Package=$TestPackage; Files=$files.Count }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceRoot '退出核验.json') -Encoding utf8
$result | ConvertTo-Json
