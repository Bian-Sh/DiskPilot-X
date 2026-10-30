# Feature Specification: Disk Space Analyzer with AI Cleanup Recommendations

**Feature Branch**: `001-disk-space-analyzer`
**Created**: 2026-02-25
**Status**: Draft
**Input**: User description: "I would like to create a SpaceMonger-like application, but with an AI analysis built into it that recommends what files and folders can be safely removed from the analyzed drive to free space up."

## Clarifications

### Session 2026-02-25

- Q: What AI analysis approach should the recommendation engine use? → A: Cloud LLM — send file metadata to a cloud AI service for richer reasoning and analysis.
- Q: What operating systems should the application target? → A: Windows-only — target Windows 10/11 exclusively.
- Q: How should duplicate file detection work? → A: Hybrid — metadata match (name + size + date) first, then content hash only the likely duplicates for confirmation.
- Q: Should scan results persist between sessions or be ephemeral? → A: Ephemeral — scan results exist only during the current session; lost when the app closes.
- Q: What happens to the treemap after cleanup completes? → A: Auto-update — remove deleted items from in-memory scan data and refresh the visualization automatically.

### Session 2026-02-25 (2)

- Q: How does the user authenticate with the cloud LLM service? → A: User provides their own API key — entered in app settings.
- Q: What scope of file metadata should be sent to the LLM for analysis? → A: Top space consumers — analyze the largest files/folders plus known pattern directories (temp, cache, logs) for practical, fast, high-ROI recommendations.

### Session 2026-02-25 (3)

- Q: What happens when the user hovers over a treemap rectangle? → A: Tooltip on hover — show full path, size (human-readable), file type, and last modified date.
- Q: Should the app support multiple LLM providers or target a single one? → A: Single provider — target one specific LLM provider for MVP.
- Q: How should the recommendation list be organized? → A: Grouped by category with filter controls — items under category headers sorted by size, plus filters for category and safety rating.
- Q: How should the app handle elevated permissions for system directories? → A: Always require admin — app requests UAC elevation on launch every time.
- Q: What happens when the user right-clicks a treemap rectangle? → A: Basic context menu — "Open in Explorer", "Copy Path", and "Properties" (size, type, dates).

### Session 2026-02-25 (4)

- Q: Can the user re-run AI analysis within the same session? → A: Re-run replaces — user can re-trigger AI analysis anytime; new recommendations replace previous ones (accepted state resets).
- Q: Should the recommendation list support bulk selection controls? → A: Category + safety shortcuts — per-category select/deselect, plus global "Select All Safe" and "Deselect All Caution" buttons.

### Session 2026-02-25 (5)

- Q: Can the AI execute commands from chat, or is it advice-only? → A: Advice-only with copy-to-clipboard — commands in chat responses have a "Copy" button; the AI never executes commands directly.
- Q: What scan data should be sent as context with each chat message? → A: Current view + selection — send the currently visible treemap level's items plus the selected item's subtree (if any).
- Q: Where should the chat panel appear in the application layout? → A: Right-side panel — chat appears as a vertical panel on the right; treemap shrinks horizontally to accommodate.
- Q: Can the user open the chat before running AI recommendation analysis? → A: Yes — chat is available as soon as a scan completes, independent of AI recommendation analysis.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Scan and Visualize Disk Space (Priority: P1)

A user launches the application and selects a drive or folder to analyze. The system scans the file system and displays a treemap visualization where each file and folder is represented as a nested, colored rectangle proportional to its size. The user can see at a glance which folders and files consume the most space. The user can click on any rectangle to drill into that folder, and navigate back up to parent directories. A status bar displays total drive capacity, used space, free space, total file count, and total folder count.

**Why this priority**: This is the core value proposition — without visualization, none of the other features are useful. This is the minimum viable product: users can immediately identify large files and folders consuming their disk space.

