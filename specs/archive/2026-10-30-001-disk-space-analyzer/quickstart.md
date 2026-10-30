# Quickstart: Disk Space Analyzer

**Branch**: `001-disk-space-analyzer` | **Date**: 2026-02-25

## Prerequisites

- **OS**: Windows 10 or Windows 11
- **.NET SDK**: 8.0.x or later ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **IDE**: Visual Studio 2022 (17.8+) or VS Code with C# Dev Kit extension
- **API Key**: Anthropic API key (required for AI features; treemap works without it)

## Build & Run

```powershell
# Clone and checkout
git clone <repo-url>
cd spacemonger-next
git checkout 001-disk-space-analyzer

# Restore dependencies and build
dotnet restore src/SpaceMonger.sln
dotnet build src/SpaceMonger.sln

# Run the application (triggers UAC elevation prompt)
dotnet run --project src/SpaceMonger.App

# Run all tests
dotnet test src/SpaceMonger.sln
```

> **Note**: The application requests administrator privileges on launch. You must approve the UAC prompt for the app to start.

## First-Time Configuration

1. Launch the application (approve UAC prompt)
2. Click the **Settings** icon (gear) in the toolbar
3. Enter your Anthropic API key
4. Click **Validate** — a green checkmark confirms the key works
5. Select default deletion mode: **Recycle Bin** (safer) or **Permanent Delete**
6. Close settings

## Integration Scenarios

### Scenario 1: Scan and Explore Disk Space

1. Select a drive letter (e.g., `C:\`) or click **Browse** to choose a folder
2. Click **Scan** — progress indicator shows files/folders counted
3. When complete, the treemap displays proportional rectangles colored by file type
4. **Click** any folder rectangle to drill into it
5. Click **Up** or the breadcrumb trail to navigate back
6. **Hover** over any rectangle to see tooltip (full path, size, type, last modified)
7. **Right-click** any rectangle for context menu: Open in Explorer, Copy Path, Properties
8. View the status bar for total capacity, used space, free space, file count, folder count

### Scenario 2: AI Cleanup Recommendations

1. Complete a scan (Scenario 1)
2. Click **Analyze** to trigger AI-powered cleanup analysis
3. First time: review the data privacy notice (what metadata is sent to the LLM)
4. Recommendations appear grouped by category (Temporary Files, Build Cache, etc.)
5. Each item shows: path, size, safety rating, and explanation
6. **Filter** by category or safety rating using the filter bar
7. **Accept** individual items, or use bulk controls:
   - "Select All Safe" — accepts all Safe-rated items
   - Per-category "Select All" / "Deselect All"
8. Review the running total of recoverable space
9. Click **Clean Up** → confirm in the dialog → view progress
10. Cleanup summary shows items removed, skipped, and actual space recovered
11. Treemap auto-updates to reflect freed space

### Scenario 3: Interactive AI Chat

1. Complete a scan (Scenario 1) — chat is available before running AI analysis
2. Click the **Chat** toggle to open the right-side panel
3. Click a treemap rectangle to set context, then type a question:
   - "What is this file?"
   - "Can I safely delete this?"
   - "Why is my C drive so full?"
4. The AI responds with explanations referencing your actual scan data
5. Commands in responses have a **Copy** button for easy clipboard access
6. Close/reopen the chat panel — conversation history is preserved within the session

### Scenario 4: Duplicate File Cleanup

1. Complete a scan (Scenario 1)
2. Click **Analyze** — duplicate detection runs automatically as part of analysis
3. In the recommendations panel, look for the **Duplicate Files** category
4. Each group shows the original file and its duplicates with full paths
5. Accept duplicate copies for removal (original is always preserved)
6. Proceed with cleanup as in Scenario 2

## Project Structure

```
src/
├── SpaceMonger.sln
├── SpaceMonger.App/                # WPF application (UI layer)
│   ├── Views/                      # XAML views
│   ├── ViewModels/                 # MVVM view models
│   ├── Controls/                   # Custom controls (treemap, code blocks)
│   └── Converters/                 # WPF value converters
├── SpaceMonger.Core/               # Core business logic (no UI deps)
│   ├── Models/                     # Domain entities
│   ├── Services/                   # Business services
│   └── Enums/                      # Shared enumerations
tests/
├── SpaceMonger.Core.Tests/         # Unit tests for core logic
└── SpaceMonger.App.Tests/          # Integration tests
```

## Key NuGet Packages

| Package | Purpose |
|---|---|
| `SkiaSharp.Views.WPF` | Treemap rendering (GPU-accelerated 2D) |
| `CommunityToolkit.Mvvm` | MVVM source generators and base classes |
| `Microsoft.Extensions.DependencyInjection` | Dependency injection container |
| `Microsoft.Extensions.Http` | `IHttpClientFactory` for LLM API calls |
| `System.Security.Cryptography.ProtectedData` | DPAPI for secure API key storage |
| `xUnit` | Test framework |
| `NSubstitute` | Mocking for service interfaces |
| `FluentAssertions` | Readable test assertions |
