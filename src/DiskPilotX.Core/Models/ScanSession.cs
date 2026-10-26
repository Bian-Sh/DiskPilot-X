namespace DiskPilotX.Core.Models;

public class ScanSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TargetPath { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int TotalFiles { get; set; }
    public int TotalFolders { get; set; }
    public long TotalSize { get; set; }
    public FileEntry? RootEntry { get; set; }
    public bool IsCancelled { get; set; }
    public long? DriveCapacity { get; set; }
    public long? DriveFreeSpace { get; set; }
}
