# Volume Snapshot Architecture

> Status: Architecture proposal / implementation contract  
> Scope: DiskPilot-X core infrastructure  
> Target document: docs/architecture/volume-snapshot.md  
> Implementation: Not included in this change. Implementation is intentionally deferred to a separately planned Codex task.

## 1. Purpose and architectural position

Volume Snapshot is a foundational DiskPilot-X infrastructure service. It provides a volume-wide, in-memory index of filesystem objects and a consistent query surface for all higher-level features.

It is not a Unity scanner, project detector, cleanup policy engine, or application-specific content classifier. Domain-specific discovery belongs in Skills or other higher-level modules that consume the generic filesystem API.

The infrastructure owns five responsibilities:

1. Build a volume index from the filesystem's native metadata source.
2. Keep that index synchronized with authoritative filesystem change records.
3. Expose a stable Virtual File System (VFS) query API.
4. Manage memory residency and index lifetime across multiple volumes.
5. Publish index-change notifications so UI and other consumers refresh from one consistent source.

The index is process-local and temporary. It is never persisted to SQLite, a custom index file, or another disk-backed database. When DiskPilot-X exits, all Volume Snapshot data is discarded.

### 1.1 Non-goals

- No Unity-specific logic in Core or the host application.
- No permanent or cross-process index persistence.
- No SQLite or other secondary on-disk index.
- No OS-mounted virtual filesystem.
- No independent filesystem event processing in Treemap or individual Skills.
- No requirement to keep every local volume indexed simultaneously.
- No implementation of the feature in this architecture-document change.

## 2. Confirmed design decisions

| Area | Decision |
|---|---|
| Architectural layer | DiskPilot-X Core infrastructure |
| Index granularity | One independent index per volume |
| Loading | Lazy; load only when a feature first needs that volume |
| Initial NTFS topology source | Full-volume MFT enumeration |
| Change authority on NTFS | USN Journal |
| FileSystemWatcher | Optional wake-up hint only; never an index mutation source |
| Index storage | Compact in-memory data structures |
| Disk persistence | None |
| Query surface | Virtual File System |
| Memory management | One global memory budget shared by all volume indexes |
| Cache eviction | LRU, subject to active-use protection |
| Manual unload | Required |
| Automatic unload | Required |
| Active-use protection | Reference-counted lease |
| Synchronization | Before a query when necessary, plus background sync triggered by hints |
| Domain discovery | Skills / higher-level consumers, not host hard-coding |
| Treemap | Consumer of VFS projections and change notifications; not an event-source peer |

## 3. Existing implementation audit

This section records the repository state observed while preparing this document. It is not a claim that every code path in the repository has been exhaustively audited. File paths and behavior should be rechecked by the implementation task before changing them.

### 3.1 MFT enumeration

File: [MftEnumerator.cs](../../src/DiskPilotX.Core/Services/Scanning/MftEnumerator.cs)

Current behavior:

- Uses FSCTL_ENUM_USN_DATA to enumerate MFT/USN records for the supplied volume root.
- Reads USN_RECORD_V2-shaped records and currently accepts major version 2 only.
- Returns Dictionary<long, MftRecord> keyed by File Reference Number (FRN).
- Each dictionary value stores one parent FRN, one filename, attributes, and directory status.
- The dictionary assignment by FRN means multiple directory entries that refer to the same file reference can overwrite one another. This is not sufficient for a general hard-link-aware volume index.
- The current parser skips alternate data stream names containing a colon and filters selected NTFS metadata file segments.

The new index must not use a single FRN-to-one-name mapping as its directory topology model.

### 3.2 Current MFT scan path

File: [IncrementalFileScanner.Mft.cs](../../src/DiskPilotX.Core/Services/Scanning/IncrementalFileScanner.Mft.cs)

Current behavior:

1. Calls the full-volume MFT enumerator.
2. For a subdirectory scan, builds a Parent FRN → child FRN lookup and walks the target subtree.
3. Creates a FileEntry object for each selected record.
4. Links FileEntry.Parent and FileEntry.Children.
5. Reconstructs and stores full paths on entries.
6. Runs whitelist pruning.
7. Drops the temporary MFT dictionary.
8. Calls ParallelSizeCollector.CollectSizes to obtain size information.
9. Returns a ScanSession containing the materialized tree.

Consequences:

- A scan of a different directory on the same volume can enumerate the entire MFT again.
- The MFT result is a temporary intermediate representation, not a reusable volume index.
- The materialized FileEntry tree is scope-specific and object-heavy.
- MFT topology alone is not sufficient for Treemap size reporting. The existing size-collection phase obtains additional filesystem metadata and must be accounted for in the new builder.

