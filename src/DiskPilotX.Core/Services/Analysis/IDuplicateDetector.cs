using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Analysis;

public record DuplicateGroup(string Hash, long Size, List<FileEntry> Files);

public interface IDuplicateDetector
{
    Task<List<DuplicateGroup>> FindDuplicatesAsync(FileEntry root, CancellationToken cancellationToken = default);
}
