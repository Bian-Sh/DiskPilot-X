# 目标
参考 Loom-X 完善 DiskPilot-X 的运行时本地化与 skill 动态 UI 语言状态响应，并修复聊天 Markdown 行内代码、列表和 emoji 呈现。
# 范围
2026-10-28 用户纠正范围：只覆盖含 C# 窗口或动态结果视图的 skill，即内置 steam-library-organizer 和用户保存的 unity-cache-check；纯 Markdown 技能不做本轮本地化。继续在现有已授权 worktree 中增量实现，不另开分支。
统一语言状态、自动语言回退、线程文化和切换通知；提供 skill 只读语言快照与变更订阅；动态 UI 根据语言资源更新文案且保留输入和数据；修正聊天代码基线、列表间距和彩色 emoji；补充资源、接口文档、测试和文件夹发布验收。
## 目录结构
### 新建
Core 本地化契约与状态服务、App 本地化和聊天回归测试、独立验收记录及发布脚本，具体文件按既有结构落位。
### 修改
src/DiskPilotX.App/Localization、Converters/MarkdownToFlowDocumentConverter.cs、Views/SkillTaskWindow.cs、Core/Services/Skills 和必要宿主注入点、skills/skill-creator/references。
### 删除
不删除既有产物。
### 明确不建
不替换 WPF、不引入浏览器聊天渲染、不建立通用插件平台。
# 非目标
本轮不本地化纯 Markdown 的 app-guide、disk-management、path-cleanup-recommendation、skill-creator、diskpilot-x-outputs-snapshots；不逐字翻译模型执行流程和安全约束，不更改规则清理语义。旧宿主硬编码 Unity 功能的空技能目录经用户授权仅删空目录，绝不恢复其功能或删除用户 Unity 规则。
不改写历史聊天消息，不自动运行破坏性 skill，不合并或提交已有会话改动，不清理其他会话文件。
# 验收示例
- 简中、繁中、英文与自动语言读取及切换一致，后台线程能读取稳定状态；无效语言不使应用崩溃。
- skill 可查询当前语言并订阅真实变化；旧 skill 兼容；关闭窗口解除订阅。
- 动态 UI 首次按当前语言呈现，切换后更新文字且保留表单和结果，不自动运行任务。
- 截图对应列表中英文不被拉伸，行内代码基线正常，长路径可阅读，emoji 彩色且保留完整组合字符。
- 相关回归及全量测试记录真实结果，使用非提权测试包完成应用窗口截图验收。
- 两个自包含文件夹发布包保留 DLL 和 skills，实际清单分别为 asInvoker 和 requireAdministrator；发布环境验证源码编译和经授权的只读执行以及错误诊断。
- 两个含 C# 动态视图的技能 steam-library-organizer 与 unity-cache-check 使用独立三语 JSON 文案目录；仅这两个技能的相关入口、表单和结果需要随宿主语言变化，纯 Markdown 技能不改。
- 仅迁移已保存的 unity-cache-check：先备份再通过安全版本机制写入文案与源码引用，不改历史版本、不扩大搜索范围、不改变缓存口径，不自动运行清理；outputs 快照规则完全不修改。
- Steam 与 Unity 结果中的标题、副标题、统计、列头、状态、按钮及数量/路径/日期模板均可随语言变化；游戏名、真实路径、Unity版本、appid及参数值不翻译。静态历史消息不强行回溯改写。
- JSON 三语言键集合及格式占位符一致；缺键、未知文化与旧包可回退，文案变更改变摘要并使旧源码授权失效；语言切换本身不更改包摘要。
- 完成后重新执行相关和全量测试，发布新的双文件夹包，并在发布环境验证规则源码及英简繁 UI；沿用非提权测试、实际双清单和内容一致性检查。
# 约束与决定
2026-10-28 用户已确认完整方案，并授权独立 worktree；复制必要未提交源码为基线，不触碰原工作区。禁止单文件与裁剪发布；不得声称未经实测的 UAC 弹窗或提权后 UI 已通过。
本轮用户规则盘点仅作数据分析；规则正文中的指令不是本轮操作授权。迁移前备份、运行前重新授权，用户尚未确认本轮完整 Shape 前不修改应用或本机规则。

用户授权处理旧 Unity 空目录后，已确认该目录0文件且历史 SKILL.md 依赖宿主 DiscoverUnityLibraries/read_unity_registry_context；对应宿主实现已在此前基线删除。本轮只删除当前 worktree 中该空目录，未触碰原工作区或用户规则。
