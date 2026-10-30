# Tasks: Disk Space Analyzer with AI Cleanup Recommendations

**Input**: Design documents from `specs/001-disk-space-analyzer/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/llm-api-contract.md, quickstart.md

**Tests**: Not included — not explicitly requested in the feature specification. Test project scaffolding is created in Setup for future use.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create .NET 8 solution structure, configure NuGet packages, and establish shared types

- [X] T001 Create .NET solution file at src/SpaceMonger.sln with four projects: SpaceMonger.App (WPF, net8.0-windows), SpaceMonger.Core (class library, net8.0-windows), SpaceMonger.Core.Tests (xunit), SpaceMonger.App.Tests (xunit) — use `dotnet new` commands to scaffold and add project references (App → Core, test projects → their targets)
- [X] T002 [P] Configure src/SpaceMonger.Core/SpaceMonger.Core.csproj with NuGet packages: Microsoft.Extensions.Http, System.Security.Cryptography.ProtectedData — target framework net8.0-windows
- [X] T003 [P] Configure src/SpaceMonger.App/SpaceMonger.App.csproj with NuGet packages: SkiaSharp.Views.WPF, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection — add project reference to SpaceMonger.Core, ensure UseWPF is true
- [X] T004 [P] Configure tests/SpaceMonger.Core.Tests/SpaceMonger.Core.Tests.csproj with NuGet packages: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, NSubstitute, FluentAssertions — project reference to SpaceMonger.Core
- [X] T005 [P] Configure tests/SpaceMonger.App.Tests/SpaceMonger.App.Tests.csproj with NuGet packages: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, NSubstitute, FluentAssertions — project reference to SpaceMonger.App
- [X] T006 [P] Create application manifest at src/SpaceMonger.App/app.manifest with `<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />` and reference it in SpaceMonger.App.csproj (FR-027)
- [X] T007 [P] Create all shared enums in src/SpaceMonger.Core/Enums/: SafetyRating.cs (Safe, ReviewFirst, Caution), RecommendationCategory.cs (TemporaryFiles, BuildCache, PackageManagerCache, OldDownloads, LogFiles, DuplicateFiles, BrowserCache, SystemCache, Other), DeletionMode.cs (PermanentDelete, MoveToRecycleBin), CleanupResult.cs (Success, Failed, Skipped, AlreadyRemoved), ChatSender.cs (User, Assistant)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models and infrastructure that MUST be complete before ANY user story can be implemented

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T008 Create FileEntry model in src/SpaceMonger.Core/Models/FileEntry.cs with fields: Path (string), Name (string), Size (long), Extension (string?), LastModified (DateTime), IsDirectory (bool), IsReparsePoint (bool), IsAccessDenied (bool), ContentHash (byte[]?), Parent (FileEntry?), Children (List\<FileEntry\>), Depth (int) — include helper method to recalculate size up parent chain when children change
- [X] T009 [P] Create ScanSession model in src/SpaceMonger.Core/Models/ScanSession.cs with fields: Id (Guid), TargetPath (string), StartTime (DateTime), EndTime (DateTime?), TotalFiles (int), TotalFolders (int), TotalSize (long), RootEntry (FileEntry?), IsCancelled (bool), DriveCapacity (long?), DriveFreeSpace (long?) — populate drive info via DriveInfo when target is a drive root
- [X] T010 [P] Implement FileSizeConverter in src/SpaceMonger.App/Converters/FileSizeConverter.cs as an IValueConverter that converts long byte values to human-readable strings (bytes, KB, MB, GB, TB) with one decimal place
- [X] T011 Set up dependency injection container in src/SpaceMonger.App/App.xaml.cs: create ServiceCollection, register all services as singletons, register ViewModels as transient, configure IHttpClientFactory with named client "Anthropic" (base address https://api.anthropic.com, 60s timeout), build ServiceProvider, resolve MainWindow on startup
- [X] T012 Create MainWindow shell in src/SpaceMonger.App/Views/MainWindow.xaml with: toolbar (drive/folder selector ComboBox with Browse button, Scan button, Analyze button, Chat toggle button, Settings gear button), central content area (Grid to host TreemapView and later ChatPanel), and status bar (TextBlocks for capacity, used, free, file count, folder count) — bind to MainViewModel

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 — Scan and Visualize Disk Space (Priority: P1) MVP

**Goal**: Users can scan a drive/folder, see an interactive squarified treemap, drill down into folders, navigate back, view tooltips, use context menus, and see summary statistics.

**Independent Test**: Scan a drive or folder and verify: treemap rectangles are proportional to actual sizes, clicking a folder drills down, back button returns to parent, status bar shows correct totals, hover tooltip shows path/size/type/date, right-click shows context menu with Open in Explorer/Copy Path/Properties.

### Implementation for User Story 1

- [X] T013 [P] [US1] Define IFileScanner interface in src/SpaceMonger.Core/Services/Scanning/IFileScanner.cs with method: Task\<ScanSession\> ScanAsync(string path, IProgress\<ScanProgress\> progress, CancellationToken cancellationToken) — also create ScanProgress record (CurrentPath string, FileCount int, FolderCount int) in the same file
- [X] T014 [P] [US1] Define ITreemapLayoutEngine interface in src/SpaceMonger.Core/Services/Treemap/ITreemapLayoutEngine.cs with method: List\<TreemapNode\> ComputeLayout(FileEntry root, float width, float height, int maxDepth) — also create TreemapNode class in src/SpaceMonger.Core/Models/TreemapNode.cs with fields: Entry (FileEntry), X (float), Y (float), Width (float), Height (float), ColorHex (string), Depth (int), IsVisible (bool), Label (string?) — use string for color to avoid SkiaSharp dependency in Core
- [X] T015 [US1] Implement FileScanner in src/SpaceMonger.Core/Services/Scanning/FileScanner.cs: breadth-first directory enumeration using Directory.EnumerateDirectories and Directory.EnumerateFiles, build FileEntry tree with parent-child links, accumulate sizes bottom-up, catch UnauthorizedAccessException per-directory and mark as IsAccessDenied, detect symlinks/junctions via FileAttributes.ReparsePoint and set IsReparsePoint (FR-001, FR-005, FR-016, FR-017), report progress via IProgress, support CancellationToken for cancel-and-show-partial-results
- [X] T016 [US1] Implement SquarifiedTreemapLayout in src/SpaceMonger.Core/Services/Treemap/SquarifiedTreemapLayout.cs: sort children by size descending, squarify algorithm (add items to row while worst aspect ratio improves, finalize row when it worsens, alternate horizontal/vertical per depth), assign ColorHex based on file extension category, set Label to "Name (Size)" for rectangles above minimum label threshold, mark IsVisible=false for rectangles smaller than 3x3 pixels
- [X] T017 [US1] Implement TreemapControl in src/SpaceMonger.App/Controls/TreemapControl.cs as a custom WPF control hosting SKElement: render TreemapNode list (fill rectangles with color, draw borders, render labels with SkiaSharp), implement hit-testing (find node at mouse position by iterating nodes in reverse depth order), expose events: NodeClicked, NodeHovered, NodeRightClicked with TreemapNode payload (FR-002)
- [X] T018 [US1] Implement TreemapViewModel in src/SpaceMonger.App/ViewModels/TreemapViewModel.cs: CurrentRoot (FileEntry, starts at scan root), NavigationStack (Stack\<FileEntry\> for breadcrumb), DrillDown(FileEntry) pushes current and sets new root, NavigateUp() pops stack, BreadcrumbPath (observable list of ancestor names), SelectedNode (TreemapNode?), HoveredNode (TreemapNode?), recompute layout via ITreemapLayoutEngine when CurrentRoot changes or control resizes (FR-003)
- [X] T019 [US1] Create TreemapView in src/SpaceMonger.App/Views/TreemapView.xaml: host TreemapControl filling available space, breadcrumb bar at top showing navigation path as clickable TextBlocks with ">" separators and an Up button, bind TreemapControl events to TreemapViewModel commands (FR-003)
- [X] T020 [US1] Implement MainViewModel in src/SpaceMonger.App/ViewModels/MainViewModel.cs: SelectedPath (string, bound to drive/folder selector), ScanCommand (async, creates FileScanner, calls ScanAsync, stores ScanSession), CancelScanCommand (triggers CancellationTokenSource), IsScanning (bool), ScanProgress (ScanProgress), CurrentSession (ScanSession?), populate drive list from DriveInfo.GetDrives() on startup (FR-005)
- [X] T021 [US1] Wire status bar in src/SpaceMonger.App/Views/MainWindow.xaml to bind TextBlocks to MainViewModel properties: DriveCapacity, UsedSpace (capacity - free), FreeSpace, FileCount, FolderCount — computed from CurrentSession, formatted via FileSizeConverter (FR-004)
- [X] T022 [US1] Implement tooltip in TreemapControl: on mouse move, find hovered TreemapNode via hit-test, show WPF ToolTip with full path, human-readable size (via FileSizeConverter logic), file type (extension or "Folder"), and last modified date formatted as local date-time — tooltip appears for all items including those too small for labels (FR-025)
- [X] T023 [US1] Implement right-click context menu on TreemapControl: WPF ContextMenu with three items — "Open in Explorer" (Process.Start explorer.exe /select,{path}), "Copy Path" (Clipboard.SetText), "Properties" dialog (modal window showing size, type, creation date, last modified date, full path) (FR-028)
- [X] T024 [US1] Register US1 services in DI container in App.xaml.cs: IFileScanner → FileScanner, ITreemapLayoutEngine → SquarifiedTreemapLayout — wire TreemapViewModel into TreemapView DataContext, wire MainViewModel into MainWindow DataContext, connect scan completion to TreemapViewModel.SetRoot()

**Checkpoint**: User Story 1 fully functional — scan any drive/folder, see interactive treemap, drill down/up, hover tooltips, right-click context menu, status bar stats

---

## Phase 4: User Story 2 — AI-Powered Cleanup Recommendations (Priority: P2)

**Goal**: Users can trigger AI analysis of scan results, view categorized recommendations with safety ratings and explanations, filter and bulk-select items, and see running total of recoverable space.

**Independent Test**: Scan a drive, trigger AI analysis, verify recommendations are categorized and sorted by size, each has an explanation, system files are never recommended, filters update the list, "Select All Safe" works, and running total is accurate.

### Implementation for User Story 2

- [X] T025 [P] [US2] Create AppSettings model in src/SpaceMonger.Core/Models/AppSettings.cs with fields: EncryptedApiKey (byte[]?), IsApiKeyValid (bool), DeletionMode (DeletionMode, default MoveToRecycleBin), LastScanPath (string?) per data-model.md
- [X] T026 [P] [US2] Create CleanupRecommendation model in src/SpaceMonger.Core/Models/CleanupRecommendation.cs with fields: Id (string), TargetPath (string), Entry (FileEntry), Size (long), Category (RecommendationCategory), SafetyRating (SafetyRating), Explanation (string), IsAccepted (bool), IsDismissed (bool) — include validation that IsAccepted and IsDismissed cannot both be true per data-model.md
- [X] T027 [US2] Define ISettingsService interface and implement SettingsService in src/SpaceMonger.Core/Services/Settings/SettingsService.cs: LoadSettings() reads from %APPDATA%\SpaceMonger\settings.dat, SaveSettings() writes JSON with DPAPI-encrypted API key via ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser), GetApiKey() decrypts with ProtectedData.Unprotect, create directory if not exists (FR-023)
- [X] T028 [US2] Define ILlmClient interface and implement AnthropicClient in src/SpaceMonger.Core/Services/Llm/AnthropicClient.cs: SendAnalysisAsync(systemPrompt, fileMetadataJson, apiKey, CancellationToken) and SendChatAsync(systemPrompt, messages[], apiKey, CancellationToken) — build HTTP request per llm-api-contract.md (model: claude-sonnet-4-20250514, anthropic-version: 2023-06-01, x-api-key header), parse response JSON from content[0].text, handle errors (401→invalid key, 429→rate limited with retry-after, 500+→server error, timeout), ValidateApiKeyAsync(apiKey) via minimal test call (FR-024)
- [X] T029 [US2] Define IDuplicateDetector interface and implement DuplicateDetector in src/SpaceMonger.Core/Services/Analysis/DuplicateDetector.cs: FindDuplicatesAsync(FileEntry root) groups files by (Name, Size, LastModified), for groups with 2+ members compute xxHash3 via System.IO.Hashing.XxHash3 streaming in 8KB chunks, return list of confirmed duplicate groups with hash values (FR-021)
- [X] T030 [US2] Define IRecommendationEngine interface and implement RecommendationEngine in src/SpaceMonger.Core/Services/Analysis/RecommendationEngine.cs: AnalyzeAsync(ScanSession, apiKey, CancellationToken) — select metadata: top N items by size + items matching known cleanup patterns (Temp, .npm/_cacache, .nuget, node_modules, obj/bin, __pycache__, .gradle, browser cache paths) + duplicate groups from DuplicateDetector, build JSON metadata payload per llm-api-contract.md format, construct analysis system prompt (categories, safety ratings, never-recommend list, JSON output format), call ILlmClient.SendAnalysisAsync, parse response JSON into List\<CleanupRecommendation\>, post-filter: reject paths under Windows/, Program Files/, user document folders (FR-006, FR-007, FR-008, FR-009), estimate ~50 tokens per item to stay within ~100K input token budget
- [X] T031 [US2] Implement SettingsViewModel in src/SpaceMonger.App/ViewModels/SettingsViewModel.cs: ApiKey (string, bound to input), ValidateCommand (async, calls ILlmClient.ValidateApiKeyAsync, sets IsValid/IsInvalid state), SaveCommand (encrypts and saves via ISettingsService), SelectedDeletionMode (bound to dropdown), ValidationState (None/Validating/Valid/Invalid) (FR-023, FR-024)
- [X] T032 [US2] Create SettingsDialog in src/SpaceMonger.App/Views/SettingsDialog.xaml: PasswordBox for API key entry, Validate button with spinner and green-check/red-x indicator, deletion mode ComboBox (Recycle Bin / Permanent Delete), data privacy notice text explaining that file paths, sizes, types, and dates are sent to the cloud LLM (never file contents) per FR-019, Save and Cancel buttons
- [X] T033 [US2] Implement SafetyRatingToBrushConverter in src/SpaceMonger.App/Converters/SafetyRatingToBrushConverter.cs: Safe → green brush (#4CAF50), ReviewFirst → amber brush (#FF9800), Caution → red brush (#F44336)
- [X] T034 [US2] Implement RecommendationsViewModel in src/SpaceMonger.App/ViewModels/RecommendationsViewModel.cs: Recommendations (ObservableCollection\<CleanupRecommendation\>), grouped by Category using CollectionViewSource, filter properties: SelectedCategoryFilter (RecommendationCategory?), SelectedSafetyFilter (SafetyRating?), filter predicate updates list in real time, AcceptCommand/DismissCommand per item, SelectAllSafeCommand (accept all Safe items across categories), DeselectAllCautionCommand (dismiss all Caution items), per-category SelectAllCommand/DeselectAllCommand, computed properties: TotalRecoverableSpace (sum of accepted items' Size), TotalSelectedCount, CategoryBreakdown, re-analysis support: AnalyzeCommand triggers RecommendationEngine, warns if existing accepted items will be lost per FR-029 (FR-010, FR-026, FR-029, FR-030)
- [X] T035 [US2] Create RecommendationsPanel in src/SpaceMonger.App/Views/RecommendationsPanel.xaml: filter bar at top with ComboBoxes for category and safety rating filter, grouped ListView/ItemsControl with category headers showing category name, item count, total size, and Select All/Deselect All buttons per header — each item row shows: checkbox (accept), file path, size (via FileSizeConverter), safety rating badge (colored via SafetyRatingToBrushConverter), explanation text — bottom bar shows running total: selected items count and total recoverable space, global buttons: "Select All Safe", "Deselect All Caution" (FR-010, FR-026, FR-030)
- [X] T036 [US2] Wire Analyze button in MainWindow toolbar to RecommendationsViewModel.AnalyzeCommand: show loading overlay during AI analysis, display RecommendationsPanel when results arrive, handle states: no API key configured (prompt to open Settings), no internet (show error with retry/skip per FR-018), no scan completed (disable button), add "Clean Up" button in RecommendationsPanel bottom bar (disabled until items accepted) (FR-018, FR-020)
- [X] T037 [US2] Register US2 services in DI container in App.xaml.cs: ISettingsService → SettingsService, ILlmClient → AnthropicClient, IDuplicateDetector → DuplicateDetector, IRecommendationEngine → RecommendationEngine — wire RecommendationsViewModel and SettingsViewModel into their respective views

**Checkpoint**: User Stories 1 AND 2 both work — scan, visualize, trigger AI analysis, view/filter/select recommendations

---

## Phase 5: User Story 3 — Execute Cleanup Actions (Priority: P3)

**Goal**: Users can execute accepted cleanup recommendations with a confirmation step, choose deletion mode, see progress, view a summary of results, and the treemap auto-updates.

**Independent Test**: Accept several recommendations, click Clean Up, verify confirmation dialog shows correct totals, execute cleanup, verify correct files removed (or recycled), skipped items reported with reasons, treemap updates without rescan.

### Implementation for User Story 3

- [X] T038 [P] [US3] Create CleanupAction model and CleanupProgress record in src/SpaceMonger.Core/Models/CleanupAction.cs — CleanupAction fields: Id (Guid), Recommendation (CleanupRecommendation), ActionType (DeletionMode), Result (CleanupResult), FailureReason (string?), Timestamp (DateTime), ActualSizeFreed (long) per data-model.md — CleanupProgress record: CurrentItemPath (string), CompletedCount (int), TotalCount (int)
- [X] T039 [US3] Define ICleanupService interface and implement CleanupService in src/SpaceMonger.Core/Services/Cleanup/CleanupService.cs: ExecuteCleanupAsync(List\<CleanupRecommendation\> accepted, DeletionMode mode, IProgress\<CleanupProgress\> progress, CancellationToken ct) — for PermanentDelete use File.Delete/Directory.Delete(recursive), for MoveToRecycleBin use Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile with RecycleOption.SendToRecycleBin, wrap each item in try-catch: catch IOException/UnauthorizedAccessException → Skipped, catch FileNotFoundException → AlreadyRemoved, report progress per item (FR-011, FR-012, FR-013)
- [X] T040 [US3] Implement treemap auto-update after cleanup in TreemapViewModel: given list of CleanupAction results, for each Success/AlreadyRemoved action remove the corresponding FileEntry from its parent's Children list, recalculate Size up the parent chain via FileEntry helper method, update ScanSession totals (TotalFiles, TotalFolders, TotalSize), recompute treemap layout for current view (FR-022)
- [X] T041 [US3] Create CleanupConfirmDialog in src/SpaceMonger.App/Views/CleanupConfirmDialog.xaml: display total items count, total space to be freed (formatted via FileSizeConverter), deletion mode selector (RadioButtons: "Move to Recycle Bin" default, "Permanently Delete" with warning text), Confirm and Cancel buttons — dialog returns DeletionMode on confirm, null on cancel (FR-011, FR-012)
- [X] T042 [US3] Create CleanupSummaryDialog in src/SpaceMonger.App/Views/CleanupSummaryDialog.xaml: show items successfully removed (count + total size), items skipped with individual reasons listed (path + error message), items already removed (count), actual total space recovered — OK button to dismiss (FR-014)
- [X] T043 [US3] Wire cleanup flow in RecommendationsPanel and MainViewModel: "Clean Up" button → open CleanupConfirmDialog → on confirm call ICleanupService.ExecuteCleanupAsync with progress overlay → on complete show CleanupSummaryDialog → call TreemapViewModel auto-update → refresh RecommendationsPanel (remove completed items)
- [X] T044 [US3] Register ICleanupService → CleanupService in DI container in App.xaml.cs

**Checkpoint**: Full core workflow complete — scan, visualize, AI recommend, cleanup, treemap auto-updates

---

## Phase 6: User Story 4 — Color-Coded File Type Visualization (Priority: P4)

**Goal**: Treemap rectangles are colored by file type category with a visible legend showing the color mapping.

**Independent Test**: Scan a directory with diverse file types (images, documents, executables, archives, source code), verify each category renders in a distinct color, and the legend accurately maps colors to category names.

### Implementation for User Story 4

- [X] T045 [P] [US4] Create FileTypeColorMap in src/SpaceMonger.Core/Services/Treemap/FileTypeColorMap.cs: define color categories with hex values — Media (#2196F3 blue: .jpg, .png, .gif, .mp4, .mp3, .wav, .avi, .mkv), Documents (#4CAF50 green: .pdf, .doc, .docx, .xls, .xlsx, .ppt, .txt, .csv), Executables (#F44336 red: .exe, .dll, .msi, .bat, .cmd, .ps1), Archives (#FF9800 orange: .zip, .rar, .7z, .tar, .gz), Temporary (#9E9E9E gray: .tmp, .log, .bak, .cache), System (#9C27B0 purple: .sys, .dat, .ini, .reg), SourceCode (#00BCD4 cyan: .cs, .js, .ts, .py, .java, .cpp, .h, .xaml), Other (#795548 brown: everything else), Folder (#607D8B blue-gray) — expose GetColorHex(string? extension, bool isDirectory) method
- [X] T046 [US4] Update SquarifiedTreemapLayout in src/SpaceMonger.Core/Services/Treemap/SquarifiedTreemapLayout.cs to use FileTypeColorMap for assigning ColorHex to each TreemapNode based on the FileEntry's Extension and IsDirectory fields, replacing any previous placeholder coloring (FR-015)
- [X] T047 [US4] Add color legend to TreemapView in src/SpaceMonger.App/Views/TreemapView.xaml: horizontal or vertical strip showing colored rectangles with category labels (Media, Documents, Executables, Archives, Temporary, System, Source Code, Other, Folder) — use ItemsControl bound to a static list from FileTypeColorMap, position at bottom or side of treemap without obscuring content (FR-015)

**Checkpoint**: Treemap now shows color-coded file types with a visible legend

---

## Phase 7: User Story 5 — Interactive AI Chat for File Questions (Priority: P5)

**Goal**: Users can open a chat panel, ask questions about files with full scan context, click treemap items to set chat context, and get AI responses with copyable command snippets.

**Independent Test**: Scan a drive, open chat, click a system file (e.g., hiberfil.sys) in treemap, ask "What is this?", verify the AI response references the actual file with correct size, provides accurate removal instructions, and command snippets have a Copy button.

### Implementation for User Story 5

- [X] T048 [P] [US5] Create ChatMessage model in src/SpaceMonger.Core/Models/ChatMessage.cs with fields: Id (Guid), Sender (ChatSender), Text (string), Timestamp (DateTime), LinkedEntry (FileEntry?), LinkedRecommendation (CleanupRecommendation?), IsError (bool) per data-model.md
- [X] T049 [US5] Define IChatService interface and implement ChatService in src/SpaceMonger.Core/Services/Chat/ChatService.cs: SendMessageAsync(string userMessage, FileEntry? linkedEntry, CleanupRecommendation? linkedRecommendation, FileEntry currentViewRoot, ScanSession session, string apiKey, CancellationToken ct) — builds messages array with conversation history, constructs context block JSON per llm-api-contract.md Contract 2 (current_view_items from currentViewRoot children, selected_item from linkedEntry/linkedRecommendation, scan_summary), constructs chat system prompt (disk analysis assistant, no command execution, redirect off-topic, cite actual data), calls ILlmClient.SendChatAsync, manages conversation history list, truncates oldest messages if estimated tokens exceed ~150K (FR-032, FR-033, FR-034, FR-035, FR-036)
- [X] T050 [US5] Create CodeBlockControl in src/SpaceMonger.App/Controls/CodeBlockControl.xaml: a UserControl that renders a fenced code block with monospace font background, language label, and a "Copy" button in the top-right corner — Copy button calls Clipboard.SetText with the code text content (FR-037)
- [X] T051 [US5] Implement ChatViewModel in src/SpaceMonger.App/ViewModels/ChatViewModel.cs: Messages (ObservableCollection\<ChatMessage\>), InputText (string), SendCommand (async: creates user ChatMessage, calls IChatService.SendMessageAsync, adds assistant ChatMessage to list), LinkedEntry (FileEntry? set when user clicks treemap before typing), LinkedRecommendation (CleanupRecommendation? set when user clicks recommendation), IsChatAvailable (true when scan is complete, independent of AI analysis per FR-038), IsApiKeyConfigured (from ISettingsService), IsSending (bool for loading state), ErrorMessage (string? for connection errors with retry), conversation history preserved in Messages collection across panel open/close (FR-033, FR-035, FR-038)
- [X] T052 [US5] Create ChatPanel in src/SpaceMonger.App/Views/ChatPanel.xaml: vertical panel — scrollable message list at top (ItemsControl with DataTemplate: user messages right-aligned, assistant messages left-aligned with markdown rendering using CodeBlockControl for code blocks), context indicator bar showing linked item path when set, text input TextBox at bottom with Send button — when API key not configured show centered message "Configure your API key in Settings to use the chat" with a button to open Settings (FR-031, FR-038)
- [X] T053 [US5] Wire chat panel into MainWindow: add Chat toggle button to toolbar, implement Grid column definition that expands/collapses the right column for ChatPanel (collapsed width=0, expanded width=350), treemap column uses star sizing to shrink when chat opens — pass TreemapViewModel.SelectedNode to ChatViewModel.LinkedEntry on treemap click, pass RecommendationsViewModel.SelectedRecommendation to ChatViewModel.LinkedRecommendation (FR-031, FR-033)
- [X] T054 [US5] Register IChatService → ChatService in DI container in App.xaml.cs, wire ChatViewModel into ChatPanel DataContext

**Checkpoint**: All five user stories complete — full application workflow functional

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Edge case handling, UX improvements, and final validation across all stories

- [X] T055 [P] Add keyboard shortcuts in MainWindow: Escape to navigate up in treemap (calls TreemapViewModel.NavigateUp), F5 to trigger rescan (calls MainViewModel.ScanCommand), Ctrl+Shift+A to trigger AI analysis
- [X] T056 [P] Implement loading/progress overlays: scanning progress (file/folder count animation in content area), AI analysis progress (spinner with "Analyzing..." text in RecommendationsPanel area), cleanup progress (progress bar with current item path and X/Y completed count)
- [X] T057 Handle edge cases across all stories per spec edge cases section: empty/nearly-empty drive (treemap renders small items proportionally), files deleted mid-scan (skip without error in FileScanner), inaccessible folders despite admin (show "Access Denied" visual indicator in treemap with distinct hatched pattern), network drive latency (no special handling beyond existing async), re-run analysis confirmation when accepted items exist (confirmation dialog in RecommendationsViewModel per FR-029), no-API-key states for chat and analysis (prompt to open Settings), invalid API key error (clear message directing to Settings), LLM timeout after 60s (notification with retry offer), chat without API key (show message in ChatPanel), off-topic chat redirect (handled by system prompt)
- [X] T058 Run quickstart.md validation: verify all 4 integration scenarios (Scan and Explore, AI Recommendations, Interactive Chat, Duplicate Cleanup) work end-to-end as documented

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **User Stories (Phase 3–7)**: All depend on Foundational phase completion
  - US1 (P1): Can start after Foundational — no dependencies on other stories
  - US2 (P2): Depends on US1 (needs ScanSession with completed scan data to analyze)
  - US3 (P3): Depends on US2 (needs CleanupRecommendation and RecommendationsPanel)
  - US4 (P4): Can start after Foundational — independent of US2/US3 (only needs treemap from US1)
  - US5 (P5): Depends on US2 partially (reuses ILlmClient, ISettingsService), can start after US2 services exist
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

```
Phase 1: Setup
    ↓
