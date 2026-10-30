# 目标
修复长思考流式输出使聊天区域卡顿的渲染路径，默认折叠思考并显示轻量活动预览。保留完整思考内容及落盘，不将 UI 改善宣称为所有超时均已修复。
# 范围
聊天思考分段模型、思考展示控件、流式 UI 分发与相应测试、真实文件夹发布验收。按用户最终决定追加：模型连续5分钟未收到新消息才超时，持续推送不受总时长限制；磁盘扫描、规则只读发现和扫描分析无固定超时，保留用户取消。
## 目录结构
### 新建
- src/DiskPilotX.App/Views/ThinkingContentView.cs — 折叠预览、按需创建虚拟列表与阅读位置管理。
- src/DiskPilotX.App/ViewModels/ChatActivityBuffer.cs — 合并高频文本事件的 UI 分发。
- tests/DiskPilotX.App.Tests/ThinkingVirtualizationTests.cs — 长文本、生命周期与可见容器验证。
### 修改
- src/TinyHarness/AgentLoop.cs、AgentSession.cs、IModelClient.cs — 模型空闲计时语义和流消息活动通知。
- src/DiskPilotX.Core/Services/Assistant/DiskAssistantService.cs — 新建及恢复会话5分钟空闲超时、扫描工具无期限。
- src/DiskPilotX.Core/Services/Assistant/AnthropicModelClient.cs、Services/Llm/OpenAiModelClient.cs — 在收到上游流数据时刷新空闲计时，包含尚未聚合成工具调用的参数增量。
- tests/DiskPilotX.Core.Tests/ModelIdleTimeoutTests.cs — 持续推送、断流、工具分片与扫描取消回归。
- src/DiskPilotX.Core/Models/ChatMessage.cs — 新思考段默认折叠。
- src/DiskPilotX.App/Views/ChatPanel.xaml — 替换整段 TextBlock。
- src/DiskPilotX.App/ViewModels/ChatViewModel.cs — 接入有序合并刷新与结束冲刷。
- tests/DiskPilotX.Core.Tests/ChatMessageTests.cs — 默认状态回归。
### 删除
无；不删除主目录或其他会话产物。
### 明确不建
不引入第三方 loopscrollview 库；不新增超时设置界面或可调范围，不实施默认10分钟等已被用户拒绝的建议，不修改清理规则、模型请求内容或权限策略。
# 非目标
不改动其他聊天功能，不删除用户文件，不访问真实模型发起付费请求，不合并、提交或推送 Git。
# 约束与决定
用户已确认：默认折叠、标题滚动最新增量、展开隐藏标题预览并虚拟化正文、合并 UI 更新、上翻不抢位置、回归测试和双包交付。
用户已授权 worktree 开发，并复制主目录现有源码与未提交改动作为基线。源码来源与哈希记录在 .planning/thinking-stream-virtualization/；不称作干净 main。Comet 历史任务在新工作树仍发生冲突，因此仅该 worktree 使用独立 artifact_root，不修改历史任务。
用户最终明确要求：只改为连续5分钟无新消息才超时；只取消扫描及对应只读发现/扫描分析的期限，保留手动取消；其余工具、HTTP配置、设置UI、超时文案不改。
# 验收示例
- 新收到或恢复的思考默认折叠；折叠标题单行显示最新增量且持续滚动，长文本不会创建正文容器。
- 展开时停止并隐藏标题预览；完整思考按有界片段虚拟化，只实现可见部分及少量缓存；无换行长文本和中文、代理对均不丢失。
- 底部跟随追加，上翻阅读不被追加抢位；收起再展开、结束、卸载不保留运行中的预览定时器。
- 高频流式增量合并后有序展示，不因 UI 忙阻塞生产线程；结束与错误事件之前的内容不遗漏，落盘完整。
- 回归测试与长文本控件数验证通过，并以非提权真实文件夹测试包完成 UI 验收；发布环境验证源码规则编译、授权只读执行及失败诊断。
- 双包放主仓库 outputs，文件夹自包含、不裁剪不单文件；实际 EXE 清单分别 asInvoker 与 requireAdministrator，依赖和 skills 齐全；明确未经交互验证的 UAC 边界。
- 模型请求超过5分钟仍持续收到新消息时不中断；首次等待或中途连续静默5分钟时才超时；新建和恢复会话语义一致，用户停止仍有效。
- 磁盘扫描、规则只读发现和扫描分析没有固定总时限，模型等待计时不误用于工具阶段；其他工具保留原时限，不新增设置入口或执行其他建议。
