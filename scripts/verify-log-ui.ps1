param([int]$ProcessId, [long]$WindowId, [string]$EvidenceRoot, [int]$Port = 39250, [string]$Session = 'log-resume')
$ErrorActionPreference = 'Stop'
$session = $Session
function Read-Window([string]$label, [string]$name) {
    $request = @{ pid=$ProcessId; window_id=$WindowId; session=$session; query=$label; tree_format='elements'; timeout_ms=5000; screenshot_out_file=(Join-Path $EvidenceRoot "$name.png") }
    $raw = & cua-driver get_window_state ($request | ConvertTo-Json -Compress)
    if ($LASTEXITCODE) { throw $raw }
    $state = $raw | ConvertFrom-Json
    $raw | Set-Content -LiteralPath (Join-Path $EvidenceRoot "$name.json") -Encoding utf8
    return $state
}
function Click-Label([string]$label, [string]$name) {
    $state = Read-Window $label "$name-前"
    $element = @($state.elements | Where-Object label -eq $label)
    if ($element.Count -ne 1) { throw "控件不唯一：$label" }
    $raw = & cua-driver click (@{pid=$ProcessId; window_id=$WindowId; session=$session; element_token=$element[0].element_token} | ConvertTo-Json -Compress)
    if ($LASTEXITCODE) { throw $raw }
    $raw | Set-Content -LiteralPath (Join-Path $EvidenceRoot "$name-动作.json") -Encoding utf8
    $null = Read-Window $label "$name-后"
}
function Read-Log([string]$name) {
    $raw = & "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command log_state -OutputPath (Join-Path $EvidenceRoot "$name.json")
    return ($raw | ConvertFrom-Json).data
}
$before = Read-Log '切页前'
if ($before.following) { throw '必须先滚动历史，才能验证切页保留阅读状态。' }
for ($index=1; $index -le 5; $index++) {
    Click-Label '推荐清理' "切页$index-隐藏"
    if ((Read-Log "切页$index-隐藏状态").active) { throw '隐藏控制台仍在刷新。' }
    Click-Label '控制台' "切页$index-显示"
    $current = Read-Log "切页$index-显示状态"
    if ($current.following -or $current.realized -gt 100 -or $current.offset -ne $before.offset) { throw '切页改变历史阅读位置或失去虚拟化。' }
}
Click-Label '回到最新日志' '回到最新'
$latest = Read-Log '最新状态'
if (-not $latest.following -or $latest.offset -ne $latest.scrollable) { throw '回到最新未到达尾部。' }
Start-Sleep -Milliseconds 1200
$idle = Read-Log '空闲状态'
if ($idle.batch_count -ne $latest.batch_count -or $idle.scheduled) { throw '无新日志仍安排刷新。' }
[pscustomobject]@{ Toggles=5; Following=$idle.following; Realized=$idle.realized; IdleBatchCount=$idle.batch_count } | ConvertTo-Json
