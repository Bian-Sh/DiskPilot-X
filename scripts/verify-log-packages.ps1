param([string]$TestPackage, [string]$UserPackage, [string]$EvidenceRoot, [switch]$ReadOnly)
$ErrorActionPreference = 'Stop'
Add-Type -ReferencedAssemblies @('System.Reflection.Metadata','System.Collections.Immutable','System.Linq') -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
public static class LogPackageReader {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll")] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    public static string Manifest(string path) {
        var module=LoadLibraryEx(path,IntPtr.Zero,2);
        if(module==IntPtr.Zero) throw new InvalidDataException("无法读取 EXE");
        try {
            var resource=FindResource(module,(IntPtr)1,(IntPtr)24);
            if(resource==IntPtr.Zero) throw new InvalidDataException("EXE 清单缺失");
            var bytes=new byte[SizeofResource(module,resource)];
            Marshal.Copy(LockResource(LoadResource(module,resource)),bytes,0,bytes.Length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        } finally { FreeLibrary(module); }
    }
    public static bool SameManagedContent(string left,string right) {
        using var a=new PEReader(File.OpenRead(left));
        using var b=new PEReader(File.OpenRead(right));
        if(!a.HasMetadata || !b.HasMetadata) return false;
        var x=a.GetMetadataReader(); var y=b.GetMetadataReader();
        if(x.MethodDefinitions.Count!=y.MethodDefinitions.Count || x.TypeDefinitions.Count!=y.TypeDefinitions.Count || x.AssemblyReferences.Count!=y.AssemblyReferences.Count) return false;
        foreach(var handle in x.MethodDefinitions) {
            var m=x.GetMethodDefinition(handle); var n=y.GetMethodDefinition(handle);
            if(x.GetString(m.Name)!=y.GetString(n.Name) || m.Attributes!=n.Attributes || m.ImplAttributes!=n.ImplAttributes || !x.GetBlobBytes(m.Signature).SequenceEqual(y.GetBlobBytes(n.Signature))) return false;
            if((m.RelativeVirtualAddress==0)!=(n.RelativeVirtualAddress==0)) return false;
            if(m.RelativeVirtualAddress==0) continue;
            var p=a.GetMethodBody(m.RelativeVirtualAddress); var q=b.GetMethodBody(n.RelativeVirtualAddress);
            if(p.MaxStack!=q.MaxStack || p.LocalVariablesInitialized!=q.LocalVariablesInitialized || p.LocalSignature!=q.LocalSignature || !p.GetILBytes().SequenceEqual(q.GetILBytes())) return false;
            if(!p.ExceptionRegions.SequenceEqual(q.ExceptionRegions)) return false;
        }
        foreach(var handle in x.AssemblyReferences) {
            var m=x.GetAssemblyReference(handle); var n=y.GetAssemblyReference(handle);
            if(x.GetString(m.Name)!=y.GetString(n.Name) || m.Version!=n.Version || m.Flags!=n.Flags || !x.GetBlobBytes(m.PublicKeyOrToken).SequenceEqual(y.GetBlobBytes(n.PublicKeyOrToken))) return false;
        }
        var r=a.PEHeaders.CorHeader.ResourcesDirectory; var s=b.PEHeaders.CorHeader.ResourcesDirectory;
        if(r.Size!=s.Size) return false;
        return r.Size==0 || a.GetSectionData(r.RelativeVirtualAddress).GetContent(0,r.Size).SequenceEqual(b.GetSectionData(s.RelativeVirtualAddress).GetContent(0,s.Size));
    }
}
'@
$testManifest = [LogPackageReader]::Manifest((Join-Path $TestPackage 'DiskPilot-X.exe'))
$userManifest = [LogPackageReader]::Manifest((Join-Path $UserPackage 'DiskPilot-X.exe'))
[xml]$testXml = $testManifest.TrimStart([char]0xfeff)
[xml]$userXml = $userManifest.TrimStart([char]0xfeff)
$testLevel = $testXml.SelectSingleNode("//*[local-name()='requestedExecutionLevel']").level
$userLevel = $userXml.SelectSingleNode("//*[local-name()='requestedExecutionLevel']").level
if ($testLevel -ne 'asInvoker' -or $userLevel -ne 'requireAdministrator') { throw '实际 EXE 清单不符合双包要求。' }
if (-not $ReadOnly) {
    $testManifest | Set-Content -LiteralPath (Join-Path $EvidenceRoot '测试EXE实际清单.xml') -Encoding utf8
    $userManifest | Set-Content -LiteralPath (Join-Path $EvidenceRoot '用户EXE实际清单.xml') -Encoding utf8
}
$left = @(Get-ChildItem -LiteralPath $TestPackage -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($TestPackage,$_.FullName) } | Sort-Object)
$right = @(Get-ChildItem -LiteralPath $UserPackage -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($UserPackage,$_.FullName) } | Sort-Object)
if (Compare-Object $left $right) { throw '双包文件列表不一致。' }
$normalized = @()
foreach ($name in $left) {
    if ($name -match '\.pdb$|^DiskPilot-X\.exe$|^发布(来源|验证)\.json$') { continue }
    $a = Join-Path $TestPackage $name
    $b = Join-Path $UserPackage $name
    if ((Get-FileHash -LiteralPath $a).Hash -eq (Get-FileHash -LiteralPath $b).Hash) { continue }
    if (-not $name.EndsWith('.dll') -or -not [LogPackageReader]::SameManagedContent($a,$b)) { throw "双包业务内容不同：$name" }
    $normalized += $name
}
$result = [pscustomobject]@{ TestManifest=$testLevel; UserManifest=$userLevel; Files=$left.Count; ManagedCodeEquivalent=$true; NormalizedAssemblies=$normalized }
if (-not $ReadOnly) { $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceRoot '双包内容核验.json') -Encoding utf8 }
$result | ConvertTo-Json -Depth 5
