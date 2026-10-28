using System.IO;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DiskPilotX.Core.Models;

public class FileEntry : INotifyPropertyChanged
{
    private bool _isDeleted;
    [JsonIgnore]
    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (_isDeleted == value) return;
            _isDeleted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDeleted)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public long AllocatedSize { get; set; }
    public bool HasAllocatedSize { get; set; }
    public string? Extension { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsReparsePoint { get; set; }
    public bool IsAccessDenied { get; set; }
    public bool IsCloudPlaceholder { get; set; }
    public FileAttributes? Attributes { get; set; }
    public byte[]? ContentHash { get; set; }
    public long FileReferenceNumber { get; set; }
    public FileEntry? Parent { get; set; }
    public List<FileEntry> Children { get; set; } = new();
    public int Depth { get; set; }
    public int SubtreeItemCount { get; set; }
    public int SubtreeFileCount { get; set; }
    public int SubtreeFolderCount { get; set; }

    public void RecalculateSize()
    {
        if (IsDirectory)
        {
            Size = Children.Sum(c => c.Size);
            AllocatedSize = Children.Sum(c => c.HasAllocatedSize ? c.AllocatedSize : c.Size);
            HasAllocatedSize = true;
            SubtreeFileCount = Children.Sum(c => c.SubtreeFileCount > 0 ? c.SubtreeFileCount : c.IsDirectory ? 0 : 1);
            SubtreeFolderCount = 1 + Children.Sum(c => c.SubtreeFolderCount > 0 ? c.SubtreeFolderCount : c.IsDirectory ? 1 : 0);
            SubtreeItemCount = SubtreeFileCount + SubtreeFolderCount;
        }
        else
        {
            SubtreeFileCount = 1;
            SubtreeFolderCount = 0;
            SubtreeItemCount = 1;
            if (AllocatedSize == 0 && Size > 0)
            {
                AllocatedSize = Size;
                HasAllocatedSize = true;
            }
        }

        Parent?.RecalculateSize();
    }

    public void RemoveChild(FileEntry child)
    {
        Children.Remove(child);
        RecalculateSize();
    }
}

