param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][long]$WindowId,
    [Parameter(Mandatory)][string]$EvidenceRoot, [int]$Port = 39251, [string]$Session = 'log-budget')
$ErrorActionPreference = 'Stop'
$session = $Session
function Snapshot([string]$label, [string]$name) {
    $raw = & cua-driver get_window_state (@{pid=$ProcessId;window_id=$WindowId;session=$session;query=$label;tree_format='elements';screenshot_out_file=(Join-Path $EvidenceRoot "$name.png")} | ConvertTo-Json -Compress)
    if ($LASTEXITCODE) { throw $raw }
    $raw | Set-Content -LiteralPath (Join-Path $EvidenceRoot "$name-ui.json") -Encoding utf8
    return ($raw | ConvertFrom-Json)
}
function Cua([string]$tool, [hashtable]$request, [string]$name) {
    $request.pid=$ProcessId; $request.window_id=$WindowId; $request.session=$session
    $raw = & cua-driver $tool ($request | ConvertTo-Json -Compress)
    if ($LASTEXITCODE) { throw $raw }
    $raw | Set-Content -LiteralPath (Join-Path $EvidenceRoot "$name-动作.json") -Encoding utf8
}
function Search([string]$value, [string]$name) {
    $state=Snapshot '搜索日志' "$name-前"
    $edit=@($state.elements | Where-Object label -eq '搜索日志')
    if ($edit.Count -ne 1) { throw '搜索控件不唯一。' }
    Cua 'set_value' @{element_token=$edit[0].element_token;value=$value} $name
    $null=Snapshot '搜索日志' "$name-后"
}
function State([string]$name) {
    $raw=& "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command log_state -OutputPath (Join-Path $EvidenceRoot "$name.json")
    return ($raw | ConvertFrom-Json).data
}
function Click([string]$label, [string]$name) {
    $state=Snapshot $label "$name-前"
    $element=@($state.elements | Where-Object label -eq $label)
    if ($element.Count -ne 1) { throw "控件不唯一：$label" }
    Cua 'click' @{element_token=$element[0].element_token} $name
    $null=Snapshot $label "$name-后"
}
Click '推荐清理' '洪峰隐藏'
$null=& "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command log_fixture -Text '{"count":100000,"length":32}'
$watch=[Diagnostics.Stopwatch]::StartNew()
do {
    Start-Sleep -Milliseconds 100
    $hidden=State '隐藏洪峰'
    if ($watch.Elapsed.TotalSeconds -gt 45) { throw '洪峰日志超时。' }
} while ($hidden.running)
if ($hidden.active -or $hidden.scheduled -or $hidden.displayed -ne 0) { throw '隐藏时仍更新集合。' }
Click '控制台' '洪峰打开'
do { Start-Sleep -Milliseconds 100; $visible=State '可见洪峰' } while ($visible.scheduled -and $watch.Elapsed.TotalSeconds -lt 45)
if ($visible.displayed -ne 5000 -or $visible.realized -gt 100) { throw '洪峰显示或虚拟化未完成。' }
Search '99999' '搜索'
if ((State '搜索').visible -ne 1) { throw '搜索未保留指定日志。' }
Search '' '取消搜索'
if (-not (State '取消搜索状态').following) { Click '回到最新日志' '历史验证回到最新' }
$null=Snapshot 'ConsoleItemsControl' '历史滚动-前'
$raw=& powershell.exe -NoProfile -Command "& ([scriptblock]::Create((Get-Content -LiteralPath '$PSScriptRoot/scroll-log-uia.ps1' -Raw -Encoding utf8))) -ProcessId $ProcessId -WindowId $WindowId"
if ($LASTEXITCODE) { throw $raw }
$raw | Set-Content -LiteralPath (Join-Path $EvidenceRoot '历史滚动-原生动作.json') -Encoding utf8
$null=Snapshot 'ConsoleItemsControl' '历史滚动-后'
$before=State '历史滚动'
if ($before.following -or $before.offset -le 1) { throw '实际历史滚动未到达可保留的中间位置。' }
$null=& "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command log_crash -Text recoverable -OutputPath (Join-Path $EvidenceRoot '可恢复异常.json')
$null=& "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command source_probe
$watch=[Diagnostics.Stopwatch]::StartNew()
do {
    Start-Sleep -Milliseconds 100
    $probe=(& "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command source_probe_state | ConvertFrom-Json).data
    if ($watch.Elapsed.TotalSeconds -gt 45) { throw '源码验证超时。' }
} while ($probe.running)
$null=& "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command source_probe_state -OutputPath (Join-Path $EvidenceRoot '源码验证.json')
Start-Sleep -Milliseconds 400
$after=State '历史新增'
if ($after.version -le $before.version -or $after.following -or $after.offset -ne $before.offset - ($after.version-$before.version)) { throw '新日志改变历史锚点或没有真正新增日志。' }
if (-not $probe.ok) { throw '真实发布环境源码验证失败。' }
$null=& "$PSScriptRoot/verify-log-ui.ps1" -ProcessId $ProcessId -WindowId $WindowId -EvidenceRoot $EvidenceRoot -Port $Port -Session $session
$null=& "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command log_fixture -Text '{"count":6000,"length":4000}'
$watch.Restart()
do {
    Start-Sleep -Milliseconds 100
    $long=State '长日志进度'
    if ($watch.Elapsed.TotalSeconds -gt 45) { throw '长日志显示超时。' }
} while ($long.running -or $long.scheduled)
$long=State '长日志'
if ($long.text_characters -gt 4194304 -or $long.displayed -ne $long.buffered_count -or $long.maximum_batch_operations -gt 128) { throw '长日志容量或刷新预算失败。' }
$null=Snapshot 'ConsoleItemsControl' '窄窗口-前'
Cua 'set_window_frame' @{x=1120;y=296;width=960;height=640} '窄窗口'
$null=Snapshot 'ConsoleItemsControl' '窄窗口'
Cua 'set_window_frame' @{x=1120;y=296;width=1200;height=800} '恢复窗口'
$null=Snapshot 'ConsoleItemsControl' '恢复窗口'
[pscustomobject]@{SourceProbe=$probe.ok;MaximumBatchOperations=$long.maximum_batch_operations;MaximumBatchMs=$long.maximum_batch_ms;LongEntries=$long.displayed;HistoryOffset=$after.offset} | ConvertTo-Json
