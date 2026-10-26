using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Whitelist;

public interface IPathWhitelistMatcher
{
    bool IsExcluded(string? path, IEnumerable<PathWhitelistEntry>? whitelist);

    IReadOnlyList<PathWhitelistEntry> MergeEntries(
        IEnumerable<PathWhitelistEntry>? current,
        IEnumerable<PathWhitelistEntry>? incoming);
}
