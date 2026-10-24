# 目标

让 AI 成为 SpaceMonger Copilot 的磁盘分析协作与任务编排核心：使用 LoomX.Harness 驱动原生工具调用、实时输出和连续多步任务。保留以磁盘分析为中心的主界面，AI 继续位于右侧面板。

# 范围

- 产品布局：左侧/中央维持路径、扫描、Treemap、文件树与推荐区域；右侧 Copilot 面板继续支持开合、调宽、关联当前选择。
- AI 运行时：从用户指定的 Loom-X 工作区引入固定版本的 LoomX.Harness 源码，迁移到 net10.0-windows，使用 AgentLoop、AgentSession、ToolRegistry 和 AgentEvent。构建不依赖原工作区的绝对路径。
- 工具闭环：AI 能读取当前状态、解析路径、启动/等待扫描、查询文件树、切换 Treemap/文件树视图并定位路径、生成并发布清理建议、操作建议选择、在确认后执行具体清理清单。每次操作结果返回同一轮 Agent。
- 推荐链路：手动分析与 AI 分析共用 Harness 和结果发布/安全校验能力；保留已有元数据提取、系统路径保护及白名单语义，消除聊天与独立推荐模型流程之间的割裂。
- 聊天界面：在 WPF 中实现 LoomX 风格的消息排版、按发生顺序排列的正文与可折叠过程、工具状态与详情、底部统一输入卡、模型标识及发送/停止；保留技能提及和上下文关联。
- 模型接入：保留现有 API Key、Anthropic Base URL、聊天/分析模型与思考设置，迁移为 Harness 的原生 streaming/tool calling 客户端；两种任务都实际使用配置的模型。
- 错误与取消：停止传播至模型与正在执行的操作，支持失败后下一条消息继续；同一时间只执行一个会修改扫描/建议状态的任务。
- 验证交付：自动化测试、原生 UI 验收、Release 发布包，包放入 outputs 下带上海时间的可读目录。

## 目录结构

### 新建

- src/LoomX.Harness/ — 指定 Harness 的固定版本源码与工程，保留命名空间并记录来源和校验信息。
- src/SpaceMonger.Core/Services/Assistant/ — 会话门面、原生模型客户端、工具注册与事件契约，复用现有核心服务。
- src/SpaceMonger.App/Services/Copilot/ — 现有目录内补充统一磁盘操作适配与用户确认连接，供手动入口和工具共用。
- tests/SpaceMonger.Core.Tests/、tests/SpaceMonger.App.Tests/ — 现有测试工程内补充 Harness 适配、工具闭环和消息投影测试。
- docs/ai-first-loomx/design.md — 架构、工具约定、UI tokens 和验证设计。

### 修改

- src/SpaceMonger.sln、src/SpaceMonger.Core/SpaceMonger.Core.csproj、src/SpaceMonger.App/SpaceMonger.App.csproj、tests 下两个 csproj — 添加 Harness 依赖并统一目标框架。
- src/SpaceMonger.App/App.xaml.cs — 依赖注入与服务生命周期。
- src/SpaceMonger.App/MainWindow.Copilot.cs、MainWindow.Cleanup.cs 及必要的导航/视图连接 — 统一操作结果、并发控制和 UI 更新。
- src/SpaceMonger.App/ViewModels/ChatViewModel*.cs、RecommendationsViewModel.cs、src/SpaceMonger.Core/Models/ChatMessage.cs — 替换旧编排，按事件展示真实运行状态。
- src/SpaceMonger.App/Views/ChatPanel.xaml 及代码后置、MainWindow.xaml、Themes/VisionProTheme.xaml、Services/ThemeManager.cs 及主题模型、Localization/ — 右侧面板和必要的整体视觉收敛。
- src/SpaceMonger.Core/Services/Analysis/、Agent/、Chat/、Llm/ 与相关测试 — 拆除旧模型编排，复用确定性的查询、推荐安全校验与内容分析逻辑。
- README.md — 更新构建、运行及 AI 使用说明。

### 删除

- 新链路替换完成后，删除旧 AgentRuntime 的文本 JSON 工具协议、ChatService 的延迟分段播放，以及失去调用者的旧提案/流程协调代码；具体删除文件由实际引用和回归验证确定。

### 明确不建

- 不迁移 Avalonia，不引入 LoomX 的网关、插件宿主、浏览器和凭据数据库。
- 不建立通用工作流引擎，不并行运行两套 Agent，不依赖原 Loom-X 绝对路径构建。

# 非目标

- AI 对话不成为默认首页，不把 Treemap/文件树收进聊天内部。
- 本次不增加通用 Shell、任意文件编辑、后台自主清理、跨项目插件系统或多会话历史管理产品。
- 不扩展新的 Provider 协议产品；优先把当前 Anthropic 兼容接入的原生工具链路做完整。
- 不改扫描算法和 Treemap 布局算法，不重做与本次交互无关的设置页面。
- 不自动提交、推送、合并、清理其他会话产物或修改 Loom-X 仓库。