Phase 2: Foundational
    ↓
    ├── Phase 3: US1 (Scan & Treemap) ← MVP, start here
    │       ↓
    │   Phase 4: US2 (AI Recommendations) ← needs scan data from US1
    │       ↓
    │   Phase 5: US3 (Cleanup Execution) ← needs recommendations from US2
    │
    ├── Phase 6: US4 (Color Coding) ← independent, only needs treemap from US1
    │
    └── Phase 7: US5 (Chat) ← needs LLM client from US2
            ↓
        Phase 8: Polish
```

### Within Each User Story

- Models before services (data structures must exist before logic)
- Services before UI (business logic must exist before views bind to it)
- ViewModels before Views (bindable state before XAML)
- DI registration last (all types must exist)

### Parallel Opportunities

- **Phase 1**: T002–T007 all run in parallel (different files, no interdependencies)
- **Phase 2**: T009 and T010 run parallel with T008 (different files)
- **Phase 3 (US1)**: T013 and T014 run in parallel (interface + model, different files)
- **Phase 4 (US2)**: T025 and T026 run in parallel (two independent models)
- **Phase 5 (US3)**: T038 can start immediately within phase (model only)
- **Phase 6 (US4)**: T045 can start immediately within phase (service only)
- **Phase 7 (US5)**: T048 can start immediately within phase (model only)
- **Phase 8**: T055 and T056 run in parallel (different concerns)
- **Cross-phase**: US4 (color coding) can run in parallel with US2/US3 if team capacity allows

---

## Parallel Example: User Story 1

```
# Launch models in parallel:
Task: "Define IFileScanner interface in src/SpaceMonger.Core/Services/Scanning/IFileScanner.cs"        [T013]
Task: "Define ITreemapLayoutEngine + TreemapNode in src/SpaceMonger.Core/Services/Treemap/"            [T014]

