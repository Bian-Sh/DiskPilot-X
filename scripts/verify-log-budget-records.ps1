param([Parameter(Mandatory)][string]$EvidenceRoot, [Parameter(Mandatory)][string]$ExitRoot,
    [Parameter(Mandatory)][string]$TestPackage)
$ErrorActionPreference='Stop'
function State([string]$name) { (Get-Content -LiteralPath (Join-Path $EvidenceRoot "$name.json") -Raw -Encoding utf8 | ConvertFrom-Json).data }
$before=State '历史滚动'; $after=State '历史新增'
if ($after.version -le $before.version -or $after.following -or $after.offset -ne $before.offset-1) { throw '历史新增缺少真正的新日志或锚点变化。' }
$toggles=State '切页前'
foreach ($index in 1..5) {
    $hidden=State "切页$index-隐藏状态"; $shown=State "切页$index-显示状态"
    if ($hidden.active -or $shown.following -or $shown.offset -ne $toggles.offset -or $shown.realized -gt 100) { throw '切页状态证据失败。' }
}
$latest=State '最新状态'; $idle=State '空闲状态'; $long=State '长日志'
if (-not $latest.following -or $latest.offset -ne $latest.scrollable -or $idle.scheduled -or $idle.batch_count -ne $latest.batch_count) { throw '回到最新或空闲停止证据失败。' }
if ($long.maximum_batch_operations -gt 128 -or $long.batch_budget_ms -ne 8 -or $long.maximum_batch_ms -le 0 -or $long.text_characters -gt 4194304 -or $long.displayed -ne $long.buffered_count) { throw '预算、耗时或文本容量证据失败。' }
$exit=Get-Content -LiteralPath (Join-Path $ExitRoot '退出核验.json') -Raw -Encoding utf8 | ConvertFrom-Json
if (-not $exit.Exited -or -not $exit.FinalLogFlushed -or [IO.Path]::GetFullPath($exit.Package) -ne [IO.Path]::GetFullPath($TestPackage)) { throw '最终包退出来源或结果错误。' }
$files=Get-ChildItem -LiteralPath (Join-Path $ExitRoot 'logs') -Filter 'app-*.tsv'
$text=($files | ForEach-Object {[IO.File]::ReadAllText($_.FullName)}) -join "`n"
if (-not $text.Contains('验收日志 0 ') -or -not $text.Contains('Application exit begin with code 0')) { throw '最终退出尾部日志未实际落盘。' }
[pscustomobject]@{Toggles=5;HistoryAnchor=$true;MaximumOperations=$long.maximum_batch_operations;MaximumBatchMs=$long.maximum_batch_ms;CooperativeBudgetMs=8;FinalExit=$true} | ConvertTo-Json