**Independent Test**: Can be fully tested by scanning a drive/folder and verifying that the treemap accurately reflects actual folder sizes, supports drill-down navigation, and displays correct summary statistics.

**Acceptance Scenarios**:

1. **Given** the application is launched, **When** the user selects a drive (e.g., C:\), **Then** the system begins scanning and displays a progress indicator showing files/folders counted so far.
2. **Given** a scan is complete, **When** the treemap is displayed, **Then** each rectangle's area is proportional to its file/folder size relative to siblings, and folder names and sizes are labeled.
3. **Given** the treemap is displayed, **When** the user clicks on a folder rectangle, **Then** the view drills into that folder showing its contents as the new root of the treemap.
4. **Given** the user has drilled into a subfolder, **When** the user clicks a "back" or "up" control, **Then** the view returns to the parent folder.
5. **Given** a scan is complete, **When** the user views the status bar, **Then** it displays total capacity, used space, free space, file count, and folder count for the scanned location.
6. **Given** a scan is in progress, **When** the user wants to cancel, **Then** they can stop the scan and see partial results for what was scanned so far.
7. **Given** the treemap is displayed, **When** the user hovers over any rectangle (including those too small to show a label), **Then** a tooltip appears showing the item's full path, human-readable size, file type, and last modified date.
8. **Given** the treemap is displayed, **When** the user right-clicks any rectangle, **Then** a context menu appears with "Open in Explorer", "Copy Path", and "Properties" options.
9. **Given** the user right-clicks a rectangle and selects "Open in Explorer", **When** the action executes, **Then** Windows Explorer opens to the containing folder with the item selected.

---

### User Story 2 - AI-Powered Cleanup Recommendations (Priority: P2)

After a scan completes, the user requests AI analysis of the disk contents. The system sends file metadata (paths, sizes, types, ages — never file contents) to a cloud LLM service, which analyzes the data and returns categorized cleanup recommendations. The LLM can reason about project structures, application patterns, and contextual relationships between files beyond what static rules can detect. Recommendations cover temporary files, build caches, old downloads, duplicate files, log files, package manager caches, and other safe-to-remove items. Each recommendation includes the item path, size, a safety rating (safe / review first / caution), and a plain-language explanation of why it can be removed. The user can accept or dismiss individual recommendations and see the total space that would be recovered.

**Why this priority**: AI cleanup recommendations are the key differentiator from existing disk space tools. Once users can see their disk usage (P1), the next highest value is intelligent guidance on what to remove.

**Independent Test**: Can be tested by running a scan, triggering AI analysis, and verifying that recommendations are sensible (e.g., temp files are flagged, system-critical files are never flagged), each recommendation has an explanation, and the total recoverable space calculation is accurate.

**Acceptance Scenarios**:

1. **Given** a completed scan, **When** the user requests AI cleanup analysis, **Then** the system analyzes the scan data and displays categorized recommendations within 30 seconds for a typical drive (under 500,000 files).
2. **Given** recommendations are displayed, **When** the user views a recommendation, **Then** it shows the file/folder path, size, safety rating, category (e.g., "Temporary Files", "Build Cache", "Old Downloads"), and a human-readable explanation of why it is safe to remove.
3. **Given** recommendations are displayed, **When** the user selects individual recommendations to accept, **Then** a running total of recoverable space updates in real time.
4. **Given** the user has accepted recommendations, **When** the user views the summary, **Then** they see total items selected, total space to be recovered, and a breakdown by category.
5. **Given** system-critical files exist on the drive (OS files, boot files, application binaries in use), **When** AI analysis runs, **Then** those files are never included in cleanup recommendations.
6. **Given** a drive with 500,000+ files, **When** the user requests AI analysis, **Then** the system selects the largest files/folders and known cleanup-pattern directories for LLM analysis, and the recommendations focus on items with the highest space recovery potential.
7. **Given** recommendations are displayed, **When** the user views the list, **Then** items are grouped under category headers (each showing item count and total size), sorted by size within each group.
8. **Given** recommendations are displayed, **When** the user applies a filter by category or safety rating, **Then** the visible list updates to show only matching items and the running totals reflect the filtered view.
9. **Given** recommendations are displayed, **When** the user clicks "Select All Safe", **Then** all items rated "Safe" across all categories are accepted and the running total updates.
10. **Given** a category with multiple recommendations, **When** the user clicks "Select All" on that category header, **Then** all items in that category are accepted and the running total updates.

