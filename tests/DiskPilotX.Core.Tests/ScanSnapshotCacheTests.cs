using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;
using FluentAssertions;

namespace DiskPilotX.Core.Tests;

public class ScanSnapshotCacheTests
{
    private static ScanSession Session(string path, int files = 1) => new() { TargetPath = path, RootEntry = new() { Path = path, Name = "样本", IsDirectory = true }, TotalFiles = files, StartTime = DateTime.Now };

    [Fact]
    public void 同路径不同扫描保留独立标识并可精确取回()
    {
        var cache = new ScanSnapshotCache(3, 100);
        var first = Session(@"D:\样本"); var second = Session(@"D:\样本");
        var a = cache.Add(first); var b = cache.Add(second);
        a.Id.Should().NotBe(b.Id);
        cache.TryGet(a, out var found).Should().BeTrue(); found.Should().BeSameAs(first);
        cache.TryGet(b, out found).Should().BeTrue(); found.Should().BeSameAs(second);
    }

    [Fact]
    public void 旧进程标识失效且预算只回收未受保护结果()
    {
        var cache = new ScanSnapshotCache(2, 100);
        var a = cache.Add(Session(@"D:\甲")); cache.Protect(a.Id);
        var b = cache.Add(Session(@"D:\乙"));
        cache.Add(Session(@"D:\丙"));
        cache.TryGet(a, out _).Should().BeTrue(); cache.TryGet(b, out _).Should().BeFalse();
        new ScanSnapshotCache().TryGet(a, out _).Should().BeFalse();
        cache.Unprotect(a.Id);
    }

    [Fact]
    public void 预算不能静默驱逐活动证据且超大扫描明确失败()
    {
        var cache = new ScanSnapshotCache(1, 10);
        var a = cache.Add(Session(@"D:\甲")); cache.Protect(a.Id);
        var add = () => cache.Add(Session(@"D:\乙"));
        add.Should().Throw<InvalidOperationException>();
        cache.TryGet(a, out _).Should().BeTrue();
        var large = () => new ScanSnapshotCache(2, 1).Add(Session(@"D:\大", 20));
        large.Should().Throw<InvalidOperationException>();
    }
}