### 3.3 Current incremental scan state

Files:

- [IncrementalFileScanner.cs](../../src/DiskPilotX.Core/Services/Scanning/IncrementalFileScanner.cs)
- [IncrementalFileScanner.Changes.cs](../../src/DiskPilotX.Core/Services/Scanning/IncrementalFileScanner.Changes.cs)
- [UsnJournalReader.cs](../../src/DiskPilotX.Core/Services/Scanning/UsnJournalReader.cs)

Current behavior:

- IncrementalFileScanner maintains a Dictionary<string, VolumeScanState>.
- VolumeScanState includes ScannedPath, CachedSession, Watermark, FrnIndex, VolumeKey, and whitelist/rules information.
- The cached session represents a scanned scope, not a canonical volume-wide index.
- SaveMftState clears _volumeStates before storing a new state. Its comment explicitly describes retaining only one volume index to avoid accumulating complete file trees.
- Incremental changes are applied to a FileEntry tree. The current applier handles delete, rename, create, and modify categories, with conservative failure paths that trigger a full scan.
- The current change reader deduplicates records by FRN and collapses multiple records into a net effect. This needs redesign for a volume-wide index, especially for multiple directory entries/hard links and rename pairing.
- The current watermark model captures Journal ID, NextUsn, and volume root. The new consistency validator also needs the journal's LowestValidUsn and other identity/capability information needed to prove continuity.
- The current reader parses USN_RECORD_V2 only. Unsupported record versions must never be silently treated as successfully synchronized.

The existing reader, native interop, watermark logic, and conservative fallback behavior are candidates for extraction/reuse. The existing VolumeScanState and FileEntry-tree mutation model are not the target architecture.

### 3.4 Existing ScanSnapshotCache is a different cache

File: [ScanSnapshotCache.cs](../../src/DiskPilotX.Core/Services/Scanning/ScanSnapshotCache.cs)

ScanSnapshotCache stores completed ScanSession instances. It has entry-count and snapshot-count limits, LRU-like usage tracking, and named-owner protection.

It is useful for retaining prior scan results and cleanup/analysis references, but it is not a canonical volume index and must not be repurposed or renamed as Volume Snapshot.

The new VolumeSnapshotCache has a different responsibility: managing the RAM residency of one canonical index per volume, under a global memory budget, with active leases preventing unload.

### 3.5 Treemap and scan-result flow

Files:

- [MainViewModel.cs](../../src/DiskPilotX.App/ViewModels/MainViewModel.cs)
- [TreemapViewModel.cs](../../src/DiskPilotX.App/ViewModels/TreemapViewModel.cs)
- [TreemapView.xaml.cs](../../src/DiskPilotX.App/Views/TreemapView.xaml.cs)
- [MainWindow.xaml.cs](../../src/DiskPilotX.App/MainWindow.xaml.cs)

Observed flow:

- MainViewModel.ScanAsync calls IFileScanner.ScanAsync.
- On successful completion it sets CurrentSession, updates the status bar, and raises ScanCompleted.
- TreemapViewModel.SetRoot accepts a FileEntry root and optional ScanSession, then computes Treemap layout from that tree.
- TreemapView observes TreemapViewModel.PropertyChanged and updates the rendered Treemap when Nodes or navigation properties change.
- TreemapView exposes a DeleteEntryAsync delegate for its context-menu delete action. The exact delegate wiring and the complete delete execution path were not established from the inspected files and must be traced before implementation.
- TreemapViewModel.ApplyCleanupResults calls DeletedEntryState.Apply and raises DeletionRevision; TreemapView responds by refreshing deletion marks. This is cleanup-result visualization, not proof of an OS-level filesystem watcher event.
- MainWindow currently owns a ScanSnapshotCache for ScanSession retention.

A repository-wide, conclusive FileSystemWatcher call graph was not established during this audit. Do not assume either that a watcher exists or that it does not. The implementation task must search all projects and trace any watcher, delete callback, cleanup execution, and Treemap refresh paths before removing or redirecting event wiring.

### 3.6 Title bar location

Files:

- [WindowTitleBar.xaml](../../src/DiskPilotX.App/Controls/WindowTitleBar.xaml)
- [WindowTitleBar.xaml.cs](../../src/DiskPilotX.App/Controls/WindowTitleBar.xaml.cs)

The title bar has a right-side horizontal StackPanel containing the update entry, settings, chat-collapse, and window controls. The Snapshot Manager entry belongs in this title-bar action area, immediately to the left of the update entry. It must not be placed in the Treemap toolbar. The shared horizontal layout should naturally shift the update entry right without overlapping or interfering with it.