---

### User Story 3 - Execute Cleanup Actions (Priority: P3)

After reviewing and accepting AI recommendations, the user initiates the cleanup process. The system deletes or moves the accepted files/folders, showing progress and providing a summary of actions taken. A confirmation step prevents accidental deletion. The user can choose between permanent deletion and moving items to the recycle bin.

**Why this priority**: Executing cleanup is the natural completion of the workflow, but users can still gain value from P1 (finding large files) and P2 (knowing what to remove) without automated deletion — they could delete manually. Automated execution adds convenience.

**Independent Test**: Can be tested by accepting a set of recommendations and executing cleanup, then verifying that the correct files were removed, the summary is accurate, and no unselected files were affected.

**Acceptance Scenarios**:

1. **Given** the user has accepted cleanup recommendations, **When** the user clicks "Clean Up", **Then** a confirmation dialog shows the total number of items and total space to be freed, requiring explicit confirmation before proceeding.
2. **Given** the user confirms cleanup, **When** cleanup executes, **Then** a progress indicator shows current item being processed, items completed, and estimated time remaining.
3. **Given** cleanup is in progress, **When** a file cannot be deleted (locked, permission denied), **Then** the system skips that file, logs the error, and continues processing remaining items.
4. **Given** cleanup completes, **When** the user views the summary, **Then** it shows items successfully removed, items skipped with reasons, and actual space recovered.
5. **Given** the user prefers safer deletion, **When** they choose the "Move to Recycle Bin" option before cleanup, **Then** all removed items are moved to the system recycle bin instead of permanently deleted.
6. **Given** cleanup completes successfully, **When** the summary is displayed, **Then** the treemap automatically updates to remove deleted items and recalculates proportions, reflecting the freed space without requiring a manual rescan.

---

### User Story 4 - Color-Coded File Type Visualization (Priority: P4)

The treemap uses distinct colors to differentiate file types and folder categories, making it easy to identify patterns visually. For example, media files in one color family, documents in another, system files in another. The user can see a legend mapping colors to file type categories.

**Why this priority**: Color-coding enhances the visual analysis experience but is not essential for the core scan-visualize-recommend-cleanup workflow. It adds polish and faster comprehension.

**Independent Test**: Can be tested by scanning a directory with known diverse file types and verifying that different file categories render in distinct, consistent colors and the legend accurately reflects the mapping.

**Acceptance Scenarios**:

1. **Given** a scan is complete, **When** the treemap is displayed, **Then** files are colored by type category (e.g., media, documents, executables, archives, temporary/cache, system) with visually distinct colors.
2. **Given** the treemap is displayed, **When** the user views the color legend, **Then** it shows each color mapped to its file type category.
3. **Given** a folder contains mixed file types, **When** the user drills into it, **Then** child rectangles display their respective type colors while the parent folder boundary remains visible.

---

### User Story 5 - Interactive AI Chat for File Questions (Priority: P5)

The application includes a small chat panel where the user can ask the AI questions about any file or folder visible in the treemap or in the recommendation list. The user can ask about the purpose of a file (e.g., "What is hiberfil.sys?"), whether it is safe to remove, how to remove files that cannot be deleted through normal means (e.g., system files requiring special commands or configuration changes), and general questions about disk space usage patterns. The chat has context — it knows what drive was scanned and what files are present, so it can give specific, actionable answers rather than generic information. The user can click on a treemap rectangle or recommendation item and have it automatically referenced in the chat for quick inquiry.

