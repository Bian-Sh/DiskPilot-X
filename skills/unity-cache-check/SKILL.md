---
name: unity-cache-check
description: "Unity 项目缓存盘点：Unity 项目可能分散在任意磁盘，不能只看 Unity Hub 的项目列表。搜索范围由任务面板决定——roots 填一个或多个绝对路径（分号分隔），depth 控制向下层数（1-8，默认 6）；面板输入为空或无效时只返回空结果并说明原因，不会自动扩大到全部磁盘。逐层查找 Unity 项目（同时存在 Assets 与 ProjectSettings），只统计 Library / Temp / obj / Logs 四类缓存，展示项目数、缓存合计、按磁盘分布与逐项目明细（含 Unity 版本），明细行可勾选后交回分析。只读扫描，不删除任何文件。"
---

# Unity 项目缓存盘点

## Purpose

帮用户搞清楚「这台机器上散落在各个磁盘的 Unity 项目到底占了多少空间、哪个盘的缓存最该清」，并给出可勾选的明细，勾选结果交回助手继续做清理分析。

**为什么不用 Unity Hub 的项目列表**：Hub 只记录它自己打开过的项目，项目被移动、从别的机器拷过来、或者直接用编辑器打开过的，Hub 里根本没有。所以判定项目的唯一依据是磁盘上的目录结构，而不是任何客户端清单。

**面向小白用户**：用户不需要知道项目结构长什么样、哪几个子目录是缓存。这些判定全部由 `src/Provider.cs` 完成；对话里只问人话（「要搜哪几个目录 / 哪个盘」）。

## 任务面板（ui.xaml）

技能自带可重开的任务面板，`show_skill_panel` 发布任务卡，用户点开填写参数再预览：

| 字段 | 含义 | 规则 |
|---|---|---|
| `roots` | 搜索根目录，多个用 `;` 分隔 | 必须是绝对路径；相对路径、不存在、无权限的会被逐条跳过并记录原因 |
| `depth` | 从每个根目录向下搜索的层数 | 1–8，默认 6；非数字按默认 6，超界按边界值并提示 |

面板还要求一个有效的本机范围（`scope_path`，宿主补充）。它只在 `roots` 未填出有效目录时**作为唯一的兜底根目录**使用，仍属用户自己填写的位置。

**范围规则（用户明确要求）**：目录完全由面板决定。`roots` 为空、全是相对路径、或全部不存在时，规则**直接返回空结果并列出被跳过的原因**，绝不静默退化成「扫描本机全部磁盘」。最多接受 32 个根目录，超出会提示按前 32 个执行。

## 只读边界

源码以当前用户权限运行，**不是安全沙箱**，但严格只读：

- ✅ 枚举目录（`context.Host.EnumerateDirectories`）
- ✅ 读文本文件（`ProjectSettings/ProjectVersion.txt`）
- ✅ 统计目录大小（`context.Host.GetDirectorySize`）
- ❌ 不写文件、不删文件、不启进程、不联网、不枚举用户没指定的盘符

删除永远不在这里发生：用户勾选回传后，助手走 `analyze_paths` → `publish_recommendations`，用户在清理建议清单里确认才会执行。

## 发现步骤

1. **逐层向下找项目**
   从每个根目录开始按层向下遍历，`depth` 控制层数。
   命中项目后**不再往项目内部递归**——`Library` 等目录巨大且不会嵌套项目。
   为控制耗时，跳过系统与无意义的目录：`Windows`、`Program Files*`、`ProgramData`、`AppData`、`$Recycle.Bin`、`System Volume Information`、`Recovery`、`PerfLogs`、`WindowsApps`、`node_modules`、`.git`、`.vs`、`.idea`、`Library`、`Temp`、`obj`、`Logs`、`Build(s)`、`MemoryCaptures`、`Recordings`、`steamapps`，以及 Unity 编辑器安装目录（路径含 `\\Unity\\Hub`）。
   目录检索总量有上限（20 万个目录），触顶时如实告知「结果可能不完整」，不假装扫全了。

2. **项目判定（唯一依据）**
   目录里**同时存在 `Assets` 和 `ProjectSettings`** 才判定为 Unity 项目。两者缺一不算，避免把普通文件夹误报成项目。

3. **缓存口径（用户确认过的白名单）**
   只统计这四类：`Library/`、`Temp/`、`obj/`、`Logs/`。
   **明确排除**：`Build/` `Builds/`（可能是要保留的成品包）、`MemoryCaptures/` `Recordings/`（录屏与快照，用户文件）、`.vs/` `.idea/` `.vscode/`（IDE 配置）、`Assets/` `Packages/` `ProjectSettings/`（项目本体，永不计入）。

4. **Unity 版本**
   读 `ProjectSettings/ProjectVersion.txt` 的 `m_EditorVersion`，读不到就如实显示「未读到」，不猜。

5. **声明视图**
   返回 `AppDataDiscoveryResult(entries, view)`，view 含：
   - `stat_row`：Unity 项目数 / 缓存合计 / 占用最大项目 / 覆盖磁盘数
   - `table`「按磁盘分布」：磁盘 / 项目数 / 缓存合计 / 占用最大项目
   - `table`「项目明细」：项目 / 所在盘 / Unity 版本 / 缓存合计 / 缓存构成，每行 `selectable`
   - `text`「输入检查」：仅在根目录被跳过、`depth` 被修正时出现，如实说明
   - 动作：`continue`（把勾选项交回分析）+ `close`
   根目录无效时改为一张 `text` 视图，明确要求回到面板补充目录。

## 勾选回传之后

1. 用户勾选若干项目 → 点底部「把勾选的项目交回分析」。
2. 助手拿到的是项目路径，**必须一次性**对所有路径调用一次 `analyze_paths`（最多 6 个），再一次性 `publish_recommendations`，不要逐个发布（会覆盖清单）。
3. 候选的 `path` 取自证据里真实存在的缓存子目录（如 `<项目>/Library`），不得编造。
4. 分类建议：`Library`/`Temp`/`obj` → `BuildCache`，`Logs` → `LogFiles`；`Library` 可重建但重建代价高（重新导入全部资源），安全等级用 `ReviewFirst` 而不是 `Safe`。

## 使用方式

- 保存后宿主会提示授权；运行前展示完整源码与包摘要。
- 面板任务卡：先填 `roots` / `depth`，再点预览，结果以原生视图呈现（气泡里只有一张入口卡）。
- 也可以在对话里直接带参数运行（`roots` 多个用分号分隔）。**任何情况下都不会自动扩大到全部磁盘。**

## 约定

- 不硬编码任何本机项目路径；一律来自面板输入。
- 读不到的信息（版本号、缓存大小、被跳过的目录）如实标注，不编造。
- 只读，绝不删除；删除只走宿主建议清单确认链路。
