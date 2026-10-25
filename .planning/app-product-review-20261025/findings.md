# 已核实事实

- 主目录和三个 worktree 的 HEAD 均为 5b11f7c8b9e46ce21bf60fe78b85b70edaaa29d1，各自存在未提交修改；不能把某个 worktree 的功能当成主目录已交付。
- AGENTS.md 描述为 Specify 模板，与实际 .NET 10 / WPF / SkiaSharp / TinyHarness 工程不符。
- CodeGraph status 显示索引最新，188 个文件；已查询 ChatViewModel 与 AskAiAboutEntry，并回到源码核实。未另建 Graphify 索引。
- TreemapView.xaml.cs:145、TreeViewControl.xaml.cs:304 已有“询问 AI”。MainWindow.ViewModels.cs:161 的共同处理器只设置 LinkedEntry、覆盖 InputText 并聚焦，未自动发送。
- MainWindow.ViewModels.cs:192 会随 Treemap 选择改写 LinkedEntry；AgentContext 与 get_copilot_context 读取实时单对象。消息关联与工作区选择尚未形成稳定的附件契约。
- AssistantWorkspace 与 DiskAssistantService 已有真实扫描、查询、导航、建议发布和确认后清理工具，不能称为仅有聊天窗口。
- conversation-history：ConversationStore 与 ChatViewModel.History 等实现存在，Comet 为 build、pending；重启续聊、JSON 快照、白名单隔离，恢复时清理磁盘对象引用和旧确认。
- treemap-wiztree：设计与测试存在；此次检查时未发现主要 Treemap 产品源码差异，Comet 为 build、pending。目标为单击选择、标题双击进入、边界/配色与悬停。
- cleanup-skill-creator：包存储、来源导入、规则预览、可选 C#、受约束 XAML、askuser 与主题实现存在；Comet verify、await-user、verification_result pass。验收记录写明 Core 264 与 WPF 53 项及 A1-A9 通过；本次未重跑。
- ScanSession 有总量、时间、容量与可用空间；FileEntry 有逻辑大小、分配大小、时间与类型，可用于确定性统计。未检索到面向用户的历史空间对比/统计图表页面。
- DuplicateDetector 先按文件名、大小和修改时间分组，再哈希，因而会漏掉改名或修改时间不同但内容一致的副本。
- App 项目未配置 ApplicationIcon，主窗口未设 Icon，未找到专用 ico 与创建桌面快捷方式的发布流程。
- 现有 AI 主线验收记录区分本地 SSE 模拟与真实 Provider；不能据此断言全部真实模型服务已联调。

检索限制：PowerShell 下向 rg 传带通配符的路径会报错，后续改为明确路径/目录配合 -g。未据终端显示判断源文件编码损坏。
