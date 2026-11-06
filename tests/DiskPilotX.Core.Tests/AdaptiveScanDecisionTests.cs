using System.Text.Json;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;

namespace DiskPilotX.Core.Tests;

public class AdaptiveScanDecisionTests
{
    [Fact]
    public async Task 小目录有可解释决策而非伪造MFT回退()
    {
        var path=Directory.CreateTempSubdirectory("diskpilot-route-").FullName;
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(path,"一.bin"),new byte[17]);
            var reports=new List<ScanProgress>();var scanner=new IncrementalFileScanner(new FileScanner());
            var result=await scanner.ScanAsync(path,new InlineProgress(reports.Add),CancellationToken.None);
            var json=JsonSerializer.SerializeToElement(result.Execution!.ToPublicData());
            Assert.True(json.TryGetProperty("decision",out var decision));
            Assert.False(string.IsNullOrWhiteSpace(decision.GetProperty("reasonCode").GetString()));
            Assert.Equal("adaptive-v1",decision.GetProperty("policyVersion").GetString());
            Assert.Equal(ScanMethod.DirectoryTraversal,result.Execution.Method);
            Assert.Empty(result.Execution.FallbackReasons??[]);Assert.Equal(17,result.TotalSize);
            Assert.All(reports.Where(p=>p.Execution is not null),p=>Assert.True(JsonSerializer.SerializeToElement(p.Execution!.ToPublicData()).TryGetProperty("decision",out _)));
        }
        finally {Directory.Delete(path,true);}
    }

    [Fact]
    public async Task 渐进探测复用枚举且计数不翻倍()
    {
        var path=Directory.CreateTempSubdirectory("diskpilot-probe-").FullName;
        try
        {
            for(int i=0;i<2100;i++)await File.WriteAllBytesAsync(Path.Combine(path,$"文件{i}.txt"),new byte[1]);
            var reports=new List<ScanProgress>();var result=await new IncrementalFileScanner(new FileScanner()).ScanAsync(path,new InlineProgress(reports.Add),CancellationToken.None);
            Assert.Equal(2100,result.TotalFiles);Assert.Equal(2100,result.TotalSize);
            Assert.Equal(2100,result.RootEntry!.Children.Select(c=>c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains(reports,p=>p.CurrentPath.Contains("评估")||p.CurrentPath.Contains("探测"));
            Assert.True(JsonSerializer.SerializeToElement(result.Execution!.ToPublicData()).TryGetProperty("attempts",out var attempts));
            Assert.True(attempts.GetArrayLength()>0);
        }
        finally {Directory.Delete(path,true);}
    }
    internal sealed class Environment : IScanRouteEnvironment
    {
        public DateTime UtcNow {get;set;}=new(2026,11,5,0,0,0,DateTimeKind.Utc);
        public ScanVolumeCapability Capability=new("volume-fixture","NTFS",true,true,"mft_available","夹具：已提权且NTFS日志可用");
        public ScanVolumeCapability Inspect(string root)=>Capability;
    }

    [Fact]
    public void 大目录有效历史选择MFT而过期和证据不足不猜测()
    {
        var env=new Environment();var now=env.UtcNow;
        var samples=new[]{new ScanPerformanceSample("volume-fixture","scope",ScanMethod.DirectoryTraversal,100000,120000,0,0,0,0,now),
            new ScanPerformanceSample("volume-fixture","volume",ScanMethod.Mft,1000000,10000,4000,2000,4000,1000000,now)};
        var d=ScanRoutePolicy.Decide(env.Capability,false,samples,"scope",now);
        Assert.Equal(ScanMethod.Mft,d.PlannedMethod);Assert.Equal("measured_advantage",d.ReasonCode);
        Assert.NotNull(d.TraversalCost);Assert.NotNull(d.MftCost);
        Assert.True(d.MftCost.UpperMilliseconds<=d.TraversalCost.LowerMilliseconds*.75);
        Assert.True(d.TraversalCost.LowerMilliseconds-d.MftCost.UpperMilliseconds>=2000);
        Assert.Equal("insufficient_evidence",ScanRoutePolicy.Decide(env.Capability,false,samples,"new-scope",now).ReasonCode);
        Assert.Equal("insufficient_evidence",ScanRoutePolicy.Decide(env.Capability,false,samples,"scope",now.AddDays(8)).ReasonCode);
    }

    [Fact]
    public void 已提权不覆盖卷能力且收益不足仍遍历()
    {
        var env=new Environment();var c=env.Capability with {CanUseMft=false,ReasonCode="volume_open_failed",Win32Error=5};
        Assert.Equal("volume_open_failed",ScanRoutePolicy.Decide(c,true,[],"scope",env.UtcNow).ReasonCode);
        var samples=new[]{new ScanPerformanceSample(c.VolumeKey,"scope",ScanMethod.DirectoryTraversal,100,10,0,0,0,0,env.UtcNow),
            new ScanPerformanceSample(c.VolumeKey,"volume",ScanMethod.Mft,1000,5000,2000,1000,2000,1000,env.UtcNow)};
        var d=ScanRoutePolicy.Decide(env.Capability,false,samples,"scope",env.UtcNow);
        Assert.Equal(ScanMethod.DirectoryTraversal,d.PlannedMethod);Assert.Equal("insufficient_saving",d.ReasonCode);
    }

    [Fact]
    public async Task MFT失败含阶段错误码且原范围不扩大()
    {
        var path=Directory.CreateTempSubdirectory("diskpilot-mft-fallback-").FullName;
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(path,"仍保留.bin"),new byte[13]);
            var env=new Environment();var store=new ScanPerformanceStore(memoryOnly:true);
            string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
            store.Record(new(env.Capability.VolumeKey,key,ScanMethod.DirectoryTraversal,100000,120000,0,0,0,0,env.UtcNow));
            store.Record(new(env.Capability.VolumeKey,"volume",ScanMethod.Mft,1000000,10000,4000,2000,4000,1000000,env.UtcNow));
            var scanner=new FailingMft(new FileScanner(),env,store);
            var result=await scanner.ScanAsync(path,new InlineProgress(_=>{}),CancellationToken.None);
            Assert.Equal(1,scanner.MftCalls);Assert.Equal(13,result.TotalSize);Assert.Equal(path,result.TargetPath);
            Assert.Contains(result.Execution!.Attempts,a=>a.Method==ScanMethod.Mft&&a.Outcome=="failed"&&a.Phase=="volume_open"&&a.Win32Error==5);
            Assert.Equal(ScanMethod.DirectoryTraversal,result.Execution.Method);Assert.Single(result.Execution.FallbackReasons!);
        }
        finally {Directory.Delete(path,true);}
    }
    private sealed class FailingMft(FileScanner inner,IScanRouteEnvironment env,IScanPerformanceStore store)
        :IncrementalFileScanner(inner,routeEnvironment:env,performance:store)
    {
        public int MftCalls;
        protected override (ScanSession session,Dictionary<long,FileEntry> frnIndex)? TryMftScan(string path,string volume,IProgress<ScanProgress> progress,CancellationToken ct)
        {
            MftCalls++;
            typeof(IncrementalFileScanner).GetField("_lastMftFailure",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
                .SetValue(this,((string Phase,int? Error)?)("volume_open",5));
            return null;
        }
    }

    [Fact]
    public void 历史仅有哈希和性能而无路径及文件名()
    {
        var dir=Directory.CreateTempSubdirectory("diskpilot-route-stat-").FullName;
        try
        {
            var path=Path.Combine(dir,"metrics.json");var store=new ScanPerformanceStore(path);
            store.Record(new("hashed-volume","hashed-scope",ScanMethod.DirectoryTraversal,42,123,0,0,0,0,DateTime.UtcNow));
            Assert.Single(new ScanPerformanceStore(path).Read());
            string text=File.ReadAllText(path);Assert.DoesNotContain(dir,text);Assert.DoesNotContain("TargetPath",text);
        }
        finally {Directory.Delete(dir,true);}
    }

    [Fact]
    public async Task 取消不回退且不把失败尝试写成成功指标()
    {
        var path=Directory.CreateTempSubdirectory("diskpilot-route-cancel-").FullName;
        try
        {
            var env=new Environment();var store=new ScanPerformanceStore(memoryOnly:true);
            string scope=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
            store.Record(new(env.Capability.VolumeKey,scope,ScanMethod.DirectoryTraversal,100000,120000,0,0,0,0,env.UtcNow));
            store.Record(new(env.Capability.VolumeKey,"volume",ScanMethod.Mft,1000000,10000,4000,2000,4000,1000000,env.UtcNow));
            using var cancel=new CancellationTokenSource();var reports=new List<ScanProgress>();
            var scanner=new FailingMft(new FileScanner(),env,store);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>scanner.ScanAsync(path,new InlineProgress(p=>
            {reports.Add(p);if(p.Execution?.Method==ScanMethod.Mft)cancel.Cancel();}),cancel.Token));
            Assert.DoesNotContain(reports,p=>p.Execution?.Method==ScanMethod.DirectoryTraversal);
            Assert.Equal(2,store.Read().Count);Assert.Equal(0,scanner.MftCalls);
        }
        finally {Directory.Delete(path,true);}
    }

    [Fact]
    public async Task 仅探测所得新证据允许一次切换失败后仍复用完整目录()
    {
        var path=Directory.CreateTempSubdirectory("diskpilot-probe-switch-").FullName;
        try
        {
            for(int i=0;i<2100;i++)await File.WriteAllTextAsync(Path.Combine(path,$"文件{i}.txt"),"x");
            var env=new Environment();var store=new LateHistory(env,Path.GetFullPath(path));var scanner=new FailingMft(new FileScanner(),env,store);
            var result=await scanner.ScanAsync(path,new InlineProgress(_=>{}),CancellationToken.None);
            Assert.Equal(1,scanner.MftCalls);Assert.Equal(2100,result.TotalFiles);Assert.Equal(2100,result.TotalSize);
            Assert.Equal(2100,result.RootEntry!.Children.DistinctBy(n=>n.Path).Count());
            Assert.Single(result.Execution!.Attempts.Where(a=>a.Method==ScanMethod.Mft));
            Assert.Contains(result.Execution.Attempts,a=>a.Phase=="bounded_probe"&&a.Outcome=="switched");
        }
        finally {Directory.Delete(path,true);}
    }
    private sealed class LateHistory(Environment env,string target):IScanPerformanceStore
    {
        private int _reads;
        public IReadOnlyList<ScanPerformanceSample> Read()
        {
            if(Interlocked.Increment(ref _reads)==1)return [];
            string scope=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(target.ToUpperInvariant())));
            return [new(env.Capability.VolumeKey,scope,ScanMethod.DirectoryTraversal,100000,120000,0,0,0,0,env.UtcNow),
                new(env.Capability.VolumeKey,"volume",ScanMethod.Mft,1000000,10000,4000,2000,4000,1000000,env.UtcNow)];
        }
        public void Record(ScanPerformanceSample sample)=>throw new InvalidOperationException("含失败尝试不能训练为成功样本");
    }

    [Fact]
    public async Task 有效整卷快照只投影目标且规则或卷变化强制失效()
    {
        var path=Directory.CreateTempSubdirectory("diskpilot-cache-route-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(path,"真实.txt"),"x");var env=new Environment();
            var scanner=new CacheScanner(new FileScanner(),env);scanner.Seed(path,env.Capability.VolumeKey);
            var result=await scanner.ScanAsync(path,new InlineProgress(_=>{}),CancellationToken.None);
            Assert.Equal(1,scanner.RefreshCalls);Assert.Equal(ScanMethod.UsnIncremental,result.Execution!.Method);
            Assert.Equal(path,result.RootEntry!.Path);Assert.Null(result.RootEntry.Parent);Assert.Single(result.RootEntry.Children);Assert.Equal(13,result.TotalSize);
            scanner.Seed(path,"other-volume");
            var invalid=await scanner.ScanAsync(path,new InlineProgress(_=>{}),CancellationToken.None);
            Assert.Equal(1,scanner.RefreshCalls);Assert.Equal(ScanMethod.DirectoryTraversal,invalid.Execution!.Method);Assert.Equal(1,invalid.TotalSize);
            scanner.Seed(path,env.Capability.VolumeKey,"changed-rules");
            await scanner.ScanAsync(path,new InlineProgress(_=>{}),CancellationToken.None);Assert.Equal(1,scanner.RefreshCalls);
        }
        finally {Directory.Delete(path,true);}
    }
    private sealed class CacheScanner(FileScanner inner,Environment env):IncrementalFileScanner(inner,routeEnvironment:env)
    {
        public int RefreshCalls;
        public bool RejectRefresh;
        public Guid SeedSameScope(string target,string volumeKey)
        {
            string volume=Path.GetPathRoot(target)!;var root=new FileEntry {Name="目标",Path=target,IsDirectory=true};
            var session=new ScanSession {TargetPath=target,RootEntry=root};
            var state=new VolumeScanState {ScannedPath=target,Watermark=new(1,1,volume),VolumeKey=volumeKey,
                RulesKey=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("[]"))),CachedSession=session};
            var field=typeof(IncrementalFileScanner).GetField("_volumeStates",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            ((System.Collections.IDictionary)field.GetValue(this)!)[volume]=state;return session.Id;
        }
        public void Seed(string target,string volumeKey,string? rules=null)
        {
            string volume=Path.GetPathRoot(target)!;var root=new FileEntry {Name=volume,Path=volume,IsDirectory=true};
            var d=new FileEntry {Name="目标",Path=target,IsDirectory=true,Parent=root,Size=13};root.Children.Add(d);
            d.Children.Add(new FileEntry {Name="缓存.txt",Path=Path.Combine(target,"缓存.txt"),Size=13,Parent=d});
            root.Children.Add(new FileEntry {Name="越界",Path=Path.Combine(volume,"越界"),Size=999,Parent=root});
            var state=new VolumeScanState {ScannedPath=volume,Watermark=new(1,1,volume),VolumeKey=volumeKey,RulesKey=rules??Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("[]"))),
                CachedSession=new ScanSession {TargetPath=volume,RootEntry=root,TotalSize=1012}};
            var field=typeof(IncrementalFileScanner).GetField("_volumeStates",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var states=(System.Collections.IDictionary)field.GetValue(this)!;states[volume]=state;
        }
        protected override ScanSession? RefreshRouteSnapshot(VolumeScanState state,IProgress<ScanProgress> progress,CancellationToken ct)
        {
            RefreshCalls++;
            if(RejectRefresh)
            {
                typeof(IncrementalFileScanner).GetField("_lastIncrementalFailure",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(this,"夹具缓存对象无法确认");return null;
            }
            return state.CachedSession;
        }
    }

    [Fact]
    public void 成本证据含负值或非有限数时不生成伪预测()
    {
        var env=new Environment();var d=ScanRoutePolicy.Decide(env.Capability,false,
            [new("volume-fixture","scope",ScanMethod.DirectoryTraversal,1000,10000,0,0,0,0,env.UtcNow),
             new("volume-fixture","volume",ScanMethod.Mft,1000,1000,-5,double.NaN,2,1000,env.UtcNow)],"scope",env.UtcNow);
        Assert.Equal(ScanMethod.DirectoryTraversal,d.PlannedMethod);Assert.Null(d.MftCost);Assert.Equal("insufficient_evidence",d.ReasonCode);
    }
    [Fact]
    public void 索引内存预算超限时即使看似更快也不盲目重建()
    {
        var env=new Environment();var d=ScanRoutePolicy.Decide(env.Capability,false,
            [new("volume-fixture","scope",ScanMethod.DirectoryTraversal,100000,120000,0,0,0,0,env.UtcNow),
             new("volume-fixture","volume",ScanMethod.Mft,5000000,1000,100,100,100,5000000,env.UtcNow)],"scope",env.UtcNow);
        Assert.Equal(ScanMethod.DirectoryTraversal,d.PlannedMethod);Assert.Equal("index_memory_budget",d.ReasonCode);Assert.True(d.EstimatedIndexMemoryBytes>1024L*1024*1024);
    }
    [Fact]
    public async Task 同范围增量结果获得新扫描标识而不复用旧快照Id()
    {
        var path=Directory.CreateTempSubdirectory("diskpilot-route-id-").FullName;
        try
        {
            var env=new Environment();var scanner=new CacheScanner(new FileScanner(),env);
            var old=scanner.SeedSameScope(path,env.Capability.VolumeKey);
            var result=await scanner.ScanAsync(path,new InlineProgress(_=>{}),CancellationToken.None);
            Assert.NotEqual(old,result.Id);Assert.Equal(ScanMethod.UsnIncremental,result.Execution!.Method);
        }
        finally{Directory.Delete(path,true);}
    }
    [Fact]
    public async Task USN快照失效后最终解释保留真实失效原因()
    {
        var path=Directory.CreateTempSubdirectory("diskpilot-invalid-cache-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(path,"当前.txt"),"x");var env=new Environment();
            var scanner=new CacheScanner(new FileScanner(),env){RejectRefresh=true};scanner.Seed(path,env.Capability.VolumeKey);
            var result=await scanner.ScanAsync(path,new InlineProgress(_=>{}),CancellationToken.None);
            Assert.Equal(ScanMethod.DirectoryTraversal,result.Execution!.Method);
            Assert.Contains("夹具缓存对象无法确认",result.Execution.Decision!.Explanation);
            Assert.Contains(result.Execution.Attempts,a=>a.Phase=="usn_validation"&&a.Outcome=="failed");
        }
        finally{Directory.Delete(path,true);}
    }
    private sealed class InlineProgress(Action<ScanProgress> action):IProgress<ScanProgress>{public void Report(ScanProgress value)=>action(value);}
}