**Why this priority**: The chat feature extends the AI capability from passive recommendations to interactive learning. Users gain understanding of their file system rather than blindly following suggestions. However, the core scan-visualize-recommend-cleanup workflow (P1-P3) delivers full value without the chat. This is a power-user enhancement.

**Independent Test**: Can be tested by scanning a drive, opening the chat panel, asking questions about specific files (e.g., "What is pagefile.sys and can I reduce its size?"), and verifying that responses are accurate, contextual, and actionable.

**Acceptance Scenarios**:

1. **Given** a scan is complete and the treemap is displayed, **When** the user opens the chat panel, **Then** a vertical chat panel appears on the right side of the window and the treemap shrinks horizontally to accommodate it without overlapping.
2. **Given** the chat panel is open, **When** the user types a question about a file (e.g., "What is hiberfil.sys?"), **Then** the AI responds with the file's purpose, whether it can be safely removed, and specific instructions for how to remove or reduce it if applicable.
3. **Given** the chat panel is open, **When** the user clicks on a treemap rectangle and then asks a question, **Then** the chat automatically includes the clicked item's path and metadata as context, so the user can simply ask "What is this?" or "Can I delete this?".
4. **Given** the chat panel is open, **When** the user clicks on a recommendation item and asks a follow-up question, **Then** the chat includes that recommendation's details (path, category, safety rating, explanation) as context.
5. **Given** the user asks about a file that requires special removal steps (e.g., hiberfil.sys requires disabling hibernation via a system command), **When** the AI responds, **Then** it provides step-by-step instructions specific to the user's situation, including any warnings about consequences.
6. **Given** the user asks a general question about disk space (e.g., "Why is my C drive so full?"), **When** the AI responds, **Then** it references the actual scan data — citing the largest folders and categories consuming space on the scanned drive.
7. **Given** the chat panel is open and the user has an ongoing conversation, **When** the user closes and reopens the chat panel within the same session, **Then** the conversation history is preserved.
8. **Given** the AI response contains a command (e.g., `powercfg /hibernate off`), **When** the user views the response, **Then** the command is rendered in a distinct code block with a "Copy" button that copies the command to the clipboard. The AI never executes commands directly.

---

### Edge Cases

