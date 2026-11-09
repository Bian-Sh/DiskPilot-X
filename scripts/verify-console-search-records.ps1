param([Parameter(Mandatory)][string]$EvidenceRoot, [Parameter(Mandatory)][string]$TestPackage)
$ErrorActionPreference = 'Stop'
function State([string]$name) { (Get-Content -LiteralPath (Join-Path $EvidenceRoot "$name.json") -Raw -Encoding utf8 | ConvertFrom-Json).data }
$launch=Get-Content -LiteralPath (Join-Path $EvidenceRoot 'launch.json') -Raw -Encoding utf8 | ConvertFrom-Json
if ($launch.Elevated -or [IO.Path]::GetFullPath($launch.Path) -ne [IO.Path]::GetFullPath((Join-Path $TestPackage 'DiskPilot-X.exe'))) { throw 'UI验收包来源或权限不匹配。' }
$empty=State 'UI空搜索'; $found=State 'UI有搜索'; $zero=State 'UI零条搜索'; $clear=State 'UI一键清空'; $six=State 'UI六组统计'; $narrow=State 'UI窄窗口'; $restored=State 'UI恢复默认统计'
if (-not $empty.search_ui.placeholder -or -not $empty.search_ui.icon -or $empty.search_ui.clear -or $empty.search_ui.radius -le 0 -or ($empty.level_counts.level -join '|') -ne 'Info|Warning|Error|Fatal') { throw '空搜索或默认等级证据失败。' }
if ($found.search_ui.placeholder -or $found.search_ui.icon -or -not $found.search_ui.clear -or $found.visible -ne 1 -or (($found.level_counts | ForEach-Object { $_.count }) -join '|') -ne '0|0|1|0') { throw '输入图标切换或统计证据失败。' }
if ($zero.visible -ne 0 -or $zero.level_counts.Count -ne 4 -or @($zero.level_counts | Where-Object count -ne 0).Count -ne 0) { throw '零条统计证据失败。' }
if ($clear.search_ui.text -ne '' -or -not $clear.search_ui.focused -or -not $clear.search_ui.placeholder -or -not $clear.search_ui.icon -or $clear.search_ui.clear) { throw '清空和焦点证据失败。' }
if (($six.level_counts.level -join '|') -ne 'Verbose|Debug|Info|Warning|Error|Fatal' -or @($six.level_counts | Where-Object { -not $_.icon }).Count -ne 0 -or $restored.level_counts.Count -ne 4) { throw '菜单等级组证据失败。' }
if ($narrow.search_ui.width -lt 80 -or $narrow.search_ui.right -gt $narrow.search_ui.counts_left) { throw '窄窗口统计与搜索重叠。' }
$menu=Get-Content -LiteralPath (Join-Path $EvidenceRoot 'UI菜单目录-ui.json') -Raw -Encoding utf8 | ConvertFrom-Json
if (@($menu.elements | Where-Object label -eq '打开日志目录').Count -ne 1) { throw '菜单目录证据缺失。' }
[pscustomobject]@{SearchClearFocused=$true;ZeroGroups=4;SixGroups=6;MenuDirectory=$true;NarrowWidth=$narrow.search_ui.width;Package=$TestPackage} | ConvertTo-Json
