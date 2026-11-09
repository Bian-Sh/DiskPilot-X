param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][long]$WindowId)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$WindowId)
if ($root.Current.ProcessId -ne $ProcessId) { throw '原生滚动窗口的进程不匹配。' }
$condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'ConsoleItemsControl')
$list=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
if (-not $list) { throw '没有找到真实控制台列表。' }
$pattern=$list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
if (-not $pattern.Current.VerticallyScrollable) { throw '真实列表不可滚动。' }
$before=$pattern.Current.VerticalScrollPercent
1..3 | ForEach-Object { $pattern.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount,[System.Windows.Automation.ScrollAmount]::LargeDecrement) }
Start-Sleep -Milliseconds 100
$after=$pattern.Current.VerticalScrollPercent
if ($after -ge $before) { throw '原生 UIA 历史滚动未生效。' }
[pscustomobject]@{ProcessId=$ProcessId;WindowId=$WindowId;Before=$before;After=$after;Route='UIA ScrollPattern，无前台激活'} | ConvertTo-Json