## 4. High-level architecture

The following components are logical responsibilities. They do not prescribe that every component must be a separate class in the first implementation.

### 4.1 VolumeSnapshotManager

The public orchestration boundary.

Responsibilities:

- Resolve a path to a stable volume identity.
- Coalesce concurrent requests to load the same volume.
- Return a VolumeSnapshotLease for a ready index.
- Ensure the index is synchronized to an appropriate USN boundary before serving a freshness-required query.
- Expose volume status and metrics.
- Coordinate manual unload and automatic eviction.
- Publish lifecycle and index-change notifications.

It must not expose mutable internal dictionaries or allow consumers to mutate index records directly.

### 4.2 VolumeSnapshotBuilder

Builds a complete volume index on first demand.

Responsibilities:

- Open and validate the target volume.
- Capture the starting USN Journal identity and watermark before MFT enumeration.
- Enumerate the volume's MFT once.
- Convert raw records into compact file-node and directory-entry indexes.
- Collect metadata required by Treemap (logical size, allocated size where available, attributes, timestamps, and other supported properties).
- Build directory aggregate values.
- Replay USN changes that occurred after the captured starting watermark.
- Validate continuity and only publish the snapshot as Ready after successful replay.

A partially built index is private to the builder and must not be visible to consumers.

### 4.3 UsnSynchronizer

The sole writer for NTFS index mutations after initial build.

Responsibilities:

- Query current Journal ID, LowestValidUsn, and NextUsn.
- Read changes from the snapshot's last committed USN through a captured target watermark.
- Validate journal continuity and volume identity.
- Normalize raw records into ordered, explicit change operations.
- Apply all operations atomically from the perspective of readers.
- Advance the committed watermark only after the entire batch succeeds.
- Mark the snapshot invalid and request rebuild when a gap or unsupported operation prevents a provably correct update.

No other service may directly modify the canonical NTFS index.

### 4.4 VirtualFileSystem

The generic read-only query facade for consumers.

Example responsibilities:

- Resolve a path to a volume and directory entry.
- Enumerate direct children.
- Query node metadata.
- Query directory aggregate sizes and counts.
- Project a requested subtree for existing UI consumers.
- Provide stable identifiers and volume identity.
- Offer explicit freshness requirements (for example, RequireSynchronized versus AllowLastCommitted).

The VFS is an application abstraction. It is not a Windows filesystem driver and does not mount a virtual drive.

### 4.5 VolumeSnapshotCache and MemoryLifecycleManager

Own memory residency across all volumes.

Responsibilities:

- Maintain one global RAM budget.
- Track per-volume estimated resident memory.
- Track last access for LRU decisions.
- Prevent eviction while a volume has active leases.
- Perform automatic eviction when memory pressure reaches the configured threshold.
- Support explicit user-requested unload.
- Reject or defer a load if the budget cannot be satisfied safely.
- Report estimated memory separately from disk capacity and volume-used space.

There is no disk-backed index store.

## 5. Data model

The data model separates a filesystem object from its directory entry. This is required for hard links and avoids assuming that one FRN maps to exactly one path.

### 5.1 Volume identity

A drive letter is not a stable identity. The key must identify the mounted volume, not merely its current display name.

The implementation should use a normalized volume identity obtained from Windows volume APIs (for example, volume GUID/name and filesystem identity where available). Drive letters and mount points are presentation aliases and may change.

A snapshot must be invalidated/recreated if the underlying volume identity changes. Removable media removal, reinserted media, and mount-point reassignment must not cause an old index to be attached to a different volume.

### 5.2 FileNode

Represents a filesystem object identified by its full file reference.

Suggested logical fields:

| Field | Meaning |
|---|---|
| FileReference | Full FRN, including sequence information |
| Attributes | NTFS/file attributes |
| IsDirectory | Object type |
| LogicalSize | Logical file length where applicable |
| AllocatedSize | Allocated bytes when available; validity tracked separately |
| LastWriteTime | Last-write metadata where available |
| MetadataFlags | Compact flags for known/unknown/unavailable fields |
| LinkCount / LinkMetadata | Only if reliably obtainable; otherwise omit |

Do not truncate the full FRN to its segment number for identity comparisons. The sequence component helps distinguish reused MFT slots.

### 5.3 DirectoryEntry

Represents a name in a parent directory.

Suggested logical fields:

| Field | Meaning |
|---|---|
| ParentDirectoryReference | Parent directory FRN |
| ChildFileReference | Referenced FileNode FRN |
| NameId / NameOffset | Compact filename storage reference |
| EntryFlags | Entry-specific flags if required |

