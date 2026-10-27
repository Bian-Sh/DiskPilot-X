# 分析编排设计与协作边界

日期：2026-10-27。状态：设计草案；所有新增名称都是拟议实现，不表示代码已存在。产品要求见 [spec.md](spec.md)。

## 一、代码基线与问题证据

| 现有位置 | 当前事实 | 本次处理方向 |
|---|---|---|
| src/DiskPilotX.App/MainWindow.Console.cs | 分析按钮代发应用消息，传递 focusPath | 保留入口，绑定分析任务而非关键词路由 |
| src/DiskPilotX.App/ViewModels/ChatViewModel.cs | SendAppAnalysisAsync 传 Skills: []，共享对话会话 | 装配任务上下文、分析流程指引与动态目录摘要 |
| src/DiskPilotX.Core/Services/Assistant/DiskAssistantService.cs | IsAnalysis 仅保留八个工具，排除规则读取、ask_user 等 | 分析侧工具按操作和参数最小开放 |
| src/DiskPilotX.Core/Services/Skills/CleanupSkillModels.cs | 有 Rules、Markdown、References、Version、Digest，无专用适用条件和阶段字段 | 只读适配现有契约；不改这个共享文件 |
| src/DiskPilotX.Core/Services/Agent/CleanupSkillTool.cs | 提供 list/read/preview 等，也包含 save 等非只读能力 | 不直接把整套操作开放给分析 |
| src/DiskPilotX.Core/Models/ScanSession.cs | 无来源及任务目标 | 加可选、向后兼容的来源摘要关联 |
| src/DiskPilotX.Core/Services/Assistant/ConversationStore.cs | 已有本地会话快照、历史展示及隐私签名 | 不改创建模块；增加分析侧受限上下文读取 |
| src/DiskPilotX.Core/Services/Analysis/RecommendationEngine.cs | 本地候选校验；保护等过滤并非无候选 | 保留保护，在边界返回可理解的校验摘要 |
| src/DiskPilotX.App/ViewModels/RecommendationsViewModel.cs | 有等待及诊断信息，但不足以表达所有终态 | 使用统一的任务状态与结果原因 |

图谱：主工作区现有 CodeGraph 可定位上述类型；新工作树尚未初始化自己的图谱，不复制其他会话数据库。当前规格以本工作树实际源码为准。

## 二、最小结构

不建立新的 Agent，也不重复实现规则仓库。由既有 DiskAssistantService 装配小型分析侧组件：

- AnalysisTaskContext：分析任务、当前意图、扫描引用和必要上下文。
- AnalysisRuleCatalog：包装既有规则目录与读取 API，提供摘要与阶段视图；只读。
- AnalysisTaskResult：已接受建议、覆盖情况、未评估内容、过滤原因及终态。
- AnalysisContextStore：扫描来源摘要、可控偏好、可选行为记录；普通本地文件，不上向量库。

优先单个轻量类／记录与现有依赖注入；只有出现真实替换需求才新增接口，不为了未来扩展建立多层工厂。

## 三、只读规则消费契约

### 目录与新鲜度

每轮开始读取目录形成该轮快照，正文按需读取。规范化字段为 id、title、description、version、digest、sourceKind、availableStages、applicabilityHints 和 requiresTrustedSource；这些是分析侧 DTO，不是要求创建模块现在新增的包字段。

- 结构化规则复用 CleanupSkillStore 的现有只读能力。
- Markdown 技能复用现有 IAiSkillRouter／ISkillPromptProvider 读取能力。
- 统一按稳定 id 和来源识别；遇到同名但来源不明确时报告冲突，不静默覆盖。沿用既有仓库已明确的用户／内置优先级。
- 当前字段不能提供的信息保持 unknown。Rules.Path 只是候选线索，不等于完整适用性；不能由缺失推导“不适用”。
- 元数据将来由创建模块提供后，在适配器中接入。当前不修改包格式、创建／编辑工具或用户规则文件。

### 阶段读取

阶段逻辑名为 discovery、analysis、handling、constraints。分析阶段读取 analysis 并始终附带 constraints；需要定位才读取 discovery。既有格式不具备可靠分段时返回 legacy-full，限量读取全文并明确读取状态，不用猜测章节伪造阶段。分析侧支持明确选择的 Markdown 契约：只有正文声明 <!-- diskpilot-analysis-stages:v1 --> 且包含 discovery、analysis、handling、constraints 四个明确二级章节时，才读取目标阶段并附加全局说明与 constraints；作者必须在约束节完整记录保留条件、恢复代价与风险。没有声明、缺少章节或未知全局部分均不得被静默省略。这个可选契约不修改规则包格式或创建模块，也不迁移既有规则。

目录摘要与正文均设体积预算；超出预算必须分页、继续读取或标记未覆盖，不能把截断当成已经读完。相关规则版本在接受结果前核对；变化则仅重做受影响部分。

