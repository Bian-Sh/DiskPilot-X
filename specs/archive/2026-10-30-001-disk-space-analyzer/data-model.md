# Data Model: Disk Space Analyzer

**Branch**: `001-disk-space-analyzer` | **Date**: 2026-02-25

## Entity Definitions

### FileEntry

Represents a file or folder discovered during scanning. Forms a tree structure via parent-child relationships.

| Field | Type | Description | Constraints |
|---|---|---|---|
| Path | `string` | Full absolute path | Required; unique within scan |
| Name | `string` | File or folder name | Required |
| Size | `long` | Size in bytes (folders: sum of children) | >= 0 |
| Extension | `string?` | File extension including dot (null for folders) | Lowercase; e.g., `.txt` |
| LastModified | `DateTime` | Last write time | Required |
| IsDirectory | `bool` | True if folder | Required |
| IsReparsePoint | `bool` | True if symbolic link or junction point | Default false |
| IsAccessDenied | `bool` | True if could not be enumerated | Default false |
| ContentHash | `byte[]?` | xxHash3 hash (computed only for duplicate candidates) | Null until computed |
| Parent | `FileEntry?` | Parent folder reference | Null only for root |
| Children | `List<FileEntry>` | Child entries (empty for files) | Non-null; empty for files |
| Depth | `int` | Nesting depth from scan root (root = 0) | >= 0 |

**Relationships**: Tree structure via `Parent`/`Children`. One `ScanSession` has one root `FileEntry`.

**Validation Rules**:
- `Path` must be an absolute Windows path (starts with drive letter or UNC)
- `Size` for directories must equal sum of direct children sizes
- `Children` must be empty when `IsDirectory` is false
- `IsReparsePoint` entries are displayed but excluded from cleanup recommendations for link targets

**State**: Immutable after scan completes. Entries are removed from the tree after successful cleanup (FR-022), which triggers size recalculation up the parent chain.

---

### ScanSession

Represents a single scan operation. Ephemeral — exists only in memory for the current session.

| Field | Type | Description | Constraints |
|---|---|---|---|
| Id | `Guid` | Unique session identifier | Auto-generated |
| TargetPath | `string` | User-selected scan root path | Required; valid path |
| StartTime | `DateTime` | When scan began | Required |
| EndTime | `DateTime?` | When scan completed | Null while in progress |
| TotalFiles | `int` | Count of files discovered | >= 0 |
| TotalFolders | `int` | Count of folders discovered | >= 0 |
| TotalSize | `long` | Sum of all file sizes in bytes | >= 0 |
| RootEntry | `FileEntry?` | Root of the file tree | Null until first entry scanned |
| IsCancelled | `bool` | True if user cancelled the scan | Default false |
| DriveCapacity | `long?` | Total drive capacity in bytes | Null for non-root folder scans |
| DriveFreeSpace | `long?` | Free space on drive in bytes | Null for non-root folder scans |

**Relationships**: Contains one root `FileEntry` which parents the entire tree.

**State Transitions**:
```
Created → Scanning (scan started)
Scanning → Completed (scan finished)
Scanning → Cancelled (user cancelled; partial results available)
```

---

### TreemapNode

Visual representation of a `FileEntry` in the treemap. Computed by the layout engine — not persisted.

| Field | Type | Description | Constraints |
|---|---|---|---|
| Entry | `FileEntry` | Source data entry | Required |
| X | `float` | Left edge position in pixels | >= 0 |
| Y | `float` | Top edge position in pixels | >= 0 |
| Width | `float` | Rectangle width in pixels | > 0 |
| Height | `float` | Rectangle height in pixels | > 0 |
| ColorHex | `string` | Fill color as hex string (e.g., `#2196F3`); App layer converts to `SKColor` for rendering | Required |
| Depth | `int` | Nesting depth for rendering order | >= 0 |
| IsVisible | `bool` | Whether rectangle is large enough to render | Default true |
| Label | `string?` | Displayed text (name + size) | Null if too small |

**Relationships**: One-to-one with `FileEntry`. Recomputed when the user drills into/out of folders or after cleanup.

**Visibility Threshold**: Rectangles smaller than 3×3 pixels are marked `IsVisible = false` but still participate in hit-testing for tooltips.

---

### CleanupRecommendation

An AI-generated suggestion to remove a file or folder.

| Field | Type | Description | Constraints |
|---|---|---|---|
| Id | `string` | Unique recommendation ID (e.g., `REC-001`) | Required; sequential |
| TargetPath | `string` | Path of the file/folder to remove | Required |
| Entry | `FileEntry` | Reference to the scanned entry | Required |
| Size | `long` | Size in bytes | >= 0 |
| Category | `RecommendationCategory` | Cleanup category | Required |
| SafetyRating | `SafetyRating` | Risk level | Required |
| Explanation | `string` | Human-readable reason for recommendation | Required; non-empty |
| IsAccepted | `bool` | User has accepted this recommendation | Default false |
| IsDismissed | `bool` | User has explicitly dismissed it | Default false |