# 约束与不变量

- 用户于 2026-10-24 明确：AI 对话不是默认主界面；磁盘能力要可由 AI 调用；AI 继续放在右侧面板。
- 扫描白名单、推荐白名单、AI 对话白名单分别保持当前代码语义；每次工具读取和发送模型上下文均应用相应规则。
- 删除只能针对应用当前持有、经本地校验的具体建议项。批准必须绑定建议标识、规范化路径、删除方式和清单版本；执行前变更则失效并重确认。
- 普通读取、定位和用户要求的扫描/分析可连续执行；覆盖现有结果或删除数据时按具体影响提示。一次清理只保留一个确认入口，拒绝后不执行、不循环索要批准。
- UI 主线程不执行模型 I/O 或扫描；所有取消、工具失败和未完成任务必须诚实反馈。
- 新增文档、注释和界面文案使用中文，保留现有本地化结构；不改无关字符串编码。

# 关键决定

- Harness 来源：D:/AppData/Github/Loom-X/LoomX.Harness，调查时工作树该目录干净，仓库提交 c907403bdcd35dc27e4460906cc572cfe8da1b08；实现时固定源码快照并记录文件摘要，后续更新显式同步。
- 目标框架：采用 .NET 10，避免维护 Harness 的 net8 分叉。本机已安装 10.0.301 和 10.0.402 SDK。
- 展示与执行分离：Harness 负责循环；SpaceMonger 会话门面负责上下文与工具；WPF 负责消息投影和 UI 调度。采用 ponytail 原则复用现有查询、扫描与清理服务。
- UI 收敛以右侧 Copilot 为重点，同时统一必要的表面、边框、强调色、圆角和间距；保留现有主题编辑与明暗切换能力。

# 验收示例

- 启动仍显示磁盘分析工作区，AI 保持右侧面板；开合、调宽、Treemap/文件树切换及未配置 API 时的手动扫描可用。
- 干净检出本项目后，可在不访问原 Loom-X 工作区的情况下以 .NET 10 构建测试和发布；运行的 AI 链路实际通过仓库内 Harness，旧文本工具协议不再执行。
- 模型正文和过程事件到达即展示，不等待整轮结束后播放；工具调用及结果按真实发生顺序展示，中文、Markdown 和长路径正常布局。
- 无扫描结果时，用户要求扫描路径并分析，Agent 可调用扫描、等待完成、查询新树，再基于实际结果回答；工具返回包含成功/失败状态和真实扫描摘要。
- AI 可查询当前树、定位路径并按需要切换 Treemap/文件树；UI 选择和后续 Agent 上下文同步，跨范围或被排除路径明确返回原因。
- 手动分析按钮与对话请求使用同一 Harness 分析能力；候选经本地安全校验发布到同一推荐列表，AI 能读取发布结果继续回答，禁止递归启动第二个 Agent 循环。
- 清理前展示具体清单、总量和删除方式；取消不删除；确认只执行绑定清单，失效批准不能复用；执行结果更新推荐、树图和统计并返回 Agent。
- 模型请求、扫描和等待批准均可停止；停止或失败后可继续下一条消息，无悬挂工具协议、重复执行或旧任务覆盖新任务结果。
- API Key、Base URL、聊天/分析模型、思考开关和语言设置继续有效；真实模型请求使用选定模型，错误展示可理解摘要，日志和异常展示不泄漏密钥或请求/响应正文。
- 现有系统保护和各类白名单在 AI 工具、推荐发布、模型上下文和清理执行中按原语义生效；恶意或过时工具参数不能绕过本地校验。
- 右侧面板具有 LoomX 风格的轻量消息区、可折叠工具过程和统一输入卡；窄宽度、深浅主题、技能提及、关联上下文和阅读中保持滚动位置可用。
- 相关自动化回归通过，使用 cua-driver/cua 验收真实发布包并仅截应用窗口；outputs 下提供带可读上海时间的 Windows 发布包，清楚区分模拟协议验证与真实模型验证。

# 验证预期

- 改造前基线：dotnet test src/SpaceMonger.sln，Core 60 项、App 41 项全部通过。已有 NU1701（SkiaSharp/OpenTK）及 Markdown 空引用警告不作为本次引入的问题，但升级后必须验证渲染。
- 使用确定性的流式协议测试服务验证 tool_use/tool_result、多轮查询、取消和错误；不得用固定延时测试冒充实时流式证据。
- GUI 验收使用临时测试目录和测试建议项，不删除用户现有数据。仅使用本次创建的文件验证清理。
- 未取得可用真实模型接入时，记录模拟协议、UI 与实际 Provider 验证的边界，不宣称真实模型端到端已通过。
- Comet Build 后安排独立只读 Verifier，逐项核验实际候选和证据；需求确认、结果接受及 Git 发布分别处理。
