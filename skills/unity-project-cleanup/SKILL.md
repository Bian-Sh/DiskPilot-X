---
name: unity-project-cleanup
description: Use when DiskPilot-X must identify Unity projects, analyze Unity Hub evidence, classify cleanup risk for Unity-generated folders such as Library, or explain Unity-specific disk usage.
---

# Unity Project Cleanup Skill

## Purpose
Use this skill when DiskPilot-X is asked to find Unity projects, judge whether a Unity project or its `Library` folder is safe to clean, or explain Unity-specific disk usage.

## Steps

This skill defines the following executable steps for the `DiscoverUnityLibraries` action:

| Step ID | Title (semantic) | Description |
|---------|------------------|-------------|
| `enumerate_drives` | Enumerate ready drives | Detect all mounted, ready drives on the system. |
| `scan_drive:<root>` | Scan drive for Unity projects | Scan a specific drive root (e.g., `C:`, `D:`) to find Unity project markers. One step per ready drive. |
| `merge_unity_hub_inventory` | Merge Unity Hub data | Cross-reference discovered projects with Unity Hub inventory files. |
| `write_unity_recommendations` | Write cleanup recommendations | Generate reviewable cleanup recommendations for discovered Unity Library folders. |

### Step execution contract
- Steps are executed sequentially in the order listed above.
- Each `scan_drive:<root>` step runs once per ready drive detected during `enumerate_drives`.
- A step's status transitions: `idle` → `running` → `finished` (or `idle` on failure).
- The UI spinner animation activates only when a step is in `running` state.
- Steps must be driven by actual execution progress, not keyword detection.

