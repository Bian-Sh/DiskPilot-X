# Research: Disk Space Analyzer with AI Cleanup Recommendations

**Branch**: `001-disk-space-analyzer` | **Date**: 2026-02-25

## Decision Log

### R-001: UI Framework — WPF on .NET 8

**Decision**: Use Windows Presentation Foundation (WPF) on .NET 8 for the desktop application.

**Rationale**:
- Windows 10/11 exclusive target eliminates cross-platform frameworks
- WPF provides native UI controls (context menus, tooltips, dialogs, split panels) required by the spec
- .NET 8 offers high-performance file system APIs (`Directory.EnumerateFileSystemEntries`, `Channel<T>`)
- Native UAC elevation via application manifest — no workarounds needed
- Mature MVVM ecosystem with CommunityToolkit.Mvvm
- Direct access to Windows APIs (Shell32 for "Open in Explorer", DPAPI for secure key storage)

**Alternatives Considered**:

| Alternative | Why Rejected |
|---|---|
| Electron + React + TypeScript | UAC elevation is complex (requires separate launcher); Node.js file enumeration slower than .NET; ~100MB runtime overhead |
| WinUI 3 / Windows App SDK | Smaller community, fewer third-party controls; treemap visualization ecosystem limited; packaging more complex |
| Avalonia UI | Cross-platform focus unnecessary; smaller ecosystem; less native Windows integration |
| .NET MAUI | Desktop support less mature than WPF; mobile-first design priorities |

---

### R-002: Treemap Rendering — SkiaSharp on WPF

**Decision**: Use SkiaSharp (via `SkiaSharp.Views.WPF`) for custom treemap rendering within an `SKElement` control.

**Rationale**:
- GPU-accelerated 2D rendering handles hundreds of thousands of rectangles efficiently
- Full control over rendering: colors, labels, hover highlights, selection outlines, cushion shading
- SkiaSharp integrates with WPF via `SKElement` (hardware-accelerated surface)
- Custom hit-testing for click/hover/right-click on treemap rectangles
- No dependency on charting libraries — treemap layout is a custom algorithm anyway

**Alternatives Considered**:

| Alternative | Why Rejected |
|---|---|
| WPF Canvas + Rectangle elements | Performance degrades with thousands of UI elements in the visual tree |
| WebView2 + D3.js | Adds web-native bridge complexity; event forwarding between JS and C# adds latency |
| OxyPlot / LiveCharts | No built-in squarified treemap layout; would need custom extension |

---

### R-003: Treemap Algorithm — Squarified Treemap

**Decision**: Implement the squarified treemap algorithm (Bruls, Huizing, van Wijk, 2000).

**Rationale**:
- Produces rectangles with aspect ratios close to 1:1, maximizing readability and click targets
- Industry standard for disk space visualization (SpaceMonger, WinDirStat, Baobab all use variants)
- Well-documented algorithm with O(n log n) time complexity
- Alternating horizontal/vertical splits at each nesting level matches the SpaceMonger visual style
- Simple recursive implementation fits within a single service class

**Algorithm Summary**:
1. Sort children by size (descending)
2. For current layout direction (horizontal or vertical):
   - Add items to current row while worst aspect ratio in the row improves
   - When aspect ratio worsens, finalize row and start new row
3. Recurse into each child folder
4. Alternate layout direction at each depth level

**Alternatives Considered**:

| Alternative | Why Rejected |
|---|---|
| Slice-and-dice | Produces thin, elongated rectangles — poor readability and click targets |
| Strip treemap | Slightly worse aspect ratios than squarified; less common |
| Voronoi treemap | Circular boundaries harder to read; significantly more complex to implement |

---

### R-004: LLM Provider — Anthropic Claude API

**Decision**: Use Anthropic Claude (`claude-sonnet-4-20250514`) as the single LLM provider for AI analysis and chat.

**Rationale**:
- Strong reasoning capabilities for analyzing file metadata patterns and relationships
- 200K token context window accommodates large file metadata payloads
- Good balance of quality and cost for analysis tasks (Sonnet tier)
- Well-documented REST API with straightforward JSON request/response format
- Structured output support for consistent recommendation formatting

**Model Selection**:
- **Recommendations**: `claude-sonnet-4-20250514` — best cost/quality balance for structured analysis
- **Chat**: `claude-sonnet-4-20250514` — same model for consistency

**Alternatives Considered**:

| Alternative | Why Rejected |
|---|---|
| OpenAI GPT-4o | Similar capabilities but higher cost per token for analysis workloads |
| Google Gemini | API less mature; smaller developer community for tooling |
| Local LLM (Ollama) | Insufficient reasoning quality for nuanced file analysis; spec requires cloud LLM |

---

### R-005: File System Scanning — Parallel Enumeration with Channel\<T>

**Decision**: Use .NET 8's `Directory.EnumerateFileSystemEntries` with `System.Threading.Channels.Channel<T>` for producer-consumer scanning.

**Rationale**:
- `EnumerateFileSystemEntries` returns results lazily (streaming) — no memory allocation for full path list
- `Channel<T>` enables non-blocking producer-consumer: scanner writes entries, UI reads progress updates
- `CancellationToken` support for user-initiated scan cancellation
- Handles access-denied errors gracefully by catching per-directory exceptions and continuing
- Symlink/junction detection via `FileAttributes.ReparsePoint` — follow for display but mark as link

**Scanning Strategy**:
1. Start from user-selected root path
2. Enumerate directories breadth-first (allows early display of top-level structure)
3. For each directory, enumerate files and accumulate sizes
4. Build in-memory tree (`FileEntry` parent-child relationships) as items are discovered
5. Report progress via channel: current file count, folder count, current path being scanned
6. On cancellation, stop enumeration and present partial tree