The logical identity of a directory entry is its parent + name + child reference, not only the child FRN.

Multiple DirectoryEntry records may point to one FileNode. This supports hard links and prevents the current Dictionary<long, MftRecord> overwrite behavior.

### 5.4 Compact in-memory representation

Do not create a large object graph with one FileEntry, one List, and multiple strings/dictionaries per filesystem item.

Preferred direction:

- Dense or chunked arrays of value-type records for nodes and directory entries.
- Integer indexes/handles between records rather than repeated object references.
- A dedicated FRN-to-node lookup index.
- A parent-directory-to-child-range or parent-to-entry-list index.
- Centralized filename storage (string pool or compact name arena).
- Dedicated directory aggregate records.
- Full paths resolved on demand from parent relationships and names.
- Avoid storing a full absolute path string on every entry.
- Avoid allocating one List<T> per directory when a compact adjacency representation is practical.

The exact collection types should be chosen after benchmarks against realistic volumes. The architecture requirement is compactness and predictable memory accounting, not a premature commitment to a specific hash-table implementation.

### 5.5 Directory aggregates

Treemap needs directory-level values without recursively recounting the entire subtree on every query.

Maintain aggregates such as:

- Logical bytes.
- Allocated bytes (with completeness/unknown state).
- File count.
- Directory count.
- Total entry count.

When a file changes size, propagate the delta through its ancestor directories. When an entry is created, deleted, or moved, update affected parent chains. For hard links, the design must define whether Treemap reports per-directory-entry logical size (usual tree semantics) or unique physical object size. The default should be per-entry logical size for directory visualization, while avoiding double-counting claims about unique physical disk usage.

Directory aggregate updates must be transactionally consistent with the underlying entry mutation.

### 5.6 Metadata collection limitation

MFT enumeration supplies topology and selected metadata but the current MFT record parser does not provide all values required by Treemap, especially file sizes. The current scanner performs a separate size-collection phase.

The new builder must explicitly retain a metadata collection phase or use a more efficient metadata query mechanism. It must not assume that MFT enumeration alone provides exact logical size, allocated size, or all timestamps.

Unknown or inaccessible values must be represented as unknown, not silently converted to zero. Cloud placeholders, reparse points, access-denied entries, and files that disappear during initial indexing need explicit policies.

## 6. Lazy loading and initial build protocol

A volume index is created only when a consumer requests data from that volume.

Opening the application must not enumerate all local disks.

### 6.1 First access

1. Resolve the requested path to its volume identity.
2. Check whether a Ready index already exists.
3. If one exists, acquire a lease and run the freshness policy.
4. If it is absent, reserve memory budget and create a per-volume loading operation.
5. Coalesce concurrent requests for the same volume onto that one loading operation.
6. Capture the USN Journal ID, LowestValidUsn, and starting NextUsn before MFT enumeration.
7. Enumerate the complete volume MFT once.
8. Build the compact index and collect required metadata.
9. Replay all USN changes after the captured starting watermark through a well-defined target watermark.
10. Re-query and validate Journal ID and LowestValidUsn; ensure no required record range was lost.
11. Publish the fully built index atomically and mark it Ready.
12. Return leases to waiting callers.

If any stage fails, discard the unpublished partial index. Do not expose a partially built or unverified index.

### 6.2 Full-volume scope

The first successful load creates a complete volume index, even if the initial consumer asked for a subdirectory.

This is intentional: all later directory queries on the same volume must be projections from the same canonical index and must not repeat full-volume MFT enumeration.

### 6.3 Non-NTFS volumes

MFT and USN are NTFS-specific. The manager must detect filesystem capability before selecting the NTFS provider.

For non-NTFS volumes, use a separately defined traversal provider or report the capability limitation. Do not fabricate USN watermarks or claim USN-level consistency.

The provider abstraction should allow other filesystems in the future without weakening the NTFS consistency contract.

## 7. USN consistency protocol

USN is the authoritative source of NTFS changes after the initial MFT baseline. FileSystemWatcher notifications are not authoritative records.

### 7.1 Initial MFT baseline

The key race is a file changing while MFT enumeration is running.

The builder must capture the journal identity and starting USN before starting MFT enumeration. After the MFT baseline is built, it must replay changes from that starting USN. This includes changes that occurred while the MFT was being read.

The snapshot is not Ready until replay completes and continuity is proven.

### 7.2 Incremental synchronization

For each synchronization batch:

