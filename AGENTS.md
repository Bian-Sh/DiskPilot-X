# AGENTS.md

## 应用打包与验收规则（优先遵循）

以下规则针对实际的 DiskPilot-X .NET/WPF 应用；与下方历史模板描述冲突时，以实际代码及本节为准。

### 禁止单文件发布

- 2026-10-28 的 Steam 库盘点故障：发布包将程序集打进单个 EXE，而 `CleanupSkillSourceService` 使用 Roslyn 从磁盘读取 .NET 与宿主契约程序集，导致运行时源码编译失败。这是打包兼容性问题，不应要求用户安装 SDK，也不能以改扫目录代替修复。
- 测试包和用户交付包均必须使用文件夹发布，显式指定 `PublishSingleFile=false`、`PublishTrimmed=false`、`SelfContained=true`；不要采用单文件自解压作为替代方案。
- 保留发布目录中的运行时 DLL、`DiskPilotX.Core.dll`、其他依赖及 `skills/`；交付完整文件夹或其压缩包，禁止只交付 EXE。
- 每次修改应用后重新打包，放入 `outputs/<可读时间>_<用途>/`，用途明确区分测试包与用户交付包；禁止覆盖或清理非本次任务产物。

### 测试包：不请求提权

- 供 `cua-driver` / `cua` 自动化验收的测试包必须使用 `requestedExecutionLevel level="asInvoker"`，启动时不请求 UAC 提权；从非提权进程启动，并核实实际进程未提权，避免自动化工具因权限等级不同无法访问应用。
- 测试时用 `Start-Process -FilePath <绝对 exe 路径> -WindowStyle Hidden` 启动，并检查进程 `Path`；只截取应用窗口，不截全屏。

### 用户交付包：自动请求 UAC 提权

- 交付用户的 EXE 必须内嵌 `requestedExecutionLevel level="requireAdministrator"` 的应用清单，用户正常双击启动时由 Windows 自动请求 UAC 提权。
- 不得以 `highestAvailable`、`asInvoker` 或“让用户右键以管理员身份运行”代替上述要求；也不能仅在打包者启动时使用 `-Verb RunAs`，却让交付 EXE 本身仍不请求提权。
- 测试包与用户交付包应通过不同清单或构建参数明确区分；测试用的非提权清单不能泄漏到交付包，不要依赖手动来回改写同一个清单。

### 发布前必验

- 开发环境单元测试通过不等于发布包可用。必须在真实文件夹发布环境中验证 `validate_skill_source` 编译和经授权的 `run_skill_source` 执行，覆盖 Steam 库盘点或等价源码规则，并检查失败诊断是否完整。
- 分别检查两个包内 EXE 的实际应用清单：测试包为 `asInvoker`，用户交付包为 `requireAdministrator`；不能仅根据构建命令或源清单宣称验证通过。
- 使用非提权测试包完成自动化 UI 验收；用户交付包须另外核实提权清单及发布内容一致性。未经实际验证，不得宣称用户交付包的 UAC 或提权后 UI 已验收。

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Project Overview

This is a **Spec-Driven Development (SDD) framework** called "Specify". It is not a traditional application — it is a meta-project template that provides Codex commands, templates, and PowerShell scripts for AI-assisted specification, planning, and implementation workflows.

There is no source code to build, no tests to run, and no package manager. The project consists entirely of Codex commands, markdown templates, and PowerShell automation scripts.

## Architecture

### Core Components

- **`.Codex/commands/`** — Nine speckit slash commands that drive the workflow
- **`.specify/templates/`** — Markdown templates for specs, plans, tasks, checklists, and constitutions
- **`.specify/scripts/powershell/`** — PowerShell automation (branch creation, prerequisite checks, plan setup, agent context updates)
- **`.specify/memory/constitution.md`** — Project governance template (non-negotiable principles)

### Workflow Sequence

The commands form a pipeline executed in order:

1. **`/speckit.specify`** — Create feature spec from natural language description. Creates a numbered branch (`NNN-short-name`), initializes `specs/<branch>/spec.md`, and generates a requirements checklist.
2. **`/speckit.clarify`** — Resolve ambiguities in the spec (max 5 targeted questions).
3. **`/speckit.plan`** — Generate technical plan (`plan.md`), research decisions (`research.md`), data model (`data-model.md`), and interface contracts (`contracts/`).
4. **`/speckit.tasks`** — Generate dependency-ordered `tasks.md` organized by user story phases.
5. **`/speckit.analyze`** — Read-only cross-artifact consistency analysis. Never modifies files.
6. **`/speckit.implement`** — Execute tasks phase-by-phase following TDD approach.

Supporting commands: `/speckit.constitution` (manage governance), `/speckit.checklist` (custom validation checklists), `/speckit.taskstoissues` (convert tasks to GitHub issues).

### Feature Directory Structure

Each feature lives in `specs/<NNN-short-name>/` and accumulates artifacts:

```
specs/001-feature-name/
├── spec.md              # Feature specification (what & why)
├── plan.md              # Technical implementation plan (how)
├── research.md          # Technical decisions and rationale
├── data-model.md        # Entity definitions and relationships
├── contracts/           # Interface contracts (APIs, CLIs, etc.)
├── tasks.md             # Dependency-ordered implementation tasks
├── quickstart.md        # Integration scenarios
└── checklists/          # Validation checklists
    └── requirements.md
```

### Key Scripts

All scripts are PowerShell (cross-platform via `pwsh`):

- **`create-new-feature.ps1`** — Creates numbered branch and feature directory. Auto-detects next number from branches and specs dirs. Supports `-Json`, `-ShortName`, `-Number` flags.
- **`check-prerequisites.ps1`** — Validates feature directory structure. Flags: `-Json`, `-RequireTasks`, `-IncludeTasks`, `-PathsOnly`.
- **`setup-plan.ps1`** — Initializes planning workflow, copies plan template.
- **`update-agent-context.ps1`** — Syncs agent-specific context files. Use `-AgentType Codex`.

### Conventions

- **Branch naming**: `NNN-short-name` (e.g., `001-user-auth`). Numbers are zero-padded to 3 digits.
- **Task IDs**: Sequential `T001`, `T002`, etc. Tasks use strict checklist format: `- [ ] [T001] [P?] [US?] Description with file path`
- **`[P]` marker**: Indicates parallelizable tasks (different files, no dependencies).
- **User story labels**: `[US1]`, `[US2]` etc. map to priority-ordered stories from spec.
- **Phase order**: Setup → Foundational → User Stories (P1, P2, P3...) → Polish
- **Specs are tech-agnostic**: `spec.md` describes WHAT and WHY, never HOW. No frameworks, languages, or APIs in specs.
- **Constitution is non-negotiable**: Violations are always CRITICAL severity in analysis. Changes require explicit constitution updates.
- **Max 3 `[NEEDS CLARIFICATION]` markers** per spec, prioritized by: scope > security > UX > technical details.