**Performance Target**: 500,000 files in <60 seconds on local SSD (spec SC-001)

**Alternatives Considered**:

| Alternative | Why Rejected |
|---|---|
| `DirectoryInfo.GetFiles(*, AllDirectories)` | Blocks until complete; no streaming progress; single exception aborts entire scan |
| P/Invoke `FindFirstFile`/`FindNextFile` | Marginal performance gains on modern .NET; significantly more complex to maintain |
| `Parallel.ForEach` over directories | Harder to build ordered tree; thread-safety complexity; diminishing returns on SSD |

---

### R-006: Secure API Key Storage — Windows DPAPI

**Decision**: Use Windows Data Protection API (DPAPI) via `System.Security.Cryptography.ProtectedData` to encrypt the API key at rest.

**Rationale**:
- Built into .NET via NuGet package `System.Security.Cryptography.ProtectedData`
- User-scoped encryption — key is tied to the Windows user account
- No external dependencies or key management infrastructure
- Encrypted data stored in a local file (`%APPDATA%\SpaceMonger\settings.dat`)
- Satisfies FR-023: "stored securely on the local machine (not in plaintext config files)"

**Storage Format**:
- Settings file at `%APPDATA%\SpaceMonger\settings.dat`
- API key field encrypted with `DataProtectionScope.CurrentUser`
- Non-sensitive settings (deletion mode, last scan path) stored as plaintext JSON alongside

**Alternatives Considered**:

| Alternative | Why Rejected |
|---|---|
| Windows Credential Manager | 512-byte limit for generic credentials may be restrictive |
| Custom AES encryption | Requires managing an encryption key — turtles all the way down |
| Plaintext config file | Explicitly prohibited by FR-023 |

---

### R-007: Duplicate File Detection — xxHash3

**Decision**: Use xxHash3 (via `System.IO.Hashing.XxHash3`) for content-hash verification of duplicate candidates.

**Rationale**:
- Fastest non-cryptographic hash available in .NET 8 standard library
- ~30 GB/s throughput on modern hardware — negligible overhead even for large files
- Included in .NET 8 runtime (`System.IO.Hashing` namespace) — no external NuGet needed
- Sufficient collision resistance for deduplication (not a security use case)
- Spec requires hybrid approach: metadata match first, then hash only for candidates

**Hashing Strategy**:
1. Group files by (name, size, lastModified) — these are duplicate candidates
2. For groups with 2+ members, compute xxHash3 of full file contents
3. Files with matching hashes within a group are confirmed duplicates
4. Stream files in 8KB chunks to avoid loading entire file into memory

**Alternatives Considered**:

| Alternative | Why Rejected |
|---|---|
| SHA-256 | ~10x slower than xxHash3; cryptographic strength unnecessary for dedup |
| MD5 | Deprecated; slower than xxHash3; known collision vulnerabilities |
| CRC32 | Higher collision rate; xxHash3 is equally fast with better distribution |

---

### R-008: UAC Elevation — Application Manifest

**Decision**: Request administrator privileges via Windows application manifest with `requireAdministrator` execution level.

**Rationale**:
- Standard Windows mechanism for requesting elevated privileges at launch
- Triggers UAC prompt before application starts — if declined, app doesn't launch (per FR-027)
- Embedded in the executable via `app.manifest` file in the WPF project
- No runtime elevation code needed; Windows handles it automatically

**Manifest Configuration**:
```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

**Alternatives Considered**:

| Alternative | Why Rejected |
|---|---|
| Runtime elevation (restart as admin) | Complex; poor UX (app starts, then restarts); race conditions |
| `asInvoker` with fallback | Spec explicitly requires admin on launch (FR-027); partial access defeats purpose |

---

### R-009: Architecture Pattern — MVVM with Dependency Injection

**Decision**: Use MVVM pattern with `CommunityToolkit.Mvvm` and `Microsoft.Extensions.DependencyInjection`.

**Rationale**:
- MVVM is the standard WPF architecture pattern; well-understood and testable
- `CommunityToolkit.Mvvm` provides source generators for `ObservableProperty`, `RelayCommand` — minimal boilerplate
- `Microsoft.Extensions.DependencyInjection` enables constructor injection of services into ViewModels
- Core services (scanning, analysis, cleanup) are injected via interfaces — fully testable with mocks
- ViewModels delegate to services and expose bindable state — no business logic in ViewModels

**DI Registration**:
- Services registered as singletons (one scanner, one LLM client per app lifecycle)
- ViewModels registered as transient (created fresh when needed)
- `IHttpClientFactory` configured for LLM API calls with timeout and retry policies

**Alternatives Considered**:

| Alternative | Why Rejected |
|---|---|
| ReactiveUI | Steeper learning curve; reactive programming paradigm overkill for this complexity level |
| Prism | Heavyweight framework with navigation, regions, modules — over-engineered for single-window app |
| No DI (manual construction) | Harder to test; tight coupling between ViewModels and services |

---

### R-010: Testing Framework — xUnit + NSubstitute + FluentAssertions

**Decision**: Use xUnit as the test framework, NSubstitute for mocking, and FluentAssertions for readable assertions.

**Rationale**:
- xUnit is the most popular .NET test framework with excellent .NET 8 support
- NSubstitute has the cleanest syntax for mocking service interfaces
- FluentAssertions provides readable, expressive assertions aligned with spec acceptance criteria
- Separate test projects: `SpaceMonger.Core.Tests` (unit) and `SpaceMonger.App.Tests` (integration)

**Alternatives Considered**:

| Alternative | Why Rejected |
|---|---|
| NUnit | Similar capabilities but xUnit has better parallel test execution defaults |
| Moq | Functional but NSubstitute syntax is more concise for interface mocking |
| MSTest | Less community support; fewer assertion helpers |
