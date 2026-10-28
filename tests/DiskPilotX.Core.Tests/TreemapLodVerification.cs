using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.Core.Tests;

/// <summary>
/// 用虚拟文件系统验证 LOD「zoom in → 展开更多细节」效果。
/// 不扫真实磁盘：目录层级、子项数量、文件大小全由我们构造。
/// </summary>
public class TreemapLodVerification
{
    [Fact]
    public void ZoomIn_ProgressivelyRevealsDeeperAndMoreNodes()
    {
        // —— 构造一个「深 + 宽」的虚拟目录树 ——
        // 根下 6 个子目录，每个子目录再递归 6 层，每层 4 个子目录 + 6 个文件，
        // 文件大小 8KB~128MB 不等，模拟真实磁盘的尺寸分布。
        var root = new FileEntry { Name = "C:", Path = "C:", IsDirectory = true };

        void BuildLevel(FileEntry dir, int depth, int maxDepth, Random rng)
        {
            if (depth >= maxDepth)
                return;

            int subdirs = 4;
            int files = 6;

            for (int d = 0; d < subdirs; d++)
            {
                var sub = new FileEntry
                {
                    Name = $"dir_{depth}_{d}",
                    Path = $"{dir.Path}/dir_{depth}_{d}",
                    IsDirectory = true,
                    Parent = dir,
                };
                dir.Children.Add(sub);
                BuildLevel(sub, depth + 1, maxDepth, rng);
            }

            for (int f = 0; f < files; f++)
            {
                long size = (long)(8 * 1024 * Math.Pow(2, rng.Next(0, 14))); // 8KB ~ 128MB
                dir.Children.Add(new FileEntry
                {
                    Name = $"file_{depth}_{f}.{PickExt(rng)}",
                    Path = $"{dir.Path}/file_{depth}_{f}",
                    Size = size,
                    Extension = ".dat",
                    IsDirectory = false,
                    Parent = dir,
                });
            }
        }

        var rng = new Random(12345);
        for (int top = 0; top < 6; top++)
        {
            var sub = new FileEntry
            {
                Name = $"Top_{top}",
                Path = $"C:/Top_{top}",
                IsDirectory = true,
                Parent = root,
            };
            root.Children.Add(sub);
            BuildLevel(sub, 0, 6, rng);
        }

        // 回填 Size（后序遍历）。
        void ComputeSize(FileEntry dir)
        {
            if (!dir.IsDirectory)
                return;
            long total = 0;
            foreach (var c in dir.Children)
            {
                ComputeSize(c);
                total += c.Size;
            }
            dir.Size = total;
        }
        ComputeSize(root);

        var engine = new SquarifiedTreemapLayout();

        // —— 在四档 zoom 下分别布局，统计节点数与最大展开深度 ——
        var rows = new List<(float zoom, int count, int maxDepth)>();
        foreach (float zoom in new[] { 1f, 2f, 4f, 8f })
        {
            var nodes = engine.ComputeLayout(root, 1200, 800, 8, zoom);
            int maxDepth = nodes.Count == 0 ? 0 : nodes.Max(n => n.Depth);
            rows.Add((zoom, nodes.Count, maxDepth));
            System.Console.WriteLine($"[LOD] zoom={zoom,-3} 节点数={nodes.Count,-6} 最大深度={maxDepth}");
        }

        // —— 断言：zoom 越大，节点越多、展开越深（单调不减） ——
        for (int i = 1; i < rows.Count; i++)
        {
            Assert.True(rows[i].count >= rows[i - 1].count,
                $"zoom={rows[i].zoom} 节点数({rows[i].count})不应少于 zoom={rows[i - 1].zoom}({rows[i - 1].count})");
            Assert.True(rows[i].maxDepth >= rows[i - 1].maxDepth,
                $"zoom={rows[i].zoom} 最大深度({rows[i].maxDepth})不应少于 zoom={rows[i - 1].zoom}({rows[i - 1].maxDepth})");
        }

        // 最终必须有「明显更多」的节点被揭示（不是仅仅 +1）。
        Assert.True(rows[^1].count > rows[0].count * 2,
            $"zoom=8 节点数({rows[^1].count})应显著超过 zoom=1({rows[0].count})的 2 倍");
        Assert.True(rows[^1].maxDepth > rows[0].maxDepth,
            $"zoom=8 最大深度({rows[^1].maxDepth})应深于 zoom=1({rows[0].maxDepth})");
    }

    private static string PickExt(Random rng) =>
        new[] { ".js", ".cs", ".png", ".mp4", ".zip", ".dll", ".json", ".nvph" }[rng.Next(8)];
}
