# DiskPilot-X

A modern disk space analyzer for Windows inspired by the legendary [SpaceMonger](https://github.com/seanofw/spacemonger1), with AI-powered cleanup recommendations.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4) ![Windows](https://img.shields.io/badge/platform-Windows-0078D6) ![License](https://img.shields.io/badge/license-MIT-green)

## AI 协作核心

AI 继续位于右侧面板，磁盘扫描、Treemap、文件树和推荐列表仍是主工作区。会话由仓库内固定版本的 Tiny Harness 驱动，使用原生流式工具调用，工具结果返回同一轮 Agent。

- 可以直接指定路径，让 Copilot 扫描、查询空间、切换视图并定位目录。
- 手动分析与对话共用推荐发布能力，路径、大小、范围、白名单和系统保护由本地服务核验。
- 右侧展示有序正文及可折叠工具过程，保留技能提及、上下文关联和发送/停止。
- 清理必须确认具体清单与删除方式；确认期间清单改变会失效。
- 继续使用现有加密 API Key、Anthropic 兼容 Base URL、聊天/分析模型和思考设置。

手动分析复用相同运行时与工具，但原始分析历史与对话隔离；更改 AI 对话排除规则后，会刷新后续模型上下文，已有界面消息仍可在本地查看。

构建需要 .NET 10 SDK；无需检出或安装原 Loom-X 仓库。设计与验收见 docs/ai-first-loomx/。Windows 发布包放在 outputs 下带上海时间的目录中。

## About

DiskPilot-X is a spiritual successor to [SpaceMonger](https://www.werkema.com/programming/the-spacemonger-1-x-post-mortem/), the brilliant treemap-based disk space visualizer created by **Sean Werkema** in 1997. The original SpaceMonger — written in a single afternoon and posted online by a college student — became one of the most beloved Windows utilities of its era. Its genius was in its simplicity: one glance at the treemap and you could instantly see where your disk space went. Even decades later, people still swear by it.

DiskPilot-X carries that same philosophy forward — instant visual clarity about disk usage — and adds AI-powered analysis to help you decide what's safe to clean up.

*Treemap view of ProgramData — each rectangle is proportional to its size on disk. Colors distinguish folders at a glance, and you can click any rectangle to drill into that directory.*

![Treemap visualization of ProgramData](docs/screenshots/ui.png)

*AI-powered cleanup in action — the right panel shows a chat conversation asking what can be safely deleted, with categorized recommendations and safety ratings. The bottom pane lists specific folders the AI flagged for cleanup, each with a size, safety rating (Safe, Review First, or Caution), and a description explaining why. Select items and click Clean Up to delete them in one step.*

![AI chat and cleanup recommendations](docs/screenshots/ui_chat.png)

**This project is not affiliated with or endorsed by Sean Werkema, Sixty-Five Software, Stardock, or EdgeRunner LLC.** It is an independent open-source reimagining built from scratch as a tribute to the original.

## Features

- **Treemap visualization** — squarified layout with the classic SpaceMonger color palette, proportional rectangles, and drill-down navigation
- **Free space indicator** — drive-level view shows free space as a distinct block so you see the full picture
- **AI cleanup recommendations** — sends file metadata to Claude and returns categorized suggestions with safety ratings (Safe / Review First / Caution)
- **Content-aware analysis** — the AI inspects file types, dates, and names inside ambiguous folders (like a folder named "Temp" that's actually full of important documents) instead of judging by name alone
- **Interactive AI chat** — ask questions about your disk usage with streaming token output
- **One-click cleanup** — select recommendations, confirm, and delete with automatic treemap refresh
- **Protected paths** — never recommends deleting OS files, system directories, or user document folders
- **Fast NTFS scanning** — enumerates the Master File Table in a single sequential pass for directory structure, then collects file sizes in parallel across all CPU cores
- **Near-instant rescans** — uses the NTFS USN change journal to detect what changed since the last scan, applying only the deltas instead of re-reading the entire file system
- **Automatic fallback** — non-NTFS volumes (FAT32, exFAT, network drives) fall back to a standard directory walk transparently

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or later)
- Windows 10 or Windows 11
- An [Anthropic API key](https://console.anthropic.com/) for AI features (optional — scanning and visualization work without it)

> **Cost note:** AI features (cleanup recommendations and chat) use the Anthropic Messages API, which is billed per token. There is no free tier or subscription-based OAuth option — Anthropic closed the third-party OAuth route, so API key with pay-per-use billing is the only supported authentication method.

## Installation

```bash
git clone https://github.com/Bian-Sh/DiskPilot-X.git
cd DiskPilot-X
```

## Build

```bash
cd src
dotnet build DiskPilotX.sln
```

## Run

```bash
cd src
dotnet run --project DiskPilotX.App
```

> 应用默认以普通用户权限运行；受限目录可能无法读取，扫描结果会据实反映可访问范围。

## Run Tests

```bash
cd src
dotnet test DiskPilotX.sln
```

## Configuration

### 更名兼容

程序与工程已更名为 DiskPilot-X / DiskPilotX。设置、历史对话和自定义清理规则优先使用 `DiskPilot-X` 数据目录；新版对应文件或目录不存在时，继续使用旧版 `SpaceMonger` 数据，不自动搬移或覆盖。旧版 `SPACEMONGER_*` 环境变量仍兼容，新版 `DISKPILOT_X_*` 同名配置优先。

### Anthropic base URL override

By default the app uses `https://api.anthropic.com`. To point DiskPilot-X at another Anthropic-compatible endpoint, enter an Anthropic Base URL in **Settings**. Leave the field empty to use the built-in endpoint. Environment variables are still supported as a fallback before launching the app:

- `DISKPILOT_X_ANTHROPIC_BASE_URL` — preferred override for this app
- `ANTHROPIC_BASE_URL` — fallback override for compatibility

Settings and environment variable values must be absolute `http://` or `https://` URLs. The app falls back to the official Anthropic base URL when the Settings field is empty and neither variable is set.
### Localization

DiskPilot-X keeps English source strings in `src/DiskPilotX.App/Localization/Strings.resx` and Simplified Chinese translations in `Strings.zh-CN.resx`. The app follows the Windows UI culture by default; set `DISKPILOT_X_LANGUAGE=zh-CN` before launching to force Chinese.

After pulling upstream changes, run:

```bash
python scripts/sync-localization.py --check
```

This verifies resource keys stay aligned and reports likely hard-coded UI strings that need to be moved into the resource files.

## Usage

### Scanning

1. Select a drive or folder from the dropdown, type/paste a path, or click **Browse...** (which starts scanning immediately)
2. Click **Scan** to start — on NTFS drives, the MFT is read first (file/folder counts climb rapidly), then file sizes are collected in parallel
3. The treemap fills in when the scan completes, with free space shown as an off-white block at the drive level
4. Subsequent scans of the same path use the USN change journal and complete in under a second
5. Click any folder rectangle to drill in, click **Up** or press **Escape** to go back
6. Clicking **Scan** while drilled into a subfolder rescans just that folder

### AI Cleanup Recommendations

1. Enter your Anthropic API key in **Settings** (gear icon)
2. Click **Recommend Cleanup** — the AI analyzes your scan and returns items it thinks you can safely remove
3. Recommendations are grouped by category (Temporary Files, Build Cache, Package Manager Cache, etc.) and rated by safety
4. Check items to select them, then click **Clean Up** to delete

> The AI only returns items it recommends for deletion. Files and folders it considers important are intentionally omitted from the list.

### Chat

1. Click **Chat** to open the side panel
2. Ask questions about your disk usage — the AI sees your current treemap view and any selected item
3. Responses stream in token-by-token

## Project Structure

```
src/
├── DiskPilotX.App/           # WPF application (UI layer)
│   ├── Controls/             # TreemapControl (SkiaSharp rendering)
│   ├── Converters/           # Value converters for XAML bindings
│   ├── ViewModels/           # MVVM view models
│   └── Views/                # XAML views (Chat, Recommendations, Settings, Treemap)
├── DiskPilotX.Core/          # Core library (no UI dependencies)
│   ├── Enums/                # Safety ratings, categories, etc.
│   ├── Models/               # FileEntry, TreemapNode, ScanSession, etc.
│   └── Services/
│       ├── Analysis/         # AI recommendation engine with content fingerprinting
│       ├── Chat/             # AI chat service
│       ├── Cleanup/          # File deletion service
│       ├── Llm/              # Anthropic API client (streaming + non-streaming)
│       ├── Scanning/         # File system scanner (MFT enumeration, parallel size collection, USN journal incremental rescans)
│       ├── Settings/         # API key and app settings persistence
│       └── Treemap/          # Squarified treemap layout algorithm
└── DiskPilotX.sln
tests/
├── DiskPilotX.App.Tests/
└── DiskPilotX.Core.Tests/
```

## Technical Notes

- **File scanner** on NTFS volumes uses `FSCTL_ENUM_USN_DATA` to read the Master File Table in one sequential pass (~3 seconds for 2M files), then fills in file sizes via parallel `FileSystemEnumerable<T>` calls across all CPU cores. Non-NTFS volumes fall back to a serial `FindFirstFile`/`FindNextFile` walk
- **Incremental rescans** read the NTFS USN change journal to detect creates, deletes, renames, and size changes since the last scan, applying only the deltas — typically sub-second for a previously-scanned volume
- **Cloud placeholder files** (OneDrive Files On-Demand) are detected and report 0 bytes to avoid triggering unwanted downloads during scan
- **Treemap layout** uses the squarified algorithm with adaptive depth — small directories become solid blocks rather than rendering unreadable children
- **AI metadata** is sent as a compact pipe-delimited format (~5-10x smaller than JSON) to maximize the data that fits within token limits
- **Content fingerprints** attached to ambiguously-named directories include file type distribution, date ranges, user-content percentage, and sample file names so the AI can make informed safety judgments

## Acknowledgments

This project exists because of the original **SpaceMonger** by [Sean Werkema](https://www.werkema.com/). SpaceMonger 1.0 proved that a great idea, well executed, can outlast decades of technology change. The treemap visualization concept, the color palette, the drill-down navigation, and the philosophy of "do one thing and do it well" all trace directly back to Sean's work.

- [SpaceMonger 1.4 source code](https://github.com/seanofw/spacemonger1) — preserved on GitHub for posterity
- [The SpaceMonger 1.x Post-Mortem](https://www.werkema.com/programming/the-spacemonger-1-x-post-mortem/) — Sean's own account of building SpaceMonger
- [Sean Werkema's Blog](https://www.werkema.com/) — includes additional SpaceMonger retrospectives

## License

MIT
