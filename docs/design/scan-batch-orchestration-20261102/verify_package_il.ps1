$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$packages=Get-Content -LiteralPath (Join-Path $root 'packages.json') -Raw|ConvertFrom-Json
function Get-MethodCodeHash([string]$file) {
    $stream=[IO.File]::OpenRead($file)
    $pe=[System.Reflection.PortableExecutable.PEReader]::new($stream)
    $buffer=[IO.MemoryStream]::new();$writer=[IO.BinaryWriter]::new($buffer)
    try {
        $metadata=[System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach($handle in $metadata.MethodDefinitions) {
            $method=$metadata.GetMethodDefinition($handle)
            $writer.Write($metadata.GetString($method.Name))
            $signature=$metadata.GetBlobBytes($method.Signature);$writer.Write($signature.Length);$writer.Write($signature)
            if($method.RelativeVirtualAddress -gt 0){$il=[System.Reflection.Metadata.PEReaderExtensions]::GetMethodBody($pe,$method.RelativeVirtualAddress).GetILBytes();$writer.Write($il.Length);$writer.Write([byte[]]$il)}
        }
        $writer.Flush();[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($buffer.ToArray()))
    } finally {$writer.Dispose();$buffer.Dispose();$pe.Dispose();$stream.Dispose()}
}
$result=@()
foreach($name in @('DiskPilot-X.dll','DiskPilotX.Core.dll','TinyHarness.dll')) {
    $test=Get-MethodCodeHash (Join-Path $packages.Test $name)
    $user=Get-MethodCodeHash (Join-Path $packages.User $name)
    if($test -ne $user){throw "测试与交付方法IL不一致：$name"}
    $result+=[ordered]@{Assembly=$name;MethodsILSHA256=$test;Identical=$true}
}
$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'package-il-verified.json') -Encoding utf8
$result|ConvertTo-Json
