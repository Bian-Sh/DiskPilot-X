param([int]$Port = 39249, [Parameter(Mandatory)][string]$Command, [string]$Text, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
$client = [Net.Sockets.TcpClient]::new()
try {
    if (-not $client.ConnectAsync('127.0.0.1', $Port).Wait(10000)) { throw '验收服务连接超时。' }
    $client.ReceiveTimeout = 15000
    $stream = $client.GetStream()
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
    $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    $writer.WriteLine((@{ command = $Command; text = $Text } | ConvertTo-Json -Compress))
    $response = $reader.ReadLine()
    if (-not $response) { throw '验收服务未返回结果。' }
    $result = $response | ConvertFrom-Json
    if (-not $result.ok) { throw $result.error }
    if ($OutputPath) { $result | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutputPath -Encoding utf8 }
    $result | ConvertTo-Json -Depth 30
} finally { $client.Dispose() }