- What happens when the user scans a drive with no removable files? The AI analysis completes and reports "No cleanup recommendations — your drive is well-maintained" with a summary of space usage by category.
- What happens when the user scans an empty or nearly empty drive? The treemap displays the existing contents proportionally and the status bar reflects the small usage accurately.
- What happens when the drive contents change during a scan (files added/removed by other processes)? The scan captures a point-in-time snapshot; if a file disappears mid-scan, it is skipped without error.
- What happens when a folder is still inaccessible despite admin privileges (e.g., encrypted by another user, exclusively locked)? The system marks those folders as "Access Denied" in the treemap with a distinct visual indicator and excludes them from AI analysis.
- What happens when a recommended file is deleted by another process before cleanup executes? The system skips the missing file, logs it as "already removed", and adjusts the recovered space total.
- What happens when the user scans a network drive or external USB drive? The application supports scanning any mounted drive or folder path accessible to the user, though network drives may scan more slowly due to latency.
- What happens when the scan encounters symbolic links or junction points? The system follows the link for display purposes but marks it as a link and does not recommend deletion of link targets.
- What happens when the internet connection is lost during AI analysis? The system notifies the user, offers to retry, and allows continued use of the treemap without AI recommendations.
- What happens when the cloud LLM service is slow or times out? The system shows analysis progress and allows cancellation; if the request times out after 60 seconds, it notifies the user and offers to retry.
- What happens when the user has not configured an API key and requests AI analysis? The system prompts the user to enter their API key in settings before AI features can be used. Treemap visualization works without a key.
- What happens when the API key is invalid or expired? The system displays a clear error message identifying the key as invalid and directs the user to settings to update it.
- What happens when the drive has far more files than the LLM can process in one request? The system prioritizes the largest items and known cleanup-pattern directories, ensuring the highest-value recommendations are always generated even on very large drives.
- What happens when duplicate candidates have matching name and size but different content? The hash confirmation step detects the mismatch and excludes them from duplicate recommendations.
- What happens when the user re-runs AI analysis after already accepting some recommendations? The system warns that existing selections will be lost, and on confirmation, replaces all recommendations with fresh results.
- What happens when the user asks a chat question without an API key configured? The system prompts the user to configure their API key in settings before the chat can respond. The chat panel itself can be opened but shows a message that an API key is required.
- What happens when the user asks a chat question unrelated to disk space or files? The AI responds helpfully but steers the conversation back to disk analysis, stating it is specialized for file system questions.
- What happens when the internet connection drops during a chat conversation? The system displays an error on the failed message and allows the user to retry sending it when connectivity is restored. Previous messages in the conversation remain visible.
- What happens when the AI provides removal instructions that require admin commands? The response clearly labels the steps as requiring administrator privileges and includes warnings about potential consequences (e.g., disabling hibernation reduces resume-from-sleep capability).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST scan a user-selected drive or folder and enumerate all files and folders with their sizes, recursively.
- **FR-002**: System MUST display scan results as an interactive treemap where rectangle area is proportional to file/folder size.
- **FR-003**: System MUST support drill-down navigation — clicking a folder zooms into it; a back control returns to the parent.
- **FR-025**: System MUST display a tooltip on mouse hover over any treemap rectangle showing the item's full path, human-readable size, file type, and last modified date. Tooltips appear for all items regardless of whether the rectangle is large enough to display a label.
- **FR-028**: System MUST display a right-click context menu on any treemap rectangle with the following actions: "Open in Explorer" (opens the containing folder and selects the item), "Copy Path" (copies the full path to clipboard), and "Properties" (shows a dialog with size, type, creation date, last modified date, and full path).
- **FR-004**: System MUST display a status bar showing total capacity, used space, free space, file count, and folder count.
- **FR-005**: System MUST show a progress indicator during scanning with files/folders counted and option to cancel.
- **FR-006**: System MUST provide AI-powered analysis via a cloud LLM service that categorizes files/folders into cleanup recommendation categories: temporary files, build caches, package manager caches, old downloads, log files, duplicate files, and other safe-to-remove items. The LLM receives file metadata only (paths, sizes, types, modification dates) — never file contents. The system prioritizes sending metadata for the largest files/folders and known cleanup-pattern directories (temp, cache, logs) to stay within LLM context limits while maximizing recoverable space identified.
- **FR-021**: System MUST detect duplicate files using a hybrid approach: first identify candidates by matching file name, size, and modification date, then confirm duplicates by computing content hashes only for those candidates. Only confirmed duplicates (hash-verified) are included in cleanup recommendations.
- **FR-018**: System MUST require an active internet connection for AI analysis and clearly notify the user if the connection is unavailable, offering to retry or skip AI analysis.
- **FR-019**: System MUST transmit only file metadata (paths, names, sizes, types, modification dates) to the cloud LLM — never file contents, and the user MUST be informed of what data is sent before the first analysis.
- **FR-020**: System MUST allow the user to proceed without AI analysis (manual treemap exploration only) when internet is unavailable or the user declines cloud analysis.
- **FR-029**: System MUST allow the user to re-trigger AI analysis at any time during the session. Re-running analysis replaces all previous recommendations — any previously accepted or dismissed states are reset. A confirmation prompt warns the user before replacing existing recommendations that have accepted items.
- **FR-023**: System MUST provide a settings screen where the user enters and stores their API key for the single supported LLM provider. The key MUST be stored securely on the local machine (not in plaintext config files). AI analysis features are disabled until a valid API key is configured. Multi-provider support is out of scope for MVP.
- **FR-024**: System MUST validate the API key on entry (e.g., test call) and clearly indicate whether it is valid or invalid before the user attempts AI analysis.
- **FR-022**: System MUST automatically update the treemap visualization after cleanup completes — removing deleted items from the in-memory scan data and recalculating rectangle proportions — without requiring a full rescan.
- **FR-007**: System MUST assign a safety rating to each recommendation: "Safe" (no risk), "Review First" (low risk but user should verify), or "Caution" (files may be wanted but are candidates for removal based on age/size).
- **FR-008**: System MUST provide a human-readable explanation for each cleanup recommendation explaining why the item is safe to remove.
- **FR-009**: System MUST never recommend deletion of operating system files, active application binaries, user-created documents in standard locations (Desktop, Documents, Pictures, etc.), or files currently in use.
- **FR-010**: System MUST allow the user to accept or dismiss individual recommendations and display a running total of recoverable space. Recommendations are grouped by category (e.g., "Temporary Files", "Build Cache") with items sorted by size (largest first) within each group. Each category header shows item count and total size for that category.
- **FR-026**: System MUST provide filter controls allowing the user to filter recommendations by category and/or safety rating. Filters update the visible list and running totals in real time.
- **FR-030**: System MUST provide bulk selection controls: "Select All" / "Deselect All" per category header, plus global shortcut buttons "Select All Safe" (accepts all items rated "Safe" across all categories) and "Deselect All Caution" (dismisses all items rated "Caution"). Bulk actions update the running total immediately.
- **FR-011**: System MUST require explicit user confirmation before executing any file deletions.
- **FR-012**: System MUST support two deletion modes: permanent deletion and move-to-Windows-Recycle-Bin.
- **FR-013**: System MUST handle locked or permission-denied files gracefully during cleanup — skip and continue, logging the failure.
- **FR-014**: System MUST display a cleanup summary showing items removed, items skipped, and actual space recovered.
- **FR-015**: System MUST color-code treemap rectangles by file type category with a visible legend.
- **FR-016**: System MUST handle symbolic links and junction points without following them into infinite loops and without recommending deletion of link targets.
- **FR-017**: System MUST support scanning any Windows drive letter or accessible folder path, including external USB drives and mapped network drives.
- **FR-027**: System MUST request Windows UAC administrator elevation on launch. If the user declines the UAC prompt, the application MUST NOT start (admin privileges are required for full disk scanning access).
- **FR-031**: System MUST provide a collapsible chat panel positioned as a vertical panel on the right side of the application window. When opened, the treemap shrinks horizontally to accommodate the chat. When collapsed, the treemap reclaims the full width. The chat uses the same cloud LLM service and API key as the recommendation engine.
- **FR-032**: System MUST allow the user to ask free-form questions about any file or folder, including its purpose, whether it is safe to remove, and specific removal instructions for files that require non-standard deletion methods (e.g., system files, hibernation files, page files). The chat is advice-only — the AI MUST NOT execute any commands or modify files directly.
- **FR-037**: System MUST render command snippets in chat responses with a "Copy" button that copies the command text to the clipboard, enabling the user to paste and execute commands manually in their own terminal.
- **FR-033**: System MUST provide contextual awareness in chat — when the user clicks a treemap rectangle or recommendation item and then asks a question, the selected item's path and metadata are automatically included as context in the chat message.
- **FR-034**: System MUST include scan data as context for chat responses by sending the currently visible treemap level's items plus the selected item's subtree (if any). This enables the AI to reference actual files, sizes, and categories the user is currently viewing when answering questions (e.g., "Why is my drive full?" or "What are these files?"). The system MUST NOT send the entire file tree to stay within LLM context limits.
- **FR-035**: System MUST preserve chat conversation history within the current session. Closing and reopening the chat panel does not clear the conversation. Chat history is ephemeral — it is lost when the application closes.
- **FR-038**: System MUST make the chat panel available as soon as a scan completes, independent of whether AI recommendation analysis has been run. The user does not need to trigger recommendations before using the chat to ask questions about files.
- **FR-036**: System MUST scope chat responses to file system and disk management topics. Off-topic questions receive a polite redirect indicating the assistant specializes in disk space analysis.