**Enums**:

`RecommendationCategory`:
- `TemporaryFiles` — OS and app temp directories
- `BuildCache` — Compiler/build output (obj, bin, .gradle, etc.)
- `PackageManagerCache` — npm, NuGet, pip, Maven caches
- `OldDownloads` — Aged files in Downloads folder
- `LogFiles` — Application and system logs
- `DuplicateFiles` — Hash-confirmed duplicate copies
- `BrowserCache` — Browser data caches
- `SystemCache` — Windows update cache, prefetch, etc.
- `Other` — Miscellaneous safe-to-remove items

`SafetyRating`:
- `Safe` — No risk of data loss; can always be regenerated
- `ReviewFirst` — Low risk but user should verify before removing
- `Caution` — Files may be wanted; candidates based on age/size patterns

**Validation Rules**:
- `IsAccepted` and `IsDismissed` cannot both be true
- `Explanation` must be at least one complete sentence
- `Entry` must reference an existing `FileEntry` in the current scan tree

**State Transitions**:
```
Pending → Accepted    (user accepts)
Pending → Dismissed   (user dismisses)
Accepted → Pending    (user unchecks)
Dismissed → Pending   (user re-enables)
Any → [discarded]     (user re-runs AI analysis; all recommendations replaced per FR-029)
```

---

### CleanupAction

A record of an executed deletion operation.

| Field | Type | Description | Constraints |
|---|---|---|---|
| Id | `Guid` | Unique action identifier | Auto-generated |
| Recommendation | `CleanupRecommendation` | Source recommendation | Required |
| ActionType | `DeletionMode` | Permanent delete or recycle bin | Required |
| Result | `CleanupResult` | Outcome of the action | Required |
| FailureReason | `string?` | Error message if failed or skipped | Null if successful |
| Timestamp | `DateTime` | When action was executed | Required |
| ActualSizeFreed | `long` | Actual bytes freed (may differ from estimate) | >= 0 |

**Enums**:

`DeletionMode`:
- `PermanentDelete` — File permanently removed from disk
- `MoveToRecycleBin` — File moved to Windows Recycle Bin

`CleanupResult`:
- `Success` — Item removed successfully
- `Failed` — Deletion threw an error (logged, continued)
- `Skipped` — File locked or permissions denied
- `AlreadyRemoved` — File was deleted by another process before cleanup reached it

---

### ChatMessage

A single message in the chat conversation. Ephemeral — exists only in memory.

| Field | Type | Description | Constraints |
|---|---|---|---|
| Id | `Guid` | Unique message identifier | Auto-generated |
| Sender | `ChatSender` | Who sent the message | Required |
| Text | `string` | Message content (may contain markdown) | Required; non-empty |
| Timestamp | `DateTime` | When message was sent/received | Required |
| LinkedEntry | `FileEntry?` | Treemap item clicked before asking | Null if no context click |
| LinkedRecommendation | `CleanupRecommendation?` | Recommendation item clicked | Null if no context click |
| IsError | `bool` | True if message represents an error state | Default false |

**Enums**:

`ChatSender`:
- `User` — Message from the user
- `Assistant` — Response from the AI

---

### AppSettings

Application configuration persisted to disk. API key is encrypted.

| Field | Type | Description | Constraints |
|---|---|---|---|
| EncryptedApiKey | `byte[]?` | Anthropic API key encrypted via DPAPI | Null until configured |
| IsApiKeyValid | `bool` | Result of last key validation | Default false |
| DeletionMode | `DeletionMode` | User's default deletion preference | Default `MoveToRecycleBin` |
| LastScanPath | `string?` | Last scanned path (convenience) | Null initially |

**Storage Location**: `%APPDATA%\SpaceMonger\settings.dat`

**Encryption**: `EncryptedApiKey` encrypted with `ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser)`. Decrypted on read with `ProtectedData.Unprotect`.

---

## Relationship Diagram

```
ScanSession 1 ──────── 1 FileEntry (root)
                             │
                             ├──── * FileEntry (children, recursive tree)
                             │
                             └──── * CleanupRecommendation
                                        │
                                        └──── 0..1 CleanupAction

FileEntry 1 ──────── 1 TreemapNode (computed, not persisted)

ChatMessage 0..1 ──── FileEntry (linked context)
ChatMessage 0..1 ──── CleanupRecommendation (linked context)

AppSettings (singleton, persisted to disk)
```

## Aggregation Rules

- **Folder size**: Sum of all descendant file sizes (computed bottom-up after scan)
- **Recoverable space total**: Sum of `Size` for all recommendations where `IsAccepted == true`
- **Category totals**: Sum of `Size` grouped by `Category` for accepted recommendations
- **Cleanup summary**: Count and sum grouped by `CleanupResult` across all `CleanupAction` records