### 匹配

先使用明确结构线索和排除条件筛选，再让 AI 用本机证据核实。不设置“文件名命中即安全”的捷径。没有可靠元数据时按摘要挑选并读取相关旧规则，无法选择时继续通用分析或在重要歧义处询问。

## 四、工具与授权边界

拟议分析专用操作：

| 能力 | 允许参数 | 明确拒绝 |
|---|---|---|
| 发现规则 | 无磁盘越界参数的目录摘要 | 通过 list 顺带写规则 |
| 读取规则 | id、实际版本、stage、分页 | 任意本地文件路径、伪造版本 |
| 预览规则 | 已保存规则 id、当前范围内的 scope | 草稿 package、源码执行、越界、扩展用户范围 |
| 询问用户 | 既有结构化 ask_user 字段 | 伪造已确认、索取凭据 |
| 公开资料检索 | 公开主题、必要公开网址 | 私密路径、文件树、用户内容或凭据 |
| 读取历史上下文 | 本次扫描绑定摘要；经许可的关联消息 | 任意全量会话枚举与回放、旧确认复用 |
| 发布建议 | 当前扫描／任务、核实过的候选 | 删除、越权清理及规则写操作 |

优先注册分析专用小 schema 和受限适配函数，不将 manage_cleanup_skill 的完整 schema 放进分析模式。既有 preview 会实际遍历文件系统且仅按用户 scope 约束，不能假定它自动遵守所有分析白名单：调用前必须核对整个子树，内部排除无法保证时拒绝这次预览并回退扫描证据查询；返回候选再次核验。

网络检索只使用当前配置模型的真实能力；失败、无结果和不支持分别记录，不扩展到另一个模型或自行传本机证据。安全保护、重解析点约束、版本确认和可信源码授权仍由宿主掌握。

## 五、任务结果与列表状态

将生命周期和结论分开，避免用一个布尔值推断：

- 生命周期：NotStarted、Running、WaitingForUser、Completed、Failed、Cancelled。
- 结果类型：Recommendations、NoRecommendations、Partial、Restricted、NeedsInput；没有结果时为 unknown。
- 覆盖记录：已检查范围／规则、跳过或读取失败的部分、用户决定、校验／发布限制。

完整、无未知且无推荐时才允许 NoRecommendations。只要有重要未知或失败，不能靠空数组进入这个状态；硬保护全部过滤应表示 Restricted 或带限制的 Partial。Failed／Cancelled 不抹去此前已完成结果的归属，也不使旧结果看起来属于新任务。

校验摘要至少包含输入、接受、保护过滤、白名单过滤、未扫描／越界、重叠去重、格式无效、发布状态。按可见范围返回原因，不能泄漏已排除路径。原因来自实际校验，不由 AI 推测；区分核心校验与宿主二次过滤，防止后者改变结果却无说明。

模型获得写作目标和真实结果，不获得固定话术模板。中文／英文界面状态文案仍正常本地化；这不等于固定模型总结句。

## 六、用户参与与创建模块交接

复用聊天中的 ask_user 卡片，避免另建阻塞系统弹窗。只为重要歧义停下受影响的部分，其余可继续。

选择“创建规则”只是创建意向。通过已有创建入口交接原始任务标识、范围与未知点；不能把原始文件内容当作创作指令。若创建入口尚未提供可恢复交接能力，分析侧保存暂停点并说明限制，用户可先继续通用检查或跳过；不能自行实现新的 creator。

后续从同一分析恢复时先验证 scanId、scope、privacySignature 与相关规则版本；对新的扫描重新获取事实，不把创建成功等同于分析已完成。用户取消或拒绝则停止对应动作，不在同轮再次提问。

## 七、扫描来源、历史与上下文

拟议 ScanProvenance 包含 scanId、source（manual/chat/rule）、可选 conversationId／messageId／ruleId 和版本、目标摘要、明确范围及保留条件。它属于本次成功扫描；来源不可从当前聊天反推到旧扫描。AnalysisTaskContext 另有当前分析目标，不覆盖扫描原始目标。

任务摘要存于独立的本地上下文文件，由 scanId 和 taskId 关联；文件树继续使用现有扫描机制，不复制进记忆。只持久化必要信息，原子写入并可删除，不保存密钥、原始工具大输出或模型思考。

默认先读取宿主生成的必要摘要；用户补充内容按数据对待。跨会话原文访问需有效授权。摘要带来源引用、隐私签名和可见性信息；每次注入核对来源是否仍允许访问。来源不存在时只保留不含其受限内容的扫描事实，不读取残留原文。单纯 conversationId 关联不赋予读取或执行权限。

旧 ScanSession 和 ConversationSnapshot 无新字段时给 unknown，不为它们编造来源；仅后续新事件补充真实关联。历史读取无效不阻止基于当前扫描做通用分析。

## 八、轻量记忆和行为建议