### Key Entities

- **ScanSession**: Represents a single scan operation — the target path, start/end time, total files/folders discovered, and total size. Sessions are ephemeral (in-memory only, not persisted to disk).
- **FileEntry**: An individual file or folder discovered during scanning — path, name, size, type/extension, last modified date, parent-child relationships forming a tree, and optionally a content hash (computed only for duplicate candidates).
- **TreemapNode**: A visual representation of a FileEntry in the treemap — position, dimensions, color, label, and nesting depth.
- **CleanupRecommendation**: An AI-generated suggestion to remove a file or folder — target path, size, category, safety rating, explanation text, and user acceptance status.
- **CleanupAction**: A record of an executed deletion — target path, action taken (deleted/recycled/skipped), result (success/failure), failure reason if applicable, and timestamp.
- **ChatMessage**: A single message in the chat conversation — sender (user or AI), text content, timestamp, and optionally a linked FileEntry reference (when the user clicked a treemap item before asking). Chat history is ephemeral (in-memory only).

## Success Criteria *(mandatory)*

> **2026-06-19 documentation addendum**: Later implementation work introduced additional requirements around WPF navigation, breadcrumb dropdown behavior, localized settings, diagnostics console, acceptance automation, release packaging, and PC Use validation. The reconstructed Chinese PRD lives at `docs/product-requirements-reconstructed-2026-06-19.md` and should be treated as the current gap-filling requirements index until those items are fully merged into this canonical SDD spec.

