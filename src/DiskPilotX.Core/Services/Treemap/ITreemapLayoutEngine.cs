using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Treemap;

public interface ITreemapLayoutEngine
{
    List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth, float zoom = 1f);
}
