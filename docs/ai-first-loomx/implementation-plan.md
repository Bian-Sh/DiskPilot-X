# Tiny Harness 磁盘 Copilot 实现计划

目标：按已确认的十二项验收，保留磁盘主工作区与右侧 AI 面板，替换 AI 编排并完成工具闭环。
架构：仓库内固定 Harness 快照；Core 会话门面和原生模型适配；App 共享操作与事件投影。
技术：.NET 10、WPF、Tiny Harness、现有扫描与路径保护服务。
规格：docs/comet/changes/ai-first-loomx-harness/specs/disk-copilot/spec.md。

## 约束

- 中文文档、注释与提交消息；保留现有资源和无关产物。
- Comet 管理实现与验收；Git 操作独立且当前未授权发布。
- 各改动先建立有行为意义的失败测试，再完成实现与相关回归。
- 最后新建只读 Verifier 核验全部验收，实际 exe 经 cua-driver 验证并发布到 outputs。

## 任务一：固定 Harness 与原生模型协议（A2、A3、A9）

- [x] src/TinyHarness 固定来源源码与 SHA-256，统一工程为 .NET 10。
- [x] tests/SpaceMonger.Core.Tests/AnthropicModelClientTests.cs 先证明原生工具、增量到达、错误脱敏与取消的缺失。
- [x] src/SpaceMonger.Core/Services/Assistant/AnthropicModelClient.cs 实现 IModelClient；运行协议测试。

## 任务二：会话与磁盘工具（A4、A5、A6、A8、A10）

- [x] 为同会话扫描后查询、工具结果继续与取消建立失败测试。
- [x] src/SpaceMonger.Core/Services/Assistant 中实现 DiskAssistantService、运行选项及环境契约。
- [x] App/MainWindow.Copilot.cs 等连接现有扫描、导航和推荐，读取实时上下文，禁止旧回调覆盖新任务。
- [x] RecommendationEngine 保留确定性安全/元数据，模型入口改走 Harness；删除旧文本工具循环及延迟播放。

## 任务三：共享清理与批准（A7、A10）

- [x] 测试拒绝零执行、批准清单变化失效、范围/白名单保护。
- [x] 共享清理操作将 ID、路径、方式与版本绑定；手动和 AI 使用同一确认及执行结果。
- [x] 回归推荐、树图、统计更新，测试仅删除本任务临时目录。

## 任务四：右侧消息与交互（A1、A3、A11）

- [x] Core/Models/ChatMessage.cs、ChatSegment.cs 建立真实事件顺序和折叠状态测试。
- [x] App/Views/ChatPanel.xaml 与代码后置实现 LoomX 风格排版、输入卡、过程、批准与滚动。
- [x] App/ViewModels/ChatViewModel*.cs 接通事件与取消；保留技能提及、上下文与手动分析。
- [x] 验证宽度 300/360/520、明暗主题及磁盘主布局。

## 任务五：集成验证与发布（A1—A12）

- [x] 必要自动化回归通过，升级后验证 WPF/SkiaSharp 实际渲染。
- [x] 本地受控 SSE 与测试扫描目录完成可重复端到端证据；真实 Provider 验证单独记录。
- [x] 发布 Windows 自包含包至上海时间命名的 outputs 目录，cua-driver 验收并仅截应用窗口。
- [x] 填写十二项证据后提交 Comet 候选，新只读 Verifier 独立核验。