### Measurable Outcomes

- **SC-001**: Users can scan a 500,000-file drive and see the complete treemap visualization within 60 seconds.
- **SC-002**: AI cleanup recommendations are generated within 30 seconds after scan completion for drives with up to 500,000 files.
- **SC-003**: AI recommendations correctly identify at least 80% of known safe-to-remove items (temp files, caches, logs) in a test dataset.
- **SC-004**: AI recommendations produce zero false positives for system-critical files (OS files, active application binaries) — 100% safety for protected categories.
- **SC-005**: Users can navigate from the root treemap view to any 5-level-deep folder in under 5 clicks.
- **SC-006**: 90% of users can complete the full workflow (scan, review recommendations, execute cleanup) on their first attempt without external help.
- **SC-007**: The treemap visualization accurately reflects actual file sizes — no rectangle is more than 5% off in proportional area relative to its true size share.
- **SC-008**: Cleanup execution handles at least 95% of accepted recommendations successfully, with clear error reporting for any skipped items.
- **SC-009**: Chat responses about well-known system files (e.g., hiberfil.sys, pagefile.sys, swapfile.sys) are accurate and include correct removal/management instructions at least 95% of the time.
- **SC-010**: Chat responses referencing scan data correctly cite actual files and folders present on the scanned drive — no hallucinated paths or sizes.

## Assumptions

- The application runs on the same machine whose drives are being analyzed (local file system access).
- The application always runs with administrator privileges (requests UAC elevation on launch) to ensure full access to all directories including system folders, Windows temp, and protected areas.
- "AI analysis" uses a cloud LLM service that receives file metadata (paths, sizes, types, ages) and returns categorized cleanup recommendations with explanations. File contents are never transmitted. The user is informed of what data is sent before first use. The user provides and manages their own LLM API key; the application does not include or subsidize API access.
- The treemap visualization style follows the classic SpaceMonger approach: alternating horizontal and vertical splits for nested rectangles, with labels showing folder/file names and sizes.
- Network drive scanning is supported but performance expectations (SC-001, SC-002) apply to local drives only.
- The application targets Windows 10 and Windows 11 exclusively. Cross-platform support is out of scope.
- The application integrates with a single LLM provider for AI analysis. Multi-provider support is out of scope for MVP.
