using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Whitelist;

namespace DiskPilotX.Core.Services.Scanning;

/// <summary>
/// Decorator around FileScanner that uses the NTFS USN change journal
/// for near-instant rescans of previously-scanned paths.
/// Falls back to full scan on any failure.
/// </summary>
public partial class IncrementalFileScanner : IFileScanner
{
    private readonly FileScanner _inner;
    private readonly ISettingsService? _settingsService;
    private readonly IPathWhitelistMatcher _whitelistMatcher;
    private readonly ILogger<IncrementalFileScanner> _logger;
    private readonly Dictionary<string, VolumeScanState> _volumeStates = new(StringComparer.OrdinalIgnoreCase);
    private Task? _pendingIndexBuild;
    private readonly IScanRouteEnvironment _routeEnvironment;
    private readonly IScanPerformanceStore _performance;
    private readonly SemaphoreSlim _scanGate = new(1,1);
    private (string Phase, int? Error)? _lastMftFailure;
    private string? _lastIncrementalFailure;
    private (double IndexMs, double TreeMs, double SizeMs, long Records)? _lastMftMetrics;

    public bool IsReady => _pendingIndexBuild is null or { IsCompleted: true };
    public event Action? IsReadyChanged;

    public IncrementalFileScanner(FileScanner inner, ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null, ILogger<IncrementalFileScanner>? logger = null,
        IScanRouteEnvironment? routeEnvironment = null, IScanPerformanceStore? performance = null)
    {
        _inner = inner;
        _settingsService = settingsService;
        _whitelistMatcher = whitelistMatcher ?? new PathWhitelistMatcher();
        _logger = logger ?? NullLogger<IncrementalFileScanner>.Instance;
        _routeEnvironment = routeEnvironment ?? new ScanRouteEnvironment();
        _performance = performance ?? new ScanPerformanceStore(memoryOnly: true);
    }