## Prompt design notes
Keep a narrow mission, rank evidence sources, define non-negotiable safety rules, and return structured user-facing decisions instead of vague advice. Do not include machine-local absolute paths in this skill; portable environment paths such as `%APPDATA%\UnityHub\` are allowed when they are evidence sources.

## Localization contract
- User-facing step labels, confirmation cards, and recommendations must follow the app's active response language/localization convention.
- Examples in this skill are semantic examples only; do not hardcode Chinese or English strings when the app has a localized label or response-language setting.
- Prefer internal semantic step IDs such as `scan_drive`, `merge_unity_hub_inventory`, and `write_cleanup_recommendations`, then render localized labels at the UI boundary.

## Current app capability
DiskPilot-X has a read-only registry toolcall named `read_unity_registry_context`. Use it when Unity Hub/editor installation context can affect risk analysis. The tool reads only a fixed allowlist of Unity and Unity Hub registry keys; do not request arbitrary registry paths and do not infer Hub project membership from registry output.

The app can also run a first-class action named `DiscoverUnityLibraries`. This action is not a decorative plan: it enumerates ready disks, scans each disk sequentially without replacing the main TreeView/Treemap scan result, detects Unity project `Library/` folders, and writes reviewable recommendations into the cleanup recommendations panel.

## Step orchestration contract
- Only show workflow steps for work the app or model will actually execute.
- For `DiscoverUnityLibraries`, use concrete semantic steps: `enumerate_drives`, one `scan_drive:<root>` step per ready drive, and `write_unity_recommendations`.
- Step state must be driven by execution progress, not by keyword detection. A Unity keyword alone may select this skill, but it must not mark a step complete unless the corresponding scan/write action completed.
- If a disk is inaccessible or cancelled, mark that disk step failed and continue only when the action implementation reports it can safely continue.
- The final answer must summarize the number of disks scanned, Unity `Library/` folders found, and where the reviewable recommendations were written.

## Evidence hierarchy
1. Explicit user path or current scanned tree path.
2. Unity Hub project inventory files:
   - Windows primary local source: `%APPDATA%\UnityHub\projects-v1.json`.
   - Also inspect `%APPDATA%\UnityHub\projectDir.json` when present.
   - Unity Hub editor inventory/source-of-truth for installed editors: `%APPDATA%\UnityHub\editors.json`.
   - Unity Hub install roots: `%APPDATA%\UnityHub\secondaryInstallPath.json` when present.
3. UnityLauncherPro-compatible evidence:
   - Its project history stores explicit project paths in app settings and validates project folders by checking both `Assets/` and `ProjectSettings/`.
   - Its Unity version detection reads `ProjectSettings/ProjectVersion.txt` first, then falls back to older `ProjectSettings/ProjectSettings.asset`/`Library/AnnotationManager` heuristics.
4. Unity project structure on disk.
5. File timestamps and folder sizes.
6. Registry corroboration through `read_unity_registry_context`.
7. User confirmation.

## Registry corroboration, not Hub project source
Registry data can confirm installed Unity/Hub/editor context, but it should not create or downgrade project evidence by itself.

The `read_unity_registry_context` tool reads these concrete Windows locations if available:
- `HKCU\Software\Unity Technologies\Installer`
- `HKLM\SOFTWARE\Unity Technologies\Installer`
- `HKLM\SOFTWARE\WOW6432Node\Unity Technologies\Installer`
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Unity Hub`
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Unity Hub`
- `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Unity Hub`

Use registry values only for facts such as installed editor versions, install locations, Hub presence, display version, and uninstall metadata. Unity Hub project membership must come from Unity Hub files, explicit app history, or filesystem/project evidence, not from registry keys.

## Unity project identification
Treat a folder as a Unity project only when evidence is strong:
- Required strong markers: `ProjectSettings/ProjectVersion.txt` plus `Assets/`.
- Supporting markers: `Packages/manifest.json`, `Packages/packages-lock.json`, `UserSettings/`, `.csproj` files generated by Unity, or `Library/`.
- Do not classify a folder as a project from `Library/` alone; generated folders may be copied or stale.

## Unity folder safety model
- `Assets/`, `ProjectSettings/`, and `Packages/` are project source/config. Never recommend deleting them as cleanup candidates.
- `Library/` is generated/import cache and is usually rebuildable by Unity, but deletion can cost time and may be risky for active work.
- `Temp/`, `Obj/`, `Logs/`, `.vs/`, `UserSettings/`, IDE generated files, and old build output folders can be candidates, but still require review.
- Any folder inside a Unity Hub project is never `safe` by default; the minimum risk is `medium`.

## Risk scoring
Return one of `high`, `medium`, `low`, or `safe`, but obey these floors:
- Hub-listed project: minimum `medium`.
- Modified or opened recently: `high` when activity is within 30 days; `medium` within 180 days.
- Project with uncommitted-looking source changes or recent `Assets/`, `Packages/`, `ProjectSettings/`: `high`.
- Unknown Unity-looking project not in Hub: minimum `low`; raise to `medium` when recently modified.
- Non-project generated cache outside Hub can be `safe` only when no source/config markers are nearby.

The app must not downgrade a candidate to Safe/Caution from timestamps or missing Hub membership by itself. You, the skill-guided AI, decide the risk from evidence:
- Recent timestamps are caution evidence, not an app policy. Treat recent generated folders as higher risk when they are near Unity source/config markers or Hub-listed projects.
- Missing Unity Hub membership is not proof that a project is abandoned. Treat it as `unknown` evidence and keep at least `low`; raise to `medium` if timestamps, project markers, or path context suggest active work.
- Only call something `safe` when it is generated/cache-like, outside a known project source/config boundary, not Hub-listed, and has no recent activity or source markers nearby.

## Decision procedure
1. Confirm whether scan data already exists for the requested roots.
2. 用户没有给出扫描根时，用 ask_user 确认本次扫描范围；用 scan_path 扫描，再通过 get_analysis_evidence 与树查询核对 Unity 标记，最后调用 publish_recommendations 发布本次扫描内的建议。
3. If analysis would overwrite existing recommendations, use one confirmation card; do not ask in chat whether to create it.
4. Enumerate candidate roots and drives step-by-step using localized UI labels, for example semantic steps like scan C drive, scan D drive, merge Unity Hub data, and write recommended cleanup list.
5. Cross-check Hub inventory before downgrading any Unity project risk.
6. Prefer recommending `Library/` cleanup with risk and rebuild-cost explanation, not automatic deletion.

## Output contract
For each candidate, report:
- `path`
- `projectEvidence`
- `hubListed`: yes/no/unknown
- `lastActivity`
- `risk`
- `reason`
- `recommendedAction`

## Sources to keep in mind
- Unity Manual: special/project folders such as `Assets`, `ProjectSettings`, and generated folders: https://docs.unity3d.com/Manual/SpecialFolders.html
- Unity Manual: project package metadata: https://docs.unity3d.com/Manual/upm-manifestPrj.html
- UnityLauncherPro project detection pattern: project paths are explicit history entries, validated with `Assets/` plus `ProjectSettings/`, and versions are read from `ProjectSettings/ProjectVersion.txt`.
- Local Unity Hub evidence on Windows: `%APPDATA%\UnityHub\projects-v1.json`, `%APPDATA%\UnityHub\projectDir.json`, `%APPDATA%\UnityHub\editors.json`, and `%APPDATA%\UnityHub\secondaryInstallPath.json`.
