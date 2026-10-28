using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// 应用语言是进程级状态，不能并发切换；共享 WPF Application 在一次性 STA 测试之后创建，
// 避免旧测试创建的线程退出后还让其他线程读写它的资源字典。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TestCollectionOrderer("DiskPilotX.App.Tests.LocalizationCollectionOrderer", "DiskPilotX.App.Tests")]

namespace DiskPilotX.App.Tests;

public sealed class LocalizationCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> collections) =>
        collections.OrderBy(c => c.DisplayName is "聊天滚动控件" or "本地化状态" ? 1 : 0)
            .ThenBy(c => c.DisplayName, StringComparer.Ordinal);
}