1. Query the current journal metadata and capture a target NextUsn.
2. Verify that Journal ID matches the snapshot.
3. Verify that the committed NextUsn is not older than LowestValidUsn.
4. Read the journal range from the committed watermark up to the captured target.
5. Parse every supported record and normalize it into explicit operations.
6. Apply the batch under the index's exclusive mutation boundary.
7. Revalidate the journal identity/range as required by the reader contract.
8. Commit the target watermark only after all operations succeed.
9. Publish one generation/change notification after the batch commits.

If the journal advances while reading, records beyond the captured target may be processed only if the committed watermark accurately reflects the actual consumed range. The reader must not skip records by advancing to a queried NextUsn that was not fully consumed. Replaying already-consumed records is preferable to skipping unconsumed records, provided operations are idempotent or the batch is deduplicated safely.

### 7.3 Conditions that invalidate the index

Mark the snapshot Invalid (or require a full rebuild) when any of the following occurs:

- Journal ID changes or the journal is deleted/recreated.
- The committed USN is below LowestValidUsn.
- A required USN range has been deleted or cannot be read.
- A record version or change type required for correctness is unsupported.
- A structural change cannot be mapped unambiguously to the index.
- Volume identity changes.
- An index invariant fails (missing parent, conflicting object identity, impossible entry relation).
- A batch is only partially applied and cannot be rolled back safely.

Do not silently skip unknown structural records and still mark the snapshot Ready.

### 7.4 USN record normalization

The current reader groups raw records by FRN and classifies each group into one net operation. That is not sufficient as the general volume-index contract.

The new normalizer must preserve enough information to correctly handle:

- Create and delete.
- Rename old-name/new-name pairing.
- Modify and close-related metadata changes.
- Directory moves.
- Hard-link create/delete and link-count changes.
- Reparse-point changes.
- Supported record versions and full file-reference identity.
- Multiple operations for one file within one synchronization window.

If an operation cannot be represented safely, invalidate and rebuild rather than guessing.

### 7.5 Atomic visibility

Consumers must never observe half of a synchronization batch.

Use a per-volume mutation gate and a reader consistency strategy (for example, read/write locking or immutable generation publication). A query sees one committed generation. The implementation must not hold a global lock across unrelated volumes.

## 8. Change-source policy and Treemap integration

### 8.1 NTFS source of truth

For NTFS, USN Journal is the only authoritative source used to mutate Volume Snapshot.

- MFT provides the initial baseline.
- USN Journal provides subsequent changes.
- FileSystemWatcher, if present, may only signal that synchronization should run sooner.
- FileSystemWatcher must not create, delete, rename, or modify index entries directly.
- A watcher event is never proof that a specific change was applied.
- Missed watcher events do not compromise correctness because the next required synchronization reads USN from the committed watermark.

This avoids duplicate mutation paths and noisy event races.

### 8.2 Treemap responsibilities

Treemap must become a consumer of VFS data, not an independent filesystem observer.

Target flow:

1. A workspace requests a volume/subtree view from VFS.
2. The request acquires or uses a volume lease.
3. VFS returns a stable subtree projection or VFS-backed read model.
4. Treemap computes/render layouts from that model.
5. After a committed USN generation changes, VFS publishes a volume-change notification.
6. The active workspace refreshes or incrementally updates from the committed generation.
7. Treemap does not separately process FileSystemWatcher or raw USN records.

The initial integration may adapt VFS data into the existing FileEntry/ScanSession model. However, this projection must be treated as a compatibility adapter, not the canonical index. Measure its memory cost; avoid retaining multiple full-volume FileEntry copies.

Longer term, Treemap should consume a lightweight VFS-backed view model or stable entry handles.

### 8.3 Lease lifetime for UI consumers

A query lease cannot be released immediately after building the Treemap if the Treemap continues to hold references into the volume index.

The active workspace must own a lease for as long as it depends on the index. Release it when the workspace changes to another volume, the view is disposed, or the relevant analysis task ends.

If the compatibility adapter fully copies a subtree and no longer references the volume index, the implementation may release the source lease after the copy completes. That choice must be explicit and reflected in memory metrics.

### 8.4 Deletion and cleanup semantics

Distinguish three different concepts:

- A real filesystem delete: becomes visible to the NTFS index through USN.
- A cleanup recommendation/preview: may mark an item visually but does not mean the filesystem object has been deleted.
- A cleanup action that actually deletes a file: USN is still the authority for the index mutation.

The current DeletedEntryState.Apply / DeletionRevision path is a cleanup-result visualization mechanism. It must not be mistaken for the future USN synchronization pipeline.

After migration, direct delete callbacks may still execute the requested filesystem operation, but they must not independently mutate the canonical Volume Snapshot or Treemap's authoritative data. The UI should wait for or request USN synchronization, then refresh from VFS.

