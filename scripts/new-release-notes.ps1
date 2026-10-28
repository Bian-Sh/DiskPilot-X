param(
    [Parameter(Mandatory)][string]$Version,
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$Ref = 'HEAD',
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$RepositoryUrl = 'https://github.com/Bian-Sh/DiskPilot-X'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号必须为三段数字。' }
if ($Ref.StartsWith('-')) { throw '无效的Git引用。' }
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$OutputPath = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $RepositoryRoot $OutputPath }
$tag = "v$Version"
$previous = & git -C $RepositoryRoot describe --tags --abbrev=0 --match 'v[0-9]*' "$Ref^" 2>$null
if ($LASTEXITCODE -ne 0) { $previous = $null }
$override = Join-Path $RepositoryRoot "docs/releases/$tag.md"
if (Test-Path -LiteralPath $override) {
    $body = [IO.File]::ReadAllText($override).Trim()
} else {
    $range = if ($previous) { "$previous..$Ref" } else { $Ref }
    $commits = @(& git -C $RepositoryRoot log $range --no-merges '--format=%s')
    if ($LASTEXITCODE) { throw '读取Git提交失败。' }
    $groups = [ordered]@{ '🐞 修复问题' = [Collections.Generic.List[string]]::new(); '✨ 新增功能' = [Collections.Generic.List[string]]::new(); '🚀 优化改进' = [Collections.Generic.List[string]]::new() }
    foreach ($subject in $commits) {
        if ([string]::IsNullOrWhiteSpace($subject)) { continue }
        $category = if ($subject -match '^(fix|修复|修正)(\(|:|：|\s|[^a-zA-Z])') { '🐞 修复问题' } elseif ($subject -match '^(feat|新增|增加)(\(|:|：|\s|[^a-zA-Z])') { '✨ 新增功能' } else { '🚀 优化改进' }
        $message = $subject -replace '^(feat|fix|perf|refactor|docs|test|build|ci|chore)(\([^)]*\))?!?:\s*', ''
        $groups[$category].Add("- $message")
    }
    $sections = foreach ($key in $groups.Keys) {
        if ($groups[$key].Count) { "## $key`n`n$($groups[$key] -join "`n")" }
    }
    $body = $sections -join "`n`n"
}
if ($body -notmatch '\*\*Full Changelog\*\*') {
    $url = if ($previous) { "$RepositoryUrl/compare/$previous...$tag" } else { "$RepositoryUrl/commits/$tag" }
    $body += "`n`n**Full Changelog**: $url"
}
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null
[IO.File]::WriteAllText($OutputPath, $body.Trim() + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $OutputPath
