using System.Reflection;
using System.Windows;
using NSubstitute;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Services;
using DiskPilotX.Core.Services.Scanning;
using DiskPilotX.Core.Services.FileTree;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Cleanup;
using DiskPilotX.Core.Services.Treemap;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ManualRecommendationsTests
{
    [Fact]
    public void 手动追加保留原清单勾选筛选与分析任务且新增需确认()
    {
        var (session, parent, child, other) = Scan();
        var original = new CleanupRecommendation { Id = "1", TargetPath = other.Path, Entry = other,
            IsAccepted = true, Category = RecommendationCategory.LogFiles, SourceScanId = session.Id };
        var vm = new RecommendationsViewModel();
        vm.SetExternalRecommendations([original]);
        vm.SelectedCategoryFilter = RecommendationCategory.LogFiles;
        vm.SelectedSafetyFilter = SafetyRating.Safe;
        var collection = vm.Recommendations;
        var notices = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Recommendations)) notices++; };

        Assert.Equal(1, Add(vm, session, [child]));

        Assert.Same(collection, vm.Recommendations);
        Assert.Same(original, vm.Recommendations[0]);
        Assert.True(original.IsAccepted);
        Assert.Equal(RecommendationCategory.LogFiles, vm.SelectedCategoryFilter);
        Assert.Equal(SafetyRating.Safe, vm.SelectedSafetyFilter);
        Assert.Same(original, Assert.Single(vm.FilteredRecommendations));
        Assert.Equal(1, vm.TotalSelectedCount);
        Assert.Equal(1, notices);
        var manual = vm.Recommendations[1];
        Assert.False(manual.IsAccepted);
        Assert.False(manual.IsDismissed);
        Assert.Equal(SafetyRating.ReviewFirst, manual.SafetyRating);
        Assert.Equal(RecommendationCategory.Other, manual.Category);
        Assert.Contains("手动添加", manual.SourceScope);
        Assert.Contains("需用户确认", manual.Explanation);
        Assert.Contains("未经 AI 风险评估", manual.Explanation);
        Assert.Equal(session.Id, manual.SourceScanId);
        Assert.Same(child, manual.Entry);
        Assert.NotEqual(original.Id, manual.Id);
        Assert.True(vm.HasRecommendations);
        Assert.True(vm.HasAnyRecommendations);
        Assert.Null(vm.AnalysisTask);
    }

    [Fact]
    public void 同批父目录覆盖子项且路径大小写重复不累计()
    {
        var (session, parent, child, _) = Scan();
        var vm = new RecommendationsViewModel();
        Assert.Equal(1, Add(vm, session, [child, parent, parent]));
        Assert.Same(parent, Assert.Single(vm.Recommendations).Entry);
        Assert.Equal(0, Add(vm, session, [parent, child]));
        vm.Recommendations[0].IsAccepted = true;
        vm.UpdateTotals();
        Assert.Equal(1, vm.TotalSelectedCount);
        Assert.Equal("2.0 KB", vm.TotalRecoverableSpace);
    }

    [Fact]
    public void 已有子项不被新父项替换已有大小写路径也不重复()
    {
        var (session, parent, child, _) = Scan();
        var original = new CleanupRecommendation { Id = "1", TargetPath = child.Path.ToUpperInvariant(),
            Entry = child, IsAccepted = true, Size = child.Size };
        var vm = new RecommendationsViewModel();
        vm.SetExternalRecommendations([original]);
        Assert.Equal(0, Add(vm, session, [parent, child]));
        Assert.Same(original, Assert.Single(vm.Recommendations));
        Assert.True(original.IsAccepted);
    }

    [Theory]
    [InlineData("过期对象")]
    [InlineData("已删除")]
    [InlineData("伪聚合")]
    [InlineData("取消扫描")]
    public void 无效选择整批拒绝且不修改原建议(string invalid)
    {
        var (session, _, child, other) = Scan();
        var vm = new RecommendationsViewModel();
        FileEntry candidate = invalid switch
        {
            "过期对象" => new() { Path = child.Path, Size = child.Size },
            "伪聚合" => new() { Path = @"D:\manual-test\其他文件", IsDirectory = true, Children = [child] },
            _ => child
        };
        if (invalid == "已删除") child.IsDeleted = true;
        if (invalid == "取消扫描") session.IsCancelled = true;
        var error = Assert.Throws<TargetInvocationException>(() => Add(vm, session, [other, candidate]));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Empty(vm.Recommendations);
    }

    [Fact]
    public void 追加不把运行或失败分析伪装成成功结果()
    {
        var (session, _, child, _) = Scan();
        var vm = new RecommendationsViewModel();
        vm.BeginAnalysis();
        var title = vm.EmptyStateTitleText;
        Add(vm, session, [child]);
        Assert.True(vm.IsAnalyzing);
        Assert.True(vm.IsWaitingForExternalRecommendations);
        Assert.Equal(title, vm.EmptyStateTitleText);
        Assert.Null(vm.LastDiagnostics);
    }

    [Fact]
    public void 相同范围不同扫描不替换旧证据但不重叠扫描可追加()
    {
        var (session, _, _, _) = Scan();
        var method = typeof(MainWindow).GetMethod("ValidateManualRecommendationScopes", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var replacement = new ScanSession { TargetPath = session.TargetPath, RootEntry = session.RootEntry };
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [replacement, new[] { session }]));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        method.Invoke(null, [session, new[] { session }]);
        var other = new ScanSession { TargetPath = @"E:\manual-other", RootEntry = new FileEntry { Path = @"E:\manual-other", IsDirectory = true } };
        method.Invoke(null, [other, new[] { session }]);
    }

    [Fact]
    public void 同一扫描定位保留现有根而跨扫描必须重建根()
    {
        var (session, _, _, _) = Scan();
        var method = typeof(MainWindow).GetMethod("ShouldResetCachedScanRoot", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.False((bool)method.Invoke(null, [session, session, session.RootEntry])!);
        Assert.True((bool)method.Invoke(null, [new ScanSession(), session, session.RootEntry])!);
        Assert.True((bool)method.Invoke(null, [session, session, new FileEntry()])!);
    }

    [Fact]
    public void 宿主追加保留多范围快照保护并推进清单版本() => WithWindow(window =>
    {
        var (session, _, child, _) = Scan();
        var main = new MainViewModel(Substitute.For<IFileScanner>()) { CurrentSession = session };
        window.DataContext = main;
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(new AppSettings { Language = L.CurrentLanguageName });
        using var clients = new ModelHttpClientFactory();
        var vm = new RecommendationsViewModel();
        window.SetViewModels(vm, new SettingsViewModel(settings,
            new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings)));
        var add = typeof(MainWindow).GetMethod("AddManualRecommendationsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var revision = typeof(MainWindow).GetField("_recommendationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var before = (long)revision.GetValue(window)!;
        Assert.True(((Task)add.Invoke(window, [new[] { child }])!).IsCompletedSuccessfully);
        Assert.True((long)revision.GetValue(window)! > before);
        var original = Assert.Single(vm.Recommendations);
        original.IsAccepted = true;
        vm.UpdateTotals();
        var cache = (ScanSnapshotCache)typeof(MainWindow).GetField("_scanSnapshots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.False(cache.RemoveUnprotected(session.Id));
        var oldVersion = CleanupPlan.ComputeVersion(session, vm.Recommendations, DeletionMode.MoveToRecycleBin);

        var otherRoot = new FileEntry { Path = @"E:\manual-other", IsDirectory = true };
        var other = new FileEntry { Path = @"E:\manual-other\second.tmp", Parent = otherRoot, Size = 1024 };
        otherRoot.Children.Add(other);
        var next = new ScanSession { TargetPath = otherRoot.Path, RootEntry = otherRoot, StartTime = DateTime.Now };
        main.CurrentSession = next;
        Assert.True(((Task)add.Invoke(window, [new[] { other }])!).IsCompletedSuccessfully);
        Assert.Equal(2, vm.Recommendations.Count);
        Assert.Same(original, vm.Recommendations[0]);
        Assert.True(original.IsAccepted);
        Assert.False(vm.Recommendations[1].IsAccepted);
        Assert.False(cache.RemoveUnprotected(session.Id));
        Assert.False(cache.RemoveUnprotected(next.Id));
        var scopes = (Dictionary<string, ScanSession>)typeof(MainWindow).GetField("_publishedScopes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.Equal(2, scopes.Count);
        var planSession = (ScanSession)typeof(MainWindow).GetField("_assistantAnalysisSession", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.Same(child, new FileTreeQueryService().FindByPath(planSession, child.Path));
        Assert.Same(other, new FileTreeQueryService().FindByPath(planSession, other.Path));
        Assert.NotEqual(oldVersion, CleanupPlan.ComputeVersion(planSession, vm.Recommendations, DeletionMode.MoveToRecycleBin));
    });

    [Fact]
    public void 缓存选择同一扫描不重设根缩放或历史() => WithWindow(window =>
    {
        var (session, parent, child, _) = Scan();
        var main = new MainViewModel(Substitute.For<IFileScanner>()) { CurrentSession = session };
        window.DataContext = main;
        var map = new TreemapViewModel(new SquarifiedTreemapLayout());
        map.SetRoot(session.RootEntry!, session);
        map.DrillDown(parent);
        map.Zoom = 2;
        window.SetTreemapViewModel(map);
        var cache = (ScanSnapshotCache)typeof(MainWindow).GetField("_scanSnapshots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        cache.Add(session);
        var select = typeof(MainWindow).GetMethod("TryShowCachedAiScanForPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True((bool)select.Invoke(window, [child.Path, false, session.Id])!);
        Assert.Same(parent, map.CurrentRoot);
        Assert.Equal(2, map.Zoom);
        Assert.True(map.CanGoBack);
        Assert.Same(session, main.CurrentSession);
    });

    private static void WithWindow(Action<MainWindow> action) => StaHost.Run(() =>
    {
        var previous = Application.Current.Resources;
        Application.Current.Resources = new ResourceDictionary
        {
            MergedDictionaries = { new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) } }
        };
        MainWindow? window = null;
        try { window = new MainWindow(); action(window); }
        finally
        {
            if (window is not null)
            {
                typeof(MainWindow).GetField("_closeConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                window.Close();
            }
            Application.Current.Resources = previous;
        }
    });

    [Fact]
    public void 推荐定位接线由过渡回调激活精确快照而非提前切根()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "DiskPilotX.App", "MainWindow.ViewModels.cs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "src", "DiskPilotX.App", "MainWindow.ViewModels.cs"));
        var start = source.IndexOf("private async void OnRecommendationPingRequested", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("private void OnRecommendationActivated", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var handler = source[start..end];
        Assert.Contains("await TreemapView.FocusScanPathAsync", handler);
        Assert.Contains("() => TryShowCachedAiScanForPath", handler);
        Assert.Contains("snapshotId: target.Session.Id", handler);
        Assert.DoesNotContain("var shown = TryShowCachedAiScanForPath", handler);
        Assert.DoesNotContain("NavigateToEntry", handler);
        var activated = source[end..source.IndexOf("private void CacheAiScanSession", end, StringComparison.Ordinal)];
        Assert.Contains("NavigateToEntry(target.Entry)", activated);
        Assert.Contains("FileEntryShellService.Open(target.Entry)", activated);
        Assert.DoesNotContain("FocusScanPathAsync", activated);
    }

    [Fact]
    public void 移除A全部建议后保留B并允许重扫A追加() => WithBoundRecommendations((window, main, vm) =>
    {
        var (a, _, aChild, aOther) = Scan();
        var (b, bEntry) = Scope(@"E:\manual-B");
        AddHost(window, main, a, [aChild, aOther]);
        AddHost(window, main, b, [bEntry]);
        var bItem = vm.Recommendations.Single(item => item.SourceScanId == b.Id);
        bItem.IsAccepted = true;
        var aItems = vm.Recommendations.Where(item => item.SourceScanId == a.Id).ToArray();
        var cache = Field<ScanSnapshotCache>(window, "_scanSnapshots");
        cache.Protect(a.Id, "view");
        cache.Protect(a.Id, "analysis");
        var before = Field<long>(window, "_recommendationRevision");
        var composite = Field<ScanSession>(window, "_assistantAnalysisSession");
        var oldVersion = CleanupPlan.ComputeVersion(composite, vm.Recommendations, DeletionMode.MoveToRecycleBin);
        vm.DismissCommand.Execute(aItems[0]);
        Assert.Same(a, Field<Dictionary<string, ScanSession>>(window, "_publishedScopes")[a.TargetPath]);
        vm.DismissCommand.Execute(aItems[1]);

        var scopes = Field<Dictionary<string, ScanSession>>(window, "_publishedScopes");
        Assert.False(scopes.ContainsKey(a.TargetPath));
        Assert.Same(b, Assert.Single(scopes).Value);
        Assert.Same(bItem, Assert.Single(vm.Recommendations));
        Assert.True(bItem.IsAccepted);
        Assert.Same(b, Field<ScanSession>(window, "_assistantAnalysisSession"));
        Assert.Equal(b.Id.ToString(), Field<string>(window, "_publishedScopeStamp"));
        Assert.True(Field<long>(window, "_recommendationRevision") > before);
        Assert.NotEqual(oldVersion, CleanupPlan.ComputeVersion(b, vm.Recommendations, DeletionMode.MoveToRecycleBin));
        Assert.DoesNotContain((a.Id, "recommendations"), Owners(cache));
        Assert.Contains((a.Id, "view"), Owners(cache));
        Assert.Contains((a.Id, "analysis"), Owners(cache));
        Assert.Contains((b.Id, "recommendations"), Owners(cache));
        Assert.Same(a, cache.FindById(a.Id));

        var (newA, _, newChild, _) = Scan();
        AddHost(window, main, newA, [newChild]);
        Assert.Equal(2, vm.Recommendations.Count);
        Assert.Same(bItem, vm.Recommendations[0]);
        Assert.True(bItem.IsAccepted);
        Assert.Equal(newA.Id, vm.Recommendations[1].SourceScanId);
        Assert.Same(newA, scopes[a.TargetPath]);
        var nextComposite = Field<ScanSession>(window, "_assistantAnalysisSession");
        Assert.NotEqual(composite.Id, nextComposite.Id);
        Assert.Same(newChild, new FileTreeQueryService().FindByPath(nextComposite, newChild.Path));
        Assert.Same(bEntry, new FileTreeQueryService().FindByPath(nextComposite, bEntry.Path));
    });

    [Theory]
    [InlineData("真实实体")]
    [InlineData("仅路径")]
    [InlineData("旧脱离实体")]
    public void 裁剪兼容没有SourceScanId的既有建议(string legacy) => WithBoundRecommendations((window, main, vm) =>
    {
        var (a, _, child, _) = Scan();
        var (b, bEntry) = Scope(@"E:\manual-legacy-B");
        AddHost(window, main, a, [child]);
        AddHost(window, main, b, [bEntry]);
        var bItem = vm.Recommendations.Single(item => item.SourceScanId == b.Id);
        bItem.SourceScanId = null;
        bItem.Entry = legacy == "仅路径" ? null : legacy == "旧脱离实体" ? new FileEntry { Path = bEntry.Path } : bEntry;
        vm.DismissCommand.Execute(vm.Recommendations.Single(item => item.SourceScanId == a.Id));
        Assert.Same(bItem, Assert.Single(vm.Recommendations));
        Assert.Same(b, Assert.Single(Field<Dictionary<string, ScanSession>>(window, "_publishedScopes")).Value);
        Assert.Same(b, Field<ScanSession>(window, "_assistantAnalysisSession"));
        Assert.Contains((b.Id, "recommendations"), Owners(Field<ScanSnapshotCache>(window, "_scanSnapshots")));
    });

    [Fact]
    public void 清空推荐仅释放推荐owner不释放展示和分析owner() => WithBoundRecommendations((window, main, vm) =>
    {
        var (a, _, child, _) = Scan();
        AddHost(window, main, a, [child]);
        var cache = Field<ScanSnapshotCache>(window, "_scanSnapshots");
        cache.Protect(a.Id, "view");
        cache.Protect(a.Id, "analysis");
        var batch = new AssistantAnalysisBatch();
        batch.AddOrReplace(new AssistantAnalysisScope { Path = a.TargetPath, Session = a });
        SetField(window, "_analysisBatch", batch);
        vm.ClearRecommendationsCommand.Execute(null);
        Assert.Empty(Field<Dictionary<string, ScanSession>>(window, "_publishedScopes"));
        Assert.Null(typeof(MainWindow).GetField("_assistantAnalysisSession", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
        Assert.Same(batch, Field<AssistantAnalysisBatch>(window, "_analysisBatch"));
        Assert.DoesNotContain((a.Id, "recommendations"), Owners(cache));
        Assert.Contains((a.Id, "view"), Owners(cache));
        Assert.Contains((a.Id, "analysis"), Owners(cache));
        Assert.Same(a, cache.FindById(a.Id));
        Assert.Equal(string.Empty, Field<string>(window, "_publishedScopeStamp"));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 异步发布准备期间空集合不裁剪新注册scope提交后重建来源(bool publishing) => WithBoundRecommendations((window, main, vm) =>
    {
        var (a, _, child, _) = Scan();
        var (b, bEntry) = Scope(@"E:\manual-publish-B");
        var (c, cEntry) = Scope(@"F:\manual-publish-C");
        AddHost(window, main, a, [child]);
        AddHost(window, main, b, [bEntry]);
        var bItem = vm.Recommendations.Single(item => item.SourceScanId == b.Id);
        var cache = Field<ScanSnapshotCache>(window, "_scanSnapshots");
        SetField(window, "_recommendationPublishing", publishing);
        try
        {
            var register = typeof(MainWindow).GetMethod("RegisterRecommendationScopes", BindingFlags.Instance | BindingFlags.NonPublic)!;
            register.Invoke(window, [new[] { c }, true]);
            vm.BeginExternalRecommendationLoad();
            var scopes = Field<Dictionary<string, ScanSession>>(window, "_publishedScopes");
            Assert.Equal(3, scopes.Count);
            Assert.Contains((c.Id, "recommendations"), Owners(cache));
            vm.SetExternalRecommendations([bItem, new CleanupRecommendation
            {
                Id = "publish-C", SourceScanId = c.Id, TargetPath = cEntry.Path, Entry = cEntry
            }]);
            Assert.Equal(publishing ? 3 : 2, scopes.Count);
            // 模拟发布的明确提交点；在最终推荐写入后重建，而非沿用注册阶段的组合会话。
            var rebuild = typeof(MainWindow).GetMethod("RebuildReferencedRecommendationSession", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(rebuild);
            SetField(window, "_assistantAnalysisSession", rebuild.Invoke(window, null));
            Assert.Equal(2, scopes.Count);
            Assert.False(scopes.ContainsKey(a.TargetPath));
            Assert.Same(b, scopes[b.TargetPath]);
            Assert.Same(c, scopes[c.TargetPath]);
            var composite = Field<ScanSession>(window, "_assistantAnalysisSession");
            Assert.Same(bEntry, new FileTreeQueryService().FindByPath(composite, bEntry.Path));
            Assert.Same(cEntry, new FileTreeQueryService().FindByPath(composite, cEntry.Path));
            Assert.DoesNotContain((a.Id, "recommendations"), Owners(cache));
            Assert.Contains((b.Id, "recommendations"), Owners(cache));
            Assert.Contains((c.Id, "recommendations"), Owners(cache));
        }
        finally { SetField(window, "_recommendationPublishing", false); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AI合并同根新扫描在修改清单或owner之前拒绝(bool withNewItem) => WithBoundRecommendations((window, main, vm) =>
    {
        var (oldScan, _, oldChild, _) = Scan();
        AddHost(window, main, oldScan, [oldChild]);
        var kept=Assert.Single(vm.Recommendations);kept.IsAccepted=true;
        var oldId=kept.Id;var oldVersion=Field<long>(window,"_recommendationRevision");
        var oldComposite=Field<ScanSession>(window,"_assistantAnalysisSession");
        var (newScan, _, newChild, newOther) = Scan();main.CurrentSession=newScan;
        var incoming=new List<CleanupRecommendation> {new(){Id="incoming",TargetPath=newChild.Path,Entry=newChild,SourceScanId=newScan.Id}};
        if(withNewItem)incoming.Add(new(){Id="new-item",TargetPath=newOther.Path,Entry=newOther,SourceScanId=newScan.Id});
        var final=DiskPilotX.Core.Services.Analysis.RecommendationEngine.MergeRecommendations(vm.Recommendations,incoming);
        Assert.Same(kept,final[0]);
        var cache=Field<ScanSnapshotCache>(window,"_scanSnapshots");var owners=Owners(cache).ToHashSet();
        var register=typeof(MainWindow).GetMethod("RegisterRecommendationScopes",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var error=Assert.Throws<TargetInvocationException>(()=>register.Invoke(window,[new[]{newScan},true]));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Contains("另一份扫描",error.InnerException!.Message);
        Assert.Same(kept,Assert.Single(vm.Recommendations));Assert.True(kept.IsAccepted);Assert.Equal(oldId,kept.Id);
        Assert.Equal(oldScan.Id,kept.SourceScanId);
        Assert.Same(oldScan,Assert.Single(Field<Dictionary<string,ScanSession>>(window,"_publishedScopes")).Value);
        Assert.Same(oldComposite,Field<ScanSession>(window,"_assistantAnalysisSession"));
        Assert.Equal(oldVersion,Field<long>(window,"_recommendationRevision"));
        Assert.True(owners.SetEquals(Owners(cache)));Assert.Null(cache.FindById(newScan.Id));
    });

    private static T Field<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static void SetField(MainWindow window, string name, object? value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static HashSet<(Guid Id, string Owner)> Owners(ScanSnapshotCache cache) =>
        (HashSet<(Guid Id, string Owner)>)typeof(ScanSnapshotCache).GetField("protectedItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;

    private static void AddHost(MainWindow window, MainViewModel main, ScanSession session, IReadOnlyList<FileEntry> entries)
    {
        main.CurrentSession = session;
        var method = typeof(MainWindow).GetMethod("AddManualRecommendationsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(((Task)method.Invoke(window, [entries])!).IsCompletedSuccessfully);
    }

    private static (ScanSession Session, FileEntry Entry) Scope(string path)
    {
        var root = new FileEntry { Path = path, IsDirectory = true };
        var entry = new FileEntry { Path = path + @"\sample.tmp", Parent = root, Size = 1024 };
        root.Children.Add(entry);
        return (new ScanSession { TargetPath = path, RootEntry = root, StartTime = DateTime.Now }, entry);
    }

    private static void WithBoundRecommendations(Action<MainWindow, MainViewModel, RecommendationsViewModel> action) => WithWindow(window =>
    {
        var main = new MainViewModel(Substitute.For<IFileScanner>());
        window.DataContext = main;
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(new AppSettings { Language = L.CurrentLanguageName });
        using var clients = new ModelHttpClientFactory();
        var vm = new RecommendationsViewModel();
        window.SetViewModels(vm, new SettingsViewModel(settings,
            new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings)));
        action(window, main, vm);
    });

    private static int Add(RecommendationsViewModel vm, ScanSession session, IReadOnlyList<FileEntry> entries)
    {
        var method = typeof(RecommendationsViewModel).GetMethod("AddManualRecommendations", [typeof(ScanSession), typeof(IReadOnlyList<FileEntry>)]);
        Assert.NotNull(method);
        return (int)method.Invoke(vm, [session, entries])!;
    }

    private static (ScanSession Session, FileEntry Parent, FileEntry Child, FileEntry Other) Scan()
    {
        var root = new FileEntry { Path = @"D:\manual-test", IsDirectory = true };
        var parent = new FileEntry { Path = @"D:\manual-test\folder", IsDirectory = true, Parent = root, Size = 2048 };
        var child = new FileEntry { Path = @"D:\manual-test\folder\item.tmp", Parent = parent, Size = 1024 };
        var other = new FileEntry { Path = @"D:\manual-test\other.log", Parent = root, Size = 512 };
        parent.Children.Add(child);
        root.Children.AddRange([parent, other]);
        return (new ScanSession { TargetPath = root.Path, RootEntry = root, StartTime = DateTime.Now }, parent, child, other);
    }
}