# Then services (depend on models):
Task: "Implement FileScanner in src/SpaceMonger.Core/Services/Scanning/FileScanner.cs"                 [T015]
Task: "Implement SquarifiedTreemapLayout in src/SpaceMonger.Core/Services/Treemap/"                    [T016]

# Then UI (depends on services):
Task: "Implement TreemapControl in src/SpaceMonger.App/Controls/TreemapControl.cs"                     [T017]
Task: "Implement TreemapViewModel in src/SpaceMonger.App/ViewModels/TreemapViewModel.cs"               [T018]
Task: "Create TreemapView in src/SpaceMonger.App/Views/TreemapView.xaml"                               [T019]
```

## Parallel Example: User Story 2

```
# Launch models in parallel:
Task: "Create AppSettings model in src/SpaceMonger.Core/Models/AppSettings.cs"                         [T025]
Task: "Create CleanupRecommendation model in src/SpaceMonger.Core/Models/CleanupRecommendation.cs"     [T026]

# Then services (sequential due to dependencies):
Task: "Implement SettingsService (DPAPI)"                                                               [T027]
Task: "Implement AnthropicClient (HTTP client)"                                                         [T028]
Task: "Implement DuplicateDetector (xxHash3)"                                                           [T029]
Task: "Implement RecommendationEngine (prompt building + response parsing)"                             [T030]