复用一个小型本地存储，逻辑上分开三类记录：任务摘要、用户确认偏好、用户启用后的行为线索。键、范围、来源、创建／更新时间和失效信息保持可读；不建设通用知识图谱或向量检索。

- 偏好：只有明确保存或确认后写入；可在聊天中查看、更正、删除和停用。当前要求可临时覆盖偏好，但不静默重写。
- 通用主题：本轮核实证据后可通过 report_task_topics 报告公开数据类型，不依赖已读规则或显式技能；校验短ID且每轮清空，成功结束才累计。未启用时不收集，不保存路径、账号或私有项目名；分析未读证据时拒绝报告。
- 行为：默认关闭，启用后仅记规范化主题 id、动作类型、时间及成功状态；不能保留原始路径来凑主题。
- 建议：第一版以同主题、同类动作近 30 天至少 3 次成功使用为默认阈值；同主题 30 天最多提示一次，暂时拒绝冷却 30 天，“不再提醒”持续到用户重新启用。这些是可测试的产品默认值，不是固定模型台词。
- 没有启用行为记录时，不从落盘历史偷偷统计频率。当前会话可在用户直接表达重复需求时提出一次建议，不声称已有长期统计。
- 只在本轮主任务有可交付结论后提出建议；失败、取消、待回答阶段不提出。查询、分析与清理分开，不能把常查询改成自动清理预设。
- 接受建议仍转交现有创建模块；未创建成功前不新增快捷入口。

## 九、并行协作边界

### 可计划修改的既有位置

- src/DiskPilotX.Core/Services/Assistant/DiskAssistantService.cs、AssistantWorkspace.cs：任务装配、最小工具权限及结果传播。
- src/DiskPilotX.Core/Models/ScanSession.cs、AnalysisDiagnostics.cs：向后兼容的可选关联及真实诊断。
- src/DiskPilotX.Core/Services/Analysis/RecommendationEngine.cs 及相关诊断：只补充校验原因，不放宽保护策略。
- src/DiskPilotX.App/MainWindow.Console.cs、MainWindow.Copilot.cs、ViewModels/ChatViewModel.cs、MainViewModel.cs、RecommendationsViewModel.cs：入口、成功扫描关联、状态和交接。
- src/DiskPilotX.App/Views/RecommendationsPanel.xaml 及其代码隐藏文件：呈现完整、部分、受限和取消状态，复用现有推荐界面。
- skills/path-cleanup-recommendation/SKILL.md、skills/disk-management/SKILL.md：分析目标、发现流程与结果表达，不预设所有清理规则。
- 对应 Core／App 测试及 Localization/Strings*.resx。

### 本次明确不改

- skills/skill-creator/、src/DiskPilotX.Core/Services/Agent/ManageDiskSkillsTool.cs、CleanupSkillTool.cs。
- src/DiskPilotX.Core/Services/Skills/ 下的创建／存储／格式／源码／面板实现，包括 CleanupSkillModels.cs 和 CleanupSkillStore.cs。
- src/DiskPilotX.Core/Services/Copilot/FileSkillPromptProvider.cs、AiSkillRouter.cs 及共享接口，不为自动路由重构创建模块。
- 其他会话正式规格、状态、工作树、outputs、work 和 .planning 产物。

新增类型放在分析侧目录。若现有只读接口不足，采用分析侧适配和明确的能力缺失状态；确需改上述禁改范围时暂停该项并与用户协调，不先行修改。

## 十、方案取舍与验证

采用动态目录 + 小型适配器 + 宿主约束，避免全量规则提示词、关键词硬路由或新的自动 Agent。保留现有创建模块和删除链路的权限边界。

第一阶段优先补可重放的脚本模型测试、范围／过滤诊断与 UI 状态测试。第二阶段补偏好持久化、关闭／清除、频率和拒绝冷却测试。测试时固定时间源；不依赖真实电脑缓存或外网来决定通过。

使用既有 .NET 10 测试项目。新增源码后检查完整方案测试；界面阶段使用隔离设置／对话／测试目录，cua-driver 后台验收，Start-Process -WindowStyle Hidden 启动并校验进程 Path，仅截取应用。最后发布 outputs/年-月-日_时-分-秒_分析规则编排，不覆盖或清理既有输出。

当前已进入 Build。落地采用 AnalysisTaskContext、AnalysisRuleCatalog、AnalysisContextStore 及服务 partial 分拆；阶段读取对无明确契约的旧格式保守返回 legacy-full，明确契约支持分段且保护条件独立保留，正文分页绑定 digest。发布确认后、列表写入前执行条件令牌检查；任务级过滤事实不被后续空发布抹除。授权前只读取 ConversationMetadata 侧文件，原始关联消息仍需宿主确认。实际测试、后台应用与包路径记在本次 .planning 记录中，不把计划或旧候选检查当作最终通过。
