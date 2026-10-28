namespace DiskPilotX.Core.Models;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiskPilotX.Core.Enums;

/// <summary>
/// Represents a cleanup recommendation for a scanned file or folder.
/// </summary>
public class CleanupRecommendation : INotifyPropertyChanged
{
    /// <summary>
    /// Gets or sets the display recommendation identifier (e.g., "1").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file system path of the file or folder to remove.
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the reference to the scanned file or folder entry.
    /// </summary>
    public FileEntry? Entry { get; set; }

    // 批准时的完整目标签名，仅供清理执行前校验。
    internal string? ExpectedCleanupFingerprint { get; set; }

    /// <summary>
    /// Gets or sets the size in bytes of the target.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the cleanup category for this recommendation.
    /// </summary>
    public RecommendationCategory Category { get; set; }

    /// <summary>
    /// Gets or sets the safety rating indicating the risk level of cleanup.
    /// </summary>
    public SafetyRating SafetyRating { get; set; }

    /// <summary>
    /// Gets or sets the human-readable explanation for this recommendation.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;

    public DateTime? LastModified { get; set; }

    /// <summary>
    /// 条目来自哪个分析范围（多范围无头分析时填根目录名）。
    /// 同一份清单里可能混合多个游戏/目录的候选，界面据此标注来源。
    /// </summary>
    public string SourceScope { get; set; } = string.Empty;

    /// <summary>分组表头文字：仅同一来源范围的第一条会带值，由宿主在重建列表时写入。</summary>
    public string ScopeHeader { get; set; } = string.Empty;

    public bool? UnityHubListed { get; set; }

    public string UnityMetadataText
    {
        get
        {
            var parts = new List<string>();
            if (LastModified is { } lastModified)
            {
                parts.Add($"Last modified: {lastModified:yyyy-MM-dd HH:mm}");
            }

            if (UnityHubListed is { } hubListed)
            {
                parts.Add("Unity Hub listed: " + (hubListed ? "yes" : "no"));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user has accepted this recommendation.
    /// When set to true, IsDismissed is automatically set to false.
    /// </summary>
    private bool _isAccepted;
    public bool IsAccepted
    {
        get => _isAccepted;
        set
        {
            if (_isAccepted == value)
            {
                return;
            }

            _isAccepted = value;
            OnPropertyChanged();
            if (value && _isDismissed)
            {
                _isDismissed = false;
                OnPropertyChanged(nameof(IsDismissed));
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user has dismissed this recommendation.
    /// When set to true, IsAccepted is automatically set to false.
    /// </summary>
    private bool _isDismissed;
    public bool IsDismissed
    {
        get => _isDismissed;
        set
        {
            if (_isDismissed == value)
            {
                return;
            }

            _isDismissed = value;
            OnPropertyChanged();
            if (value && _isAccepted)
            {
                _isAccepted = false;
                OnPropertyChanged(nameof(IsAccepted));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