## 9. Lease and reference-counting model

Leases are mandatory. LRU recency alone is not sufficient to protect active readers.

### 9.1 Contract

- A consumer acquires a lease before accessing a loaded volume index.
- The lease increments that volume's active lease count.
- The consumer releases the lease when it no longer references the index.
- A volume with one or more active leases cannot be unloaded or evicted.
- A volume with zero active leases is eligible for automatic LRU eviction.
- Manual unload must never invalidate an active lease or free memory underneath a reader.

### 9.2 API shape

The public API should conceptually resemble:

- AcquireAsync(pathOrVolume, options, cancellationToken) returns VolumeSnapshotLease.
- The lease exposes read-only snapshot/VFS access.
- Dispose or DisposeAsync releases exactly one lease.
- RequestUnloadAsync(volumeId, policy, cancellationToken) requests safe unload.

The lease should be an idempotently disposable reference object. Double disposal must not decrement the count twice. The manager must guard against underflow and leaked lease accounting.

Do not return naked mutable index references that can outlive the lease.

### 9.3 Manual unload behavior

When the user requests unload:

- If active lease count is zero, unload can proceed.
- If active lease count is nonzero, show the active-use state and either refuse/defer or offer to wait for release.
- Do not forcibly revoke leases.
- Do not silently detach the index while Treemap or a background analysis still uses it.
- If waiting is supported, it must be cancellable and must not block the UI thread.

### 9.4 Automatic eviction

LRU means Least Recently Used: among eligible cached volumes, evict the one whose last successful access is oldest.

Only volumes with zero active leases are candidates. A background sync or build operation also counts as active use and blocks eviction.

The eviction algorithm must use a monotonic access sequence or timestamp and update it on meaningful access. It should not use filesystem timestamps.

When no eligible volume can be evicted, the manager must reject/defer the new load or ask the user to release active work. It must not exceed the configured budget without an explicit, bounded temporary reservation policy.

## 10. Memory budget and metrics

### 10.1 Global budget

One configurable global memory budget governs all resident volume indexes. The budget is based on estimated resident index memory, not the number of volumes.

The policy should support:

- A sensible default.
- A user-configurable limit.
- Automatic eviction at a high-water threshold.
- A lower target after eviction to avoid repeated churn.
- A clear error when active leases prevent enough memory from being reclaimed.

A simple maximum-three-disks policy is not acceptable because volumes vary greatly in record count and index size.

### 10.2 Accounting

Track per-volume memory estimates by owned allocations/capacities where possible:

- Node record storage.
- Directory-entry storage.
- FRN lookup table capacity.
- Parent/child index capacity.
- Name storage capacity.
- Aggregate storage.
- Temporary build/synchronization buffers.
- Compatibility projections, if retained.

Do not report SQLite size (there is no SQLite index), disk capacity, volume-used bytes, or a proportional share of process Working Set as index RAM.

If exact managed/native memory attribution is unavailable, label the figure as an estimate and document the estimation method. Keep separate metrics for:

- Volume capacity and used space.
- Index record count.
- Estimated resident index memory.
- Temporary build memory.
- Total Volume Snapshot memory budget usage.

### 10.3 Build reservations

A volume load can have a large temporary peak because raw MFT parsing, compact index construction, metadata collection, and USN replay may overlap.

The memory manager must reserve estimated build memory before loading and account for temporary buffers. It should avoid simultaneously retaining both a full raw MFT dictionary and the final index longer than necessary.

If the reservation cannot be made, defer/reject the load rather than allowing uncontrolled memory growth.

## 11. Lifecycle state machine

Recommended externally observable states:

| State | Meaning |
|---|---|
| Unloaded | No resident index; a later request may start a new full build |
| Loading | Initial MFT/index build in progress |
| Ready | Index is published and its committed USN watermark is valid |
| Syncing | An incremental USN batch is being applied |
| Stale | Index exists but cannot currently be claimed fresh; sync or validation is required |
| Invalid | Continuity or index invariants failed; full rebuild required |
| Unloading | Safe teardown is in progress after active use reaches zero |
| Faulted | A non-recoverable build/runtime failure occurred; retry policy applies |

Important distinctions:

- Unloaded is not Invalid. It means the in-memory index was released.
- LRU eviction does not mean the filesystem snapshot became inconsistent.
- Because there is no persistence, loading an Unloaded volume starts a new MFT baseline rather than restoring an old index.
- Stale/Invalid describes data correctness, not memory residency.

