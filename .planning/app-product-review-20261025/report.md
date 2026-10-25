# SpaceMonger Copilot：产品能力评估与建议

日期：2026-10-25。评估基于主目录、Git 注册的三个 worktree、现有 CodeGraph 索引、实际源码与 Comet 记录。以下为建议，没有实施产品变更，也没有重跑已有验收。

## 判断

现有底座已经包括快速扫描、增量刷新、Treemap、文件树、原生 AI 工具调用、建议列表和确认清理。下一阶段的价值在于把这些能力连接为连续操作：用户指出对象，AI 引用证据，用户在原视图核对，清理后验证效果，日后能够比较变化。

## 主目录与 worktree 现状

| 位置 | 实际内容 | 本次检查时的状态 | 对下一阶段的影响 |
| --- | --- | --- | --- |
| 主目录 | .NET 10/WPF、SkiaSharp、TinyHarness，扫描/查询/导航/清理工具 | 主干底座；含其他会话未提交文档及忽略规则修改 | 基于现有工具补交互，无需重写 Agent |
| conversation-history | 本地 JSON 会话快照、历史列表、恢复模型与 UI 消息、异常恢复 | 有源码及测试；Comet build，验收 pending | 加入聊天附件时应同时设计可序列化对象引用 |
| treemap-wiztree | 单击选中、标题双击进入、颜色/边界/悬停设计与新测试 | Comet build，验收 pending；主要交互源码尚未在此次差异中出现 | 先解决选择语义，再做图与聊天的稳定联动 |
| cleanup-skill-creator | Skill 包、资料来源、规则试扫、可选 C#、受约束 XAML、askuser、动态界面样式 | Comet verify、await-user，verification_result pass；验收记录为 A1–A9 通过 | 扩展底座已较丰富，优先打磨发现、预览、解释和复用体验 |

四处 HEAD 相同，功能主要位于未提交文件中，不能只比较提交日志。三个 worktree 均纳入本次评估，但没有合并或接管它们。

## 第一优先：对象进入会话的完整体验

当前两个右键菜单都已有“询问 AI”：TreemapView.xaml.cs:145、TreeViewControl.xaml.cs:304。MainWindow.ViewModels.cs:161 的处理器设置单个 LinkedEntry，覆盖输入框问题并聚焦，没有发送。

另一个关键点是 MainWindow.ViewModels.cs:192 随 Treemap 选择改写 LinkedEntry，而 AssistantWorkspace 的工具读取实时工作区。这样，“浏览中选中的对象”“准备发送的附件”“这轮问题引用的对象”容易混在一起。

建议交付：

1. 两视图共享“加入当前 AI 会话”“在新会话中分析”“分析清理风险”入口，选中已有历史会话后也可加入。
2. “加入当前会话”在输入框上方追加可移除的对象卡片，保留原草稿，不自动触发模型请求。另一个明确的“分析”操作可发送预设问题。
3. 附件展示类型、完整路径、扫描时大小和时间；先支持一个稳定附件，再按使用需求扩展多个对象和拖拽。
4. 发送时固定本轮对象引用，包括路径、类型、scan_id 与采集时间。导航和选中变化只更新工作区，不悄悄改写已发送消息。
5. 历史恢复保留可展示的对象描述；重新扫描、路径移动或文件消失时标记待刷新。重新执行任何操作仍由宿主重新验证。
6. AI 引用的对象卡片可定位 Treemap、定位文件树、打开资源管理器，并能将真实候选加入建议列表。

最小验收例：正在输入“这两个目录分别是什么”，依次加入对象不会丢失草稿；发送后继续浏览其他目录，AI 这一轮仍回答最初附件；重启恢复后能看见引用，无法复用旧删除授权。

## 第二优先：让 AI 回答能操作、能核对

现有 navigate_path、publish_recommendations 与 cleanup_recommendations 已能支撑闭环。建议在回复中增加原生对象/候选卡片，包含观测数据、依据、可能影响、恢复代价和不确定项，并提供定位和加入清理清单的按钮。

“可以删”需要回答为什么、将影响什么、证据来自哪个对象和哪次扫描。不要把模型文本中的路径直接当作执行依据；操作始终经过现有宿主的扫描范围、白名单、保护路径与清单版本检查。

完成单对象分析后，允许用户保存为已有 Skill Creator 能接受的清理规则。例如“每次只清 Unity 可重建缓存，保留构建产物和下载资料”。优先复用 cleanup-skill-creator，不另建平行规则系统。

## 第三优先：空间变化与清理效果