    public async Task<ScanSession> ScanAsync(string path, IProgress<ScanProgress> progress, CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ScanAdaptiveCore(path, progress, cancellationToken).ConfigureAwait(false); }
        finally { _scanGate.Release(); }
    }

    private async Task<ScanSession> ScanAdaptiveCore(string path, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_pendingIndexBuild is { IsCompleted: false })
            await _pendingIndexBuild.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        string fullPath = ScanPathResolver.Resolve(path);
        var whitelist = _settingsService?.LoadSettings().ScanWhitelist ?? [];
        if (_whitelistMatcher.IsExcluded(fullPath, whitelist))
            throw new InvalidOperationException("Scan target is excluded by whitelist settings.");
        string volume = Path.GetPathRoot(fullPath) ?? fullPath;
        string scopeKey = ScanRouteEnvironment.Hash(fullPath.ToUpperInvariant());
        string rulesKey = ScanRouteEnvironment.Hash(System.Text.Json.JsonSerializer.Serialize(whitelist));
        var capability = await Task.Run(() => _routeEnvironment.Inspect(volume), ct).ConfigureAwait(false);
        var decision = ScanRoutePolicy.Decide(capability, IsWholeVolumeRoot(fullPath), _performance.Read(), scopeKey, _routeEnvironment.UtcNow);
        var execution = new ScanExecutionInfo(fullPath, ScanMethod.Unknown, ScanExecutionStatus.Running, DateTime.Now) { Decision = decision };
        var attempts = new List<ScanRouteAttempt>();
        var startWatermark = capability.CanUseMft ? QueryRouteWatermark(volume) : null;
        var total = System.Diagnostics.Stopwatch.StartNew();
        DateTime attemptStart = DateTime.Now;
        ScanMethod activeMethod = ScanMethod.Unknown;
        bool switched = false;
        bool traversalComplete = false;
        var relay = new RouteProgress(p => progress.Report(p with { Execution = execution with { Decision = decision, Attempts = attempts.ToArray() } }));
        void Explain() => progress.Report(new ScanProgress(decision.Explanation,0,0)
            {Execution=execution with {Decision=decision,Attempts=attempts.ToArray()}});
        void Enter(ScanMethod method)
        {
            activeMethod = method; attemptStart=DateTime.Now;
            execution = execution with {Method=method,Decision=decision,Attempts=attempts.ToArray()};
            Explain();
            _logger.LogInformation("扫描决策：{ReasonCode}，计划{Planned}，实际尝试{Actual}；{Explanation}",decision.ReasonCode,decision.PlannedMethod,method,decision.Explanation);
        }
        void End(string outcome,string phase,int? error=null,string? detail=null)
        {
            attempts.Add(new(activeMethod,attemptStart,DateTime.Now,outcome,phase,error,detail));
            execution=execution with {Attempts=attempts.ToArray()};
        }
        void Fallback(string reason)
        {
            execution=execution with {FallbackReasons=[..execution.FallbackReasons??[],reason]};
            _logger.LogWarning("扫描路线回退：{Reason}",reason);
        }
        ScanSession Complete(ScanSession session, bool record)
        {
            if(execution.FallbackReasons is {Count:>0})
                decision=decision with {Explanation=decision.Explanation+" 之前路线未采用："+string.Join("；",execution.FallbackReasons)};
            execution=execution with {Decision=decision,Attempts=attempts.ToArray(),Status=session.IsCancelled?ScanExecutionStatus.Cancelled:ScanExecutionStatus.Completed,
                CompletedAt=session.EndTime??DateTime.Now};session.Execution=execution;
            if(record&&!session.IsCancelled&&session.RootEntry is not null
                && !attempts.Any(a=>a.Outcome=="failed"))
            {
                var m=_lastMftMetrics;
                _performance.Record(new(capability.VolumeKey,scopeKey,execution.Method,(long)session.TotalFiles+session.TotalFolders,
                    total.Elapsed.TotalMilliseconds,m?.IndexMs??0,m?.TreeMs??0,m?.SizeMs??0,m?.Records??0,_routeEnvironment.UtcNow));
            }
            _logger.LogInformation("扫描决策完成：{Execution}",System.Text.Json.JsonSerializer.Serialize(execution.ToPublicData()));
            progress.Report(new ScanProgress(session.IsCancelled?"扫描已取消":"扫描完成",session.TotalFiles,session.TotalFolders){Execution=execution});
            return session;
        }
        Explain();
        _lastMftMetrics=null;
        try
        {
            if (capability.CanUseMft && _volumeStates.TryGetValue(volume,out var state) && state.Watermark is not null
                && state.RulesKey==rulesKey && state.VolumeKey==capability.VolumeKey
                && (string.Equals(state.ScannedPath,fullPath,StringComparison.OrdinalIgnoreCase)
                    || IsWholeVolumeRoot(state.ScannedPath) && Under(fullPath,state.ScannedPath)))
            {
                state.ScanWhitelist = whitelist.ToArray();
                bool wholeCache = !string.Equals(state.ScannedPath,fullPath,StringComparison.OrdinalIgnoreCase);
                decision=decision with {PlannedMethod=ScanMethod.UsnIncremental,ReasonCode=wholeCache?"volume_snapshot_refresh":"same_scope_incremental",
                    Explanation=wholeCache?"检测到覆盖目标的卷快照；先校验并应用USN变化，再只返回所选子树。":"检测到同范围快照；先校验USN水位，再增量处理变化。"};
                Enter(ScanMethod.UsnIncremental);
                _lastIncrementalFailure=null;
                var refreshed=await Task.Run(()=>RefreshRouteSnapshot(state,relay,ct),ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if(refreshed is not null)
                {
                    var scoped=wholeCache?ProjectScope(refreshed,fullPath,ct):new ScanSession
                    {
                        TargetPath=refreshed.TargetPath,RootEntry=refreshed.RootEntry,StartTime=execution.StartedAt??DateTime.Now,
                        EndTime=refreshed.EndTime,TotalFiles=refreshed.TotalFiles,TotalFolders=refreshed.TotalFolders,TotalSize=refreshed.TotalSize,
                        DriveCapacity=refreshed.DriveCapacity,DriveFreeSpace=refreshed.DriveFreeSpace,Provenance=refreshed.Provenance
                    };
                    if(scoped is not null)
                    {
                        if(!wholeCache)state.CachedSession=scoped;
                        End("completed",wholeCache?"refresh_and_project":"usn_incremental");
                        return Complete(scoped,false);
                    }
                }
                End("failed","usn_validation",null,_lastIncrementalFailure??"日志水位失效或目标不在刷新后的快照内");
                _volumeStates.Remove(volume);
                _lastMftMetrics=null;
                Fallback("USN快照校验失败："+(_lastIncrementalFailure??"水位失效或目标不在有效覆盖范围")+"；未使用旧数据，重新选择扫描路线。");
                decision=ScanRoutePolicy.Decide(capability,IsWholeVolumeRoot(fullPath),_performance.Read(),scopeKey,_routeEnvironment.UtcNow);
            }
            else if (_volumeStates.ContainsKey(volume))
            {
                _volumeStates.Remove(volume);
                decision=decision with {Explanation=decision.Explanation+" 已丢弃范围、卷身份、规则或访问条件不匹配的旧快照。"};
            }

            (ScanSession session,Dictionary<long,FileEntry> index)? RunMft()
            {
                _lastMftFailure=null;
                var result=TryMftScan(fullPath,volume,relay,ct);
                ct.ThrowIfCancellationRequested();
                if(result is null)
                {
                    var fail=_lastMftFailure??("mft_execution",(int?)null);
                    End("failed",fail.Item1,fail.Item2,"MFT执行未完成");
                    Fallback($"MFT阶段{fail.Item1}失败"+(fail.Item2.HasValue?$"，Win32错误{fail.Item2}":"")+"，接回原范围目录遍历。");
                }
                return result;
            }
            if(decision.PlannedMethod==ScanMethod.Mft)
            {
                Enter(ScanMethod.Mft);
                var result=await Task.Run(RunMft,ct).ConfigureAwait(false);
                if(result is {} success)
                {
                    End("completed","mft_scan");SaveMftState(success.session,success.index,fullPath,volume,capability.VolumeKey,rulesKey,startWatermark,ct);
                    return Complete(success.session,true);
                }
                switched=true;
                decision=decision with {PlannedMethod=ScanMethod.DirectoryTraversal,ReasonCode="mft_fallback",Explanation=execution.FallbackReasons!.Last()};
            }
            Enter(ScanMethod.DirectoryTraversal);
            var session=await _inner.ScanAdaptiveAsync(fullPath,relay,ct,observation=>
            {
                ct.ThrowIfCancellationRequested();
                if(observation.Completed)
                {
                    traversalComplete=true;
                    if(!switched)decision=decision with {ReasonCode="probe_completed",Explanation=capability.Description+" 目标已在渐进遍历中完成，直接复用结果，无需新建整卷索引。",Observation=observation};
                    return null;
                }
                if(switched)return null;
                var next=ScanRoutePolicy.Decide(capability,false,_performance.Read(),scopeKey,_routeEnvironment.UtcNow,observation);
                decision=next with {Observation=observation};progress.Report(new ScanProgress($"渐进探测已处理{observation.Entries}条，{observation.Elapsed.TotalMilliseconds:F0}ms；{next.Explanation}",0,0)
                    {Execution=execution with {Decision=decision,Attempts=attempts.ToArray()}});
                if(next.PlannedMethod!=ScanMethod.Mft)return null;
                switched=true;End("switched","bounded_probe",null,$"已探测{observation.Entries}条，{observation.Elapsed.TotalMilliseconds:F0}ms；原枚举保留");
                Enter(ScanMethod.Mft);
                var mft=RunMft();
                if(mft is {} success)
                {
                    End("completed","mft_scan");SaveMftState(success.session,success.index,fullPath,volume,capability.VolumeKey,rulesKey,startWatermark,ct);
                    return success.session;
                }
                decision=decision with {PlannedMethod=ScanMethod.DirectoryTraversal,ReasonCode="mft_fallback",Explanation=execution.FallbackReasons!.Last()+"已探测节点和枚举位置继续复用。"};
                Enter(ScanMethod.DirectoryTraversal);return null;
            }).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if(activeMethod==ScanMethod.DirectoryTraversal)End(session.IsCancelled?"cancelled":"completed","directory_traversal");
            if(!session.IsCancelled&&activeMethod==ScanMethod.DirectoryTraversal&&session.RootEntry is not null)
            {
                // 仅同范围的受限索引，不能冒充完整卷快照；规则/卷身份随后每次核对。
                if (startWatermark is not null && (long)session.TotalFiles+session.TotalFolders<=500000) _pendingIndexBuild=Task.Run(()=>
                {
                    try { _volumeStates.Clear();CaptureWatermark(session,fullPath,volume,startWatermark);if(_volumeStates.TryGetValue(volume,out var cached)){cached.VolumeKey=capability.VolumeKey;cached.RulesKey=rulesKey;} }
                    catch(Exception e){_logger.LogWarning(e,"扫描后水位记录失败");}
                }).ContinueWith(_=>IsReadyChanged?.Invoke(),TaskScheduler.Default);
                IsReadyChanged?.Invoke();
            }
            return Complete(session,traversalComplete||activeMethod==ScanMethod.Mft);
        }
        catch(OperationCanceledException)
        {
            End("cancelled","cancelled");
            execution=execution with {Decision=decision,Attempts=attempts.ToArray(),Status=ScanExecutionStatus.Cancelled,CompletedAt=DateTime.Now};
            progress.Report(new ScanProgress("扫描已取消，未回退或记录成功样本",0,0){Execution=execution});throw;
        }
        catch(Exception)
        {
            End("failed","execution");progress.Report(new ScanProgress("扫描失败",0,0){Execution=execution with {Status=ScanExecutionStatus.Failed,Decision=decision,Attempts=attempts.ToArray(),CompletedAt=DateTime.Now}});throw;
        }
    }

    protected virtual UsnWatermark? QueryRouteWatermark(string volume) => UsnJournalReader.QueryJournal(volume);
    protected virtual ScanSession? RefreshRouteSnapshot(VolumeScanState state,IProgress<ScanProgress> progress,CancellationToken ct)
        => TryIncrementalRescan(state,progress,ct);

    private void SaveMftState(ScanSession session,Dictionary<long,FileEntry> index,string target,string volume,string volumeKey,string rulesKey,UsnWatermark? watermark,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 使用读取开始前的水位，下一轮必须重放扫描期间变化，不能跳过并发写入。
        // 只保留一个卷的索引，避免连续扫描多个卷累计全部文件树。
        _volumeStates.Clear();
        if(watermark is not null&&index.Count<=500000)_volumeStates[volume]=new VolumeScanState {ScannedPath=target,CachedSession=session,Watermark=watermark,
            FrnIndex=index,VolumeKey=volumeKey,RulesKey=rulesKey};
    }

    private static bool Under(string path,string parent) => path.TrimEnd('\\','/').StartsWith(parent.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static ScanSession? ProjectScope(ScanSession source,string target,CancellationToken ct)
    {
        if(source.RootEntry is null)return null;
        var pending=new Stack<FileEntry>();pending.Push(source.RootEntry);FileEntry? found=null;
        while(pending.TryPop(out var n))
        {
            ct.ThrowIfCancellationRequested();if(string.Equals(n.Path.TrimEnd('\\','/'),target.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)){found=n;break;}
            foreach(var child in n.Children)if(child.IsDirectory&&(string.Equals(target.TrimEnd('\\','/'),child.Path.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)||Under(target,child.Path)))pending.Push(child);
        }
        if(found is null)return null;
        FileEntry Copy(FileEntry e,FileEntry? parent)
        {
            ct.ThrowIfCancellationRequested();var n=new FileEntry {Name=e.Name,Path=e.Path,Size=e.Size,AllocatedSize=e.AllocatedSize,HasAllocatedSize=e.HasAllocatedSize,
                IsDirectory=e.IsDirectory,IsReparsePoint=e.IsReparsePoint,IsAccessDenied=e.IsAccessDenied,IsCloudPlaceholder=e.IsCloudPlaceholder,
                Attributes=e.Attributes,LastModified=e.LastModified,Extension=e.Extension,FileReferenceNumber=e.FileReferenceNumber,Parent=parent,Depth=parent is null?0:parent.Depth+1};
            n.Children=e.Children.Select(c=>Copy(c,n)).ToList();return n;
        }
        var root=Copy(found,null);var (files,folders)=CountEntries(root);
        return new ScanSession {TargetPath=target,RootEntry=root,StartTime=DateTime.Now,EndTime=DateTime.Now,TotalFiles=files,TotalFolders=folders,TotalSize=root.Size};
    }
    private sealed class RouteProgress(Action<ScanProgress> report):IProgress<ScanProgress>{public void Report(ScanProgress value)=>report(value);}

    protected class VolumeScanState
    {
        public IReadOnlyList<PathWhitelistEntry> ScanWhitelist { get; set; } = [];
        public string? VolumeKey { get; set; }
        public string? RulesKey { get; set; }
        public string ScannedPath { get; set; } = string.Empty;
        public ScanSession? CachedSession { get; set; }
        public UsnWatermark? Watermark { get; set; }
        public Dictionary<long, FileEntry> FrnIndex { get; set; } = new();
    }
}