A lightweight in-process status record may remain after index eviction so the UI can show the volume as Unloaded and display last-session metadata. It must not retain the full index or be treated as a persisted snapshot.

## 12. Synchronization scheduling

Use a hybrid policy:

1. Before a freshness-required query: synchronize the volume to a captured USN target before returning data.
2. Wake-up hint: if a FileSystemWatcher exists and reports a change, signal the synchronizer; do not apply the event directly.
3. Background synchronization: when a volume is loaded and active, coalesce hints and run synchronization without high-frequency polling.
4. Idle behavior: do not keep every volume hot or poll every disk indefinitely.
5. On reactivation: validate journal continuity and synchronize before claiming the index is current.
6. Failure: invalidate and rebuild when the required USN range is unavailable or cannot be applied safely.

A debounce/coalescing mechanism may combine bursts of wake-up hints into one USN read. The debounce must not delay a query that explicitly requires fresh data.

## 13. Snapshot Manager UI requirements

The Snapshot Manager is a view of Volume Snapshot infrastructure state, not a separate cache implementation.

### 13.1 Entry point

- Add a snapshot icon to the right-side action area of WindowTitleBar.
- Place it immediately to the left of the existing update entry.
- Keep the update entry and its popup behavior independent.
- Do not place the entry in the Treemap toolbar.

### 13.2 Hover summary

Show concise global status:

- Number of volumes currently loaded.
- Total indexed records/entries.
- Estimated resident memory.
- Number of volumes currently loading/syncing/invalid, as useful.

### 13.3 Management popup/window

Show one card per discovered or previously loaded volume, including:

- Volume label and mount point(s).
- Lifecycle/synchronization status.
- Snapshot build time for the current in-memory generation.
- Last successful USN synchronization time.
- Volume capacity and used space.
- Index record/entry count.
- Estimated resident index memory.
- Active lease count or active-use indication.
- A 16:9 Treemap-style thumbnail generated from the index summary, not a screenshot of the whole application window.
- Refresh action: incremental USN sync when the index is valid; full rebuild when invalid/unloaded.
- Manual unload action, disabled or guarded while active leases exist.

Do not confuse volume-used bytes with index memory. If the index is unloaded, show that its resident memory is zero and preserve only lightweight display metadata for the current process.

## 14. Suggested logical code organization

The implementation may adapt names to existing project conventions, but responsibilities should remain separated.

- VolumeSnapshots/VolumeSnapshotManager
- VolumeSnapshots/VolumeSnapshotBuilder
- VolumeSnapshots/VolumeSnapshotCache
- VolumeSnapshots/MemoryLifecycleManager
- VolumeSnapshots/VolumeSnapshotLease
- VolumeSnapshots/VirtualFileSystem
- VolumeSnapshots/Index/FileNodeStore
- VolumeSnapshots/Index/DirectoryEntryStore
- VolumeSnapshots/Index/DirectoryAggregateStore
- VolumeSnapshots/Synchronization/UsnSynchronizer
- VolumeSnapshots/Synchronization/UsnChangeNormalizer
- VolumeSnapshots/Synchronization/UsnChangeApplier
- VolumeSnapshots/Synchronization/SnapshotConsistencyValidator
- VolumeSnapshots/Providers/INativeVolumeIndexProvider
- VolumeSnapshots/Models/VolumeIdentity
- VolumeSnapshots/Models/VolumeSnapshotStatus
- VolumeSnapshots/Models/VolumeSnapshotMetrics

Keep the existing scanning service as a compatibility/consumer layer during migration. Avoid a broad rewrite before the new core index and its tests are independently validated.

## 15. Migration strategy

This document defines architecture, not an implementation schedule. A safe migration should nevertheless preserve the following order:

1. Audit all current filesystem change sources, delete callbacks, cleanup execution paths, Treemap refresh paths, and scan-session ownership.
2. Add the volume identity, compact index model, and invariant tests without replacing the current scanner.
3. Implement a complete-volume MFT builder with a captured starting USN watermark.
4. Implement and test replay into an unpublished index.
5. Implement the lease API and per-volume load coalescing.
6. Implement the USN synchronizer and atomic generation publication.
7. Implement VFS query/projection APIs.
8. Adapt existing scanner/Treemap consumers to VFS while preserving current user-visible behavior.
9. Add global memory accounting, automatic LRU eviction, and manual unload.
10. Add Snapshot Manager UI.
11. Remove obsolete scope-bound cache paths only after all consumers have migrated and regression tests pass.

Do not implement Unity project discovery in Core. A Unity Skill should query VFS through the documented generic API.

## 16. Required correctness and acceptance tests

### 16.1 Single MFT enumeration per loaded volume