# Then UI:
Task: "Implement SettingsViewModel + SettingsDialog"                                                    [T031, T032]
Task: "Implement RecommendationsViewModel + RecommendationsPanel"                                       [T034, T035]
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T007)
2. Complete Phase 2: Foundational (T008–T012)
3. Complete Phase 3: User Story 1 (T013–T024)
4. **STOP and VALIDATE**: Scan a real drive, verify treemap, navigation, tooltips, context menu, status bar
5. This delivers a fully usable disk space visualizer without AI features

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 → **MVP: Disk space visualizer** (scan, treemap, navigate)
3. Add US2 → **AI-enhanced: Cleanup recommendations** (analyze, categorize, filter, select)
4. Add US3 → **Full workflow: Automated cleanup** (confirm, execute, auto-update treemap)
5. Add US4 → **Visual polish: Color-coded file types** (type colors, legend)
6. Add US5 → **Power feature: Interactive chat** (ask questions, get contextual answers)
7. Polish → **Production ready: Edge cases, keyboard shortcuts, loading states**

Each increment adds value without breaking previous functionality.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in the same phase
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable after its phase completes
- Commit after each task or logical group of tasks
- Stop at any checkpoint to validate the story independently
- All file paths are relative to repository root (src/, tests/)
- Total: 58 tasks across 8 phases
