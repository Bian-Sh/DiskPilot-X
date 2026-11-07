using DiskPilotX.Core.Models;
namespace DiskPilotX.App.Views;

// 单击等系统双击间隔结束再发出；双击先取消尚未执行的 Ping。
internal sealed class RecommendationPathGesture(Action<CleanupRecommendation> ping, Action<CleanupRecommendation> activate)
{
    private CleanupRecommendation? pending;
    internal void Queue(CleanupRecommendation item) => pending = item;
    internal void Complete() { var item = pending; pending = null; if (item is not null) ping(item); }
    internal void Activate(CleanupRecommendation item) { pending = null; activate(item); }
    internal void Cancel() => pending = null;
}