现有 USN 增量扫描回答扫描效率问题，但面向用户的历史基线与变化对比需要单独建设。

首期保存用户选定范围的轻量目录统计快照：记录采样范围、时间、完成/取消/拒绝访问状态和统计口径，再比较同一范围的新增、减少和消失对象。先做目录级 Top N 与手动保存基线，无需默认长期保存整盘文件明细或启动常驻监控。

可带来的新问题包括：“C 盘这周为什么少了 20 GB？”“哪个项目增长最快？”“昨天清理的缓存今天又长回来多少？”AI 读取本地确定性算出的差异，解释来源并给出下一步核查。

清理报告同时区分候选逻辑大小、扫描统计减少量、卷可用空间变化，以及失败/跳过项。不要将这些数值都叫作实际回收空间。默认可回收站方案也不能承诺立即释放对应容量。

## 关于“桌面图表”和桌面图标

如果指统计图表，建议先提供一个轻量“空间概览”，放在现有主工作区：

- 容量条：已用、可用及本次扫描覆盖范围；不能用部分目录扫描量冒充整卷占用。
- 横向排行：Top 目录/文件类型，可点击筛选或定位，也能加入 AI 会话。
- 文件年龄分布：按最后修改时间统计，注明这不等于多久未使用。
- 变化排行：只在存在可比较快照时显示。

图表聚合由本地完成，不调用模型计算总量；AI 负责解释和交互。现有 FileEntry 与 ScanSession 已提供多数基础字段，复用现有 SkiaSharp 即可先做静态及可点击图形，暂时没有必要引入通用 BI 平台。

如果原意是“桌面图标”，当前也缺少完整实现：App 项目未配置 ApplicationIcon，主窗口未设置 Icon，未找到专用 ico 和桌面快捷方式创建流程。建议一个小任务完成 exe、任务栏、窗口与快捷方式的视觉统一，并在发布安装/首次配置中提供桌面和开始菜单入口。

## 后续值得做的三个小方向

1. 重复文件工作流。现有 DuplicateDetector 先按文件名、大小、修改时间分组，再计算哈希，会漏掉改名副本。可改为大小分组、分阶段哈希，UI 允许选择保留位置，并沿用清理确认；继续跳过云占位文件，避免检测诱发下载。
2. 应用/项目视角。复用 Unity Skill 的路径识别经验，把构建缓存、包缓存、日志按项目或应用归属聚合，显示再生代价与正在使用的风险。识别不确定时明确标记，不扩大到整机应用管理器。
3. 会话质量与发布收口。显示当前范围、真实失败原因及实际可获得的模型用量；增加一套可选真实 Provider 的小型验收。统一包版本、提交/变更来源、发布时间与功能清单，避免多个 worktree 的独立包让人误判功能丢失。

## 建议交付顺序

第一批：右键对象附件 + 保留草稿 + 聊天结果定位 + 图标/快捷方式。沿用现有 Treemap 和会话历史的工作，避免两边反复冲突。

第二批：空间概览 + 清理前后核验 + 用户主动保存统计基线。

第三批：历史差异的 AI 解释 + Skill 从单次分析中复用 + 重复文件/项目归属的确定性检测。

暂不优先增加另一个 Agent 框架、向量数据库、后台常驻监控或开放式任意插件平台。当前最有价值的增量是让已有能力在同一窗口内连续工作。

## 核心证据位置

- 主目录：src/SpaceMonger.App/MainWindow.ViewModels.cs、MainWindow.Copilot.cs、Views/TreemapView.xaml.cs、Views/TreeViewControl.xaml.cs、ViewModels/ChatViewModel.cs。
- 模型与宿主：src/SpaceMonger.Core/Services/Assistant/DiskAssistantService.cs、AssistantWorkspace.cs、Services/Agent/AgentContext.cs。
- 数据与检测：src/SpaceMonger.Core/Models/FileEntry.cs、ScanSession.cs、Services/Analysis/DuplicateDetector.cs。
- 会话历史 worktree：docs/conversation-history/design.md、ConversationStore.cs、ChatViewModel.History.cs 以及对应 Comet 状态。
- Treemap worktree：docs/comet/changes/treemap-wiztree/brief.md、comet-state.yaml 和 TreemapControlTests.cs。
- Skill Creator worktree：docs/design/2026-10-25-cleanup-skill-creator-design.md、docs/cleanup-skill-creator/验收记录.md、Services/Skills 与 CleanupSkillTool.cs。

备注：AGENTS.md 还将工程描述为无源码的 Specify 模板，建议单独更正项目概览和构建/验收说明。此次保留全部其他会话产物，没有进行清理。
