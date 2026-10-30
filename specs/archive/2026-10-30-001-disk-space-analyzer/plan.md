# Implementation Plan: Disk Space Analyzer with AI Cleanup Recommendations

**Branch**: `001-disk-space-analyzer` | **Date**: 2026-02-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-disk-space-analyzer/spec.md`

## Summary

Build a Windows desktop application (WPF/.NET 8) that scans drives/folders, visualizes disk usage as an interactive squarified treemap (SkiaSharp rendering), provides AI-powered cleanup recommendations via the Anthropic Claude API, executes user-approved file deletions, and offers an interactive chat panel for file system Q&A. The application runs with administrator privileges for full disk access and stores the user's API key securely via Windows DPAPI.

## Technical Context

**Language/Version**: C# / .NET 8.0 (LTS)
**Primary Dependencies**: SkiaSharp.Views.WPF, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Http, System.Security.Cryptography.ProtectedData
**Storage**: Ephemeral in-memory (scan data, recommendations, chat); DPAPI-encrypted settings file at `%APPDATA%\SpaceMonger\settings.dat`
**Testing**: xUnit + NSubstitute + FluentAssertions
**Target Platform**: Windows 10/11 desktop (x64)
**Project Type**: Desktop application (WPF)
**Performance Goals**: 500K-file scan in <60s (SC-001); AI recommendations in <30s (SC-002); treemap rendering at interactive frame rates
**Constraints**: Requires administrator privileges (UAC on launch); requires internet for AI features; single LLM provider (Anthropic Claude)
**Scale/Scope**: Single user, local machine, ~5 primary views (treemap, recommendations panel, chat panel, settings dialog, cleanup dialog)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Status**: PASS — No constitution has been ratified for this project. The constitution file (`/.specify/memory/constitution.md`) contains only template placeholders with no defined principles or gates. No violations are possible.

**Post-Phase 1 Re-check**: PASS — Same assessment. When a constitution is ratified, future planning iterations should re-evaluate.

## Project Structure

### Documentation (this feature)

```text
specs/001-disk-space-analyzer/
├── plan.md              # This file
├── research.md          # Technology decisions and rationale
├── data-model.md        # Entity definitions and relationships
├── quickstart.md        # Build, run, and integration scenarios
├── contracts/
│   └── llm-api-contract.md  # Anthropic Claude API integration contract
└── tasks.md             # Implementation tasks (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── SpaceMonger.sln                         # Solution file
│
├── SpaceMonger.App/                        # WPF application (UI layer)
│   ├── SpaceMonger.App.csproj
│   ├── App.xaml
│   ├── App.xaml.cs                         # DI container setup, app startup
│   ├── app.manifest                        # UAC requireAdministrator
│   ├── Views/
│   │   ├── MainWindow.xaml                 # Shell: toolbar, treemap, status bar, chat toggle
│   │   ├── TreemapView.xaml               # SkiaSharp treemap host + breadcrumb nav
│   │   ├── RecommendationsPanel.xaml      # Categorized recommendation list with filters
│   │   ├── ChatPanel.xaml                 # Right-side collapsible chat panel
│   │   ├── SettingsDialog.xaml            # API key entry, deletion mode, validation
│   │   ├── CleanupConfirmDialog.xaml      # Pre-cleanup confirmation with summary
│   │   └── CleanupSummaryDialog.xaml      # Post-cleanup results
│   ├── ViewModels/
│   │   ├── MainViewModel.cs               # App-level state, scan orchestration
│   │   ├── TreemapViewModel.cs            # Treemap navigation, selection, hover
│   │   ├── RecommendationsViewModel.cs    # Recommendation list, filters, bulk actions
│   │   ├── ChatViewModel.cs               # Chat messages, context linking, send/receive
│   │   └── SettingsViewModel.cs           # API key management, validation
│   ├── Controls/
│   │   ├── TreemapControl.cs              # SkiaSharp custom control (render + hit-test)
│   │   └── CodeBlockControl.xaml          # Markdown code block with Copy button
│   └── Converters/
│       ├── FileSizeConverter.cs            # bytes → human-readable (KB, MB, GB)
│       └── SafetyRatingToBrushConverter.cs # Safe=green, ReviewFirst=yellow, Caution=red
│
├── SpaceMonger.Core/                       # Core business logic (no UI dependencies)
│   ├── SpaceMonger.Core.csproj
│   ├── Models/
│   │   ├── FileEntry.cs
│   │   ├── ScanSession.cs
│   │   ├── CleanupRecommendation.cs
│   │   ├── CleanupAction.cs
│   │   ├── ChatMessage.cs
│   │   └── AppSettings.cs
│   ├── Services/
│   │   ├── Scanning/
│   │   │   ├── IFileScanner.cs
│   │   │   └── FileScanner.cs             # Channel<T>-based async file enumeration
│   │   ├── Treemap/
│   │   │   ├── ITreemapLayoutEngine.cs
│   │   │   └── SquarifiedTreemapLayout.cs # Squarified treemap algorithm
│   │   ├── Analysis/
│   │   │   ├── IRecommendationEngine.cs
│   │   │   ├── RecommendationEngine.cs    # Metadata selection, LLM prompt, response parsing
│   │   │   ├── IDuplicateDetector.cs
│   │   │   └── DuplicateDetector.cs       # Metadata match + xxHash3 verification
│   │   ├── Cleanup/
│   │   │   ├── ICleanupService.cs
│   │   │   └── CleanupService.cs          # Delete/recycle with error handling
│   │   ├── Chat/
│   │   │   ├── IChatService.cs
│   │   │   └── ChatService.cs             # Context injection, conversation management
│   │   ├── Llm/
│   │   │   ├── ILlmClient.cs
│   │   │   └── AnthropicClient.cs         # HTTP client for Anthropic Messages API
│   │   └── Settings/
│   │       ├── ISettingsService.cs
│   │       └── SettingsService.cs         # DPAPI encryption, file persistence
│   └── Enums/
│       ├── SafetyRating.cs
│       ├── RecommendationCategory.cs
│       ├── DeletionMode.cs
│       ├── CleanupResult.cs
│       └── ChatSender.cs
│
tests/
├── SpaceMonger.Core.Tests/                 # Unit tests
│   ├── SpaceMonger.Core.Tests.csproj
│   ├── Services/
│   │   ├── FileScannerTests.cs
│   │   ├── SquarifiedTreemapLayoutTests.cs
│   │   ├── DuplicateDetectorTests.cs
│   │   ├── RecommendationEngineTests.cs
│   │   ├── CleanupServiceTests.cs
│   │   ├── ChatServiceTests.cs
│   │   ├── AnthropicClientTests.cs
│   │   └── SettingsServiceTests.cs
│   └── Models/
│       └── FileEntryTests.cs
└── SpaceMonger.App.Tests/                  # Integration / ViewModel tests
    ├── SpaceMonger.App.Tests.csproj
    └── ViewModels/
        ├── MainViewModelTests.cs
        └── RecommendationsViewModelTests.cs
```

**Structure Decision**: Two-project solution (App + Core) with MVVM separation. `SpaceMonger.Core` contains all business logic and has zero UI dependencies — it can be tested independently. `SpaceMonger.App` references Core and adds WPF views, view models, and controls. This keeps the solution simple (no micro-service overhead) while maintaining clean testability boundaries.

## Complexity Tracking

> No constitution violations to justify — constitution has not been ratified.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| N/A | — | — |
