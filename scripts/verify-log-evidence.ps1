param([Parameter(Mandatory)][string]$EvidenceRoot, [Parameter(Mandatory)][string]$TestPackage, [Parameter(Mandatory)][string]$UserPackage, [switch]$ReadOnly)
$ErrorActionPreference = 'Stop'
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Read-State([string]$name) { return (Get-Content -LiteralPath (Join-Path $EvidenceRoot "$name.json") -Raw -Encoding utf8 | ConvertFrom-Json).data }
$hidden = Read-State '隐藏洪峰'
$visible = Read-State '可见洪峰'
$search = Read-State '搜索'
$history = Read-State '历史滚动'
$appended = Read-State '历史新增'
$probe = Read-State '源码验证'
Require (-not $hidden.running -and -not $hidden.active -and $hidden.displayed -eq 0 -and $hidden.buffered_count -eq 5000 -and -not $hidden.scheduled) '隐藏洪峰证据不符合要求。'
Require ($visible.visible -eq 5000 -and $visible.realized -gt 0 -and $visible.realized -lt 100) '可见回收虚拟化证据不符合要求。'
Require ($search.visible -eq 1 -and $search.query -eq '99999') '搜索证据不符合要求。'
Require (-not $appended.following -and $appended.offset -eq $history.offset - 1) '新增日志改变历史锚点。'
Require ($probe.ok -and $probe.validated -and $probe.count -eq 2 -and $probe.diagnostic_count -ge 2) '发布源码编译、运行或失败诊断缺失。'
$count = 0
$special = $false
$first = $false
$last = $false
$files = @(Get-ChildItem -LiteralPath (Join-Path $EvidenceRoot 'logs') -Filter 'app-*.tsv')
foreach ($file in $files) {
    $stream = [IO.FileStream]::new($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $bytes = [byte[]]::new(3)
        $stream.ReadExactly($bytes)
        Require ([Convert]::ToHexString($bytes) -eq 'EFBBBF') '普通日志缺少 UTF-8 BOM。'
        $stream.Position = 0
        $parser = [Microsoft.VisualBasic.FileIO.TextFieldParser]::new($stream, [Text.Encoding]::UTF8)
        try {
            $parser.HasFieldsEnclosedInQuotes = $true
            $parser.TrimWhiteSpace = $false
            $parser.SetDelimiters("`t")
            Require (($parser.ReadFields() -join '|') -eq '时间|等级|进程|来源|消息|异常类型|异常堆栈') '普通日志列头不正确。'
            while (-not $parser.EndOfData) {
                $fields = $parser.ReadFields()
                Require ($fields.Count -eq 7) '普通日志串列。'
                if ($fields[4].StartsWith('验收日志 ')) { $count++ }
                if ($fields[4].StartsWith('验收日志 0 ')) { $first=$true }
                if ($fields[4].StartsWith('验收日志 99999 ')) { $last=$true }
                if ($fields[4] -eq "分列标记`t中文`r`n第二行`"引号`"") {
                    Require ($fields[5] -eq 'System.InvalidOperationException' -and $fields[6].Contains('AcceptanceLogFixture')) '异常类型或完整堆栈缺失。'
                    $special=$true
                }
            }
        } finally { $parser.Dispose() }
    } finally { $stream.Dispose() }
}
Require ($count -ge 100000 -and $first -and $last -and $special) '洪峰文件记录或特殊字符还原缺失。'
$crashes = @(Get-ChildItem -LiteralPath (Join-Path $EvidenceRoot 'Diagnostics/logs') -Filter 'crash-*.tsv')
foreach ($file in $crashes) {
    Require ([Convert]::ToHexString([IO.File]::ReadAllBytes($file.FullName)[0..2]) -eq 'EFBBBF') '崩溃日志缺少 BOM。'
    $parser = [Microsoft.VisualBasic.FileIO.TextFieldParser]::new($file.FullName)
    try {
        $parser.HasFieldsEnclosedInQuotes = $true
        $parser.SetDelimiters("`t")
        Require ($parser.ReadFields().Count -eq 11) '崩溃列头错误。'
        $fields = $parser.ReadFields()
        Require ($fields.Count -eq 11 -and $fields[3] -eq 'System.InvalidOperationException' -and $fields[5].Contains('AcceptanceLogCrash')) '崩溃字段或堆栈缺失。'
        Require ((Test-Path -LiteralPath $fields[9]) -or $fields[10].Length -gt 0 -or -not $parser.EndOfData) 'MiniDump 与失败说明均缺失。'
    } finally { $parser.Dispose() }
}
Require ($crashes.Count -ge 1) '独立崩溃记录缺失。'
foreach ($package in @($TestPackage,$UserPackage)) {
    foreach ($name in @('DiskPilot-X.exe','DiskPilot-X.dll','DiskPilotX.Core.dll','System.Private.CoreLib.dll','skills')) {
        Require (Test-Path -LiteralPath (Join-Path $package $name)) "完整发布包缺失：$name"
    }
}
$result = [pscustomobject]@{ FloodRows=$count; LogFiles=$files.Count; CrashReports=$crashes.Count; HiddenElapsedMs=$hidden.elapsed_ms; Realized=$visible.realized; SourceProbe=$probe.ok; SpecialFields=$special }
if (-not $ReadOnly) { $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceRoot '日志证据核验.json') -Encoding utf8 }
$result | ConvertTo-Json
