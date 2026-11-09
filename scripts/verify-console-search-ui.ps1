param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][long]$WindowId,
    [Parameter(Mandatory)][string]$EvidenceRoot, [int]$Port = 39253, [string]$Session = 'log-search')
$ErrorActionPreference = 'Stop'
function Snapshot([string]$query, [string]$name) {
    $raw = & cua-driver get_window_state (@{pid=$ProcessId;window_id=$WindowId;session=$Session;query=$query;tree_format='elements';screenshot_out_file=(Join-Path $EvidenceRoot "$name.png")} | ConvertTo-Json -Compress)
    if ($LASTEXITCODE) { throw $raw }
    $raw | Set-Content -LiteralPath (Join-Path $EvidenceRoot "$name-ui.json") -Encoding utf8
    return ($raw | ConvertFrom-Json)
}
function Action([string]$tool, [hashtable]$request, [string]$name) {
    $request.pid=$ProcessId; $request.window_id=$WindowId; $request.session=$Session
    $raw = & cua-driver $tool ($request | ConvertTo-Json -Compress)
    if ($LASTEXITCODE) { throw $raw }
    $raw | Set-Content -LiteralPath (Join-Path $EvidenceRoot "$name-动作.json") -Encoding utf8
}
function Click([string]$label, [string]$name) {
    $snapshot=Snapshot $label "$name-前"
    $elements=@($snapshot.elements | Where-Object label -eq $label)
    if ($elements.Count -ne 1) { throw "控件不唯一：$label" }
    Action 'click' @{element_token=$elements[0].element_token} $name
    $null=Snapshot $label "$name-后"
}
function Search([string]$text, [string]$name) {
    $snapshot=Snapshot '搜索日志' "$name-前"
    $edit=@($snapshot.elements | Where-Object label -eq '搜索日志')
    if ($edit.Count -ne 1) { throw '搜索控件不唯一。' }
    Action 'set_value' @{element_token=$edit[0].element_token;value=$text} $name
    $null=Snapshot '搜索日志' "$name-后"
}
function State([string]$name) {
    $raw = & "$PSScriptRoot/invoke-log-acceptance.ps1" -Port $Port -Command log_state -OutputPath (Join-Path $EvidenceRoot "$name.json")
    return ($raw | ConvertFrom-Json).data
}
Search '' 'UI空搜索'
$empty=State 'UI空搜索'
if (-not $empty.search_ui.placeholder -or -not $empty.search_ui.icon -or $empty.search_ui.clear -or $empty.search_ui.radius -le 0) { throw '空搜索外观不符合要求。' }
if (($empty.level_counts.level -join '|') -ne 'Info|Warning|Error|Fatal') { throw '默认统计组不正确。' }
Search '分列标记' 'UI有搜索'
$found=State 'UI有搜索'
if ($found.search_ui.placeholder -or $found.search_ui.icon -or -not $found.search_ui.clear -or $found.visible -ne 1) { throw '有内容图标切换或搜索失败。' }
if ((($found.level_counts | ForEach-Object { $_.count }) -join '|') -ne '0|0|1|0') { throw '统计未按当前搜索结果计数。' }
Search '不存在的搜索-UI验收' 'UI零条搜索'
$zero=State 'UI零条搜索'
if ($zero.level_counts.Count -ne 4 -or @($zero.level_counts | Where-Object count -ne 0).Count -ne 0) { throw '0条勾选组不应隐藏。' }
Click '清空搜索' 'UI一键清空'
$clear=State 'UI一键清空'
if ($clear.search_ui.text -ne '' -or -not $clear.search_ui.focused -or -not $clear.search_ui.icon -or $clear.search_ui.clear) { throw '清空或保留输入焦点失败。' }
Click '筛选控制台日志' 'UI打开菜单'
$menu=Snapshot '打开日志目录' 'UI菜单目录'
if (@($menu.elements | Where-Object label -eq '打开日志目录').Count -ne 1) { throw '日志目录入口未在菜单出现。' }
Click 'Verbose' 'UI勾选Verbose'
Click 'Debug' 'UI勾选Debug'
$six=State 'UI六组统计'
if (($six.level_counts.level -join '|') -ne 'Verbose|Debug|Info|Warning|Error|Fatal') { throw '菜单未控制六组统计。' }
$null=Snapshot '搜索日志' 'UI窄窗口-前'
Action 'set_window_frame' @{x=1120;y=296;width=960;height=640} 'UI窄窗口'
$null=Snapshot '搜索日志' 'UI窄窗口-后'
$narrow=State 'UI窄窗口'
if ($narrow.search_ui.width -lt 80 -or $narrow.search_ui.right -gt $narrow.search_ui.counts_left) { throw '六组统计挤压或重叠搜索框。' }
$null=Snapshot '搜索日志' 'UI恢复窗口-前'
Action 'set_window_frame' @{x=1120;y=296;width=1200;height=800} 'UI恢复窗口'
$null=Snapshot '搜索日志' 'UI恢复窗口-后'
Click 'Verbose' 'UI取消Verbose'
Click 'Debug' 'UI取消Debug'
$restored=State 'UI恢复默认统计'
if ($restored.level_counts.Count -ne 4) { throw '取消勾选未隐藏统计组。' }
[pscustomobject]@{Placeholder=$true;SearchClearSwitch=$true;ClearFocused=$clear.search_ui.focused;ZeroGroups=$zero.level_counts.Count;MenuDirectory=$true;SixGroups=$six.level_counts.Count;NarrowWidth=$narrow.search_ui.width} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceRoot '搜索UI核验.json') -Encoding utf8