- First access to D: performs one complete MFT enumeration.
- Subsequent requests for D:\UnityProjects, D:\AppData, and D:\Games use the same volume index.
- Those subsequent requests do not invoke another FSCTL_ENUM_USN_DATA.
- Access to E: loads E: lazily and does not rebuild D:.

### 16.2 Initial-build race

- Create, delete, rename, and modify files while MFT enumeration is in progress.
- Verify the final published index matches the filesystem after USN replay.
- Ensure no partially built index is visible before replay and validation complete.

### 16.3 Journal invalidation

- Simulate Journal ID change.
- Simulate committed watermark below LowestValidUsn.
- Simulate journal deletion/recreation.
- Simulate unreadable or unsupported records.
- Verify the snapshot becomes Invalid and no stale data is advertised as current.

### 16.4 Directory-entry correctness

- Multiple hard links to one file are represented as multiple directory entries referencing one FileNode.
- Rename within one directory updates the correct entry.
- Move across directories updates both parent relationships and aggregate values.
- Delete of an old FRN does not delete a newer object reusing the same name/slot.
- FRN sequence changes are treated as different object identity.
- Directory subtree changes update all affected ancestor aggregates.

### 16.5 Single-source event behavior

- FileSystemWatcher hints never mutate index data directly.
- A lost watcher notification is repaired by the next USN synchronization.
- A real delete performed through the application becomes visible through USN synchronization.
- Cleanup recommendation marks do not masquerade as actual filesystem deletes.
- Treemap refreshes from a committed VFS generation, not by applying raw filesystem events independently.

### 16.6 Lease and lifecycle behavior

- Two consumers acquiring one volume receive leases to the same loaded index.
- Concurrent first requests coalesce into one MFT build.
- Disposing one lease does not release another consumer's protection.
- Double-dispose does not underflow the lease count.
- A volume with active leases cannot be automatically evicted.
- Manual unload with active leases is refused or waits safely; it never force-disposes active data.
- When memory is over budget, the least-recently-used eligible volume is evicted.
- If every candidate is leased, a new load is deferred/rejected with an actionable result.
- After eviction, a later request performs a new full MFT build because no disk persistence exists.

### 16.7 Memory behavior

- Measure memory with representative volumes at approximately 100k, 1M, and multi-million entries.
- Measure peak build memory separately from steady-state resident memory.
- Validate that per-volume metrics are estimates of owned index allocations rather than arbitrary process Working Set shares.
- Verify that unloading releases the index's large backing arrays and lookup tables after outstanding references are gone.

### 16.8 Regression behavior

- Existing scan progress, cancellation, whitelist behavior, permissions handling, Treemap navigation, cleanup preview, and cleanup execution remain functional during migration.
- The existing ScanSnapshotCache retains its separate ScanSession semantics until all consumers are explicitly migrated.
- Non-NTFS volumes continue to use a supported fallback path or a clear capability error.

## 17. Implementation guardrails

- Do not persist the index to disk.
- Do not eagerly index every volume at application startup.
- Do not create a Unity-specific service in Core.
- Do not let Treemap or Skills mutate the canonical index.
- Do not use FileSystemWatcher as an authoritative event log.
- Do not silently skip unsupported USN records and still mark the index Ready.
- Do not key a file object solely by a truncated FRN segment number.
- Do not model a directory entry as a unique FileNode; hard links require separate entries.
- Do not retain full absolute paths on every indexed item unless a measured, documented exception justifies it.
- Do not evict an index with active leases.
- Do not count an unloaded index as invalid merely because its RAM was released.
- Do not treat the existing ScanSnapshotCache as Volume Snapshot.
- Do not claim a volume index is fresh until its committed USN watermark has been validated.

## 18. Open implementation questions

These are intentionally left for the implementation task to resolve through code inspection, platform constraints, and tests:

1. Exact Windows APIs and identity tuple for stable volume identity.
2. Supported USN record versions and whether V3/V4 support is required for the supported Windows/filesystem matrix.
3. Exact handling of hard links, alternate data streams, reparse points, sparse/compressed files, and cloud placeholders.
4. Most efficient metadata acquisition strategy for size/allocation/time fields missing from the current MFT record representation.
5. Whether the initial Treemap adapter should materialize a FileEntry subtree or move directly to VFS-backed entry handles.
6. Exact default memory budget and high-/low-water thresholds.
7. Whether manual unload should default to immediate refusal or offer a cancellable wait.
8. Whether FileSystemWatcher is currently present anywhere in the full solution, and if so, which consumers subscribe to it.

These questions must be resolved without weakening the confirmed design decisions above.
