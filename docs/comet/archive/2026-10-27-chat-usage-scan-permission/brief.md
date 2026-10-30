# 目标
让每条 AI 回复准确展示模型与缓存用量，并让 AI 基于管理员状态和真实扫描执行信息回答问题，避免把后来取消的请求误当成之前扫描的状态。用户于 2026-10-28 已确认方案和截图补充范围；授权临时处理新 worktree 内继承的旧 Comet 记录，原项目和其它 worktree 不动。

# 范围
扩展现有对话、用量、扫描结果与宿主实时上下文；使用原生管理员检测和现有确认卡，保持小步改动。

## 目录结构
### 新建
- src/DiskPilotX.Core/Services/Scanning/ScanExecutionInfo.cs：扫描路线、状态、回退与提权事实的轻量契约。
- 对应 Core / App 自动化测试与本任务验收证据。
### 修改
- src/TinyHarness、src/DiskPilotX.Core/Models/ChatMessage.cs、Services/Llm、Services/Assistant：模型标识、缓存分母、消息历史、实时扫描事实。
- src/DiskPilotX.Core/Models/ScanSession.cs、Services/Scanning：实际路线记录。
- src/DiskPilotX.App/ViewModels、MainWindow.Copilot.cs、Views/ChatPanel.xaml：消息展示与宿主提醒。
### 删除
不删除产品文件和其它任务产物。
### 明确不建
不新增统计框架、缓存系统、权限服务或自动提权重启机制。

# 非目标
不强制管理员启动，不承诺管理员必然能用 MFT，不猜测缺失的历史模型或扫描路线，不更改扫描算法和白名单策略，不自动合并、提交、推送或清理其它任务。

# 验收示例
- A1：同一会话切换模型后，每条 AI 回复在 token 左侧展示属于本次回复的模型 ID；保存与恢复历史后不变，旧记录不猜测模型。
- A2：token 弹窗展示缓存命中率，兼容 OpenAI 与 Anthropic 输入口径，多请求按对应输入总量加权，不平均百分比。
- A3：缺失或无效缓存统计明确显示未提供，明确零命中显示 0%，零输入不除零；重复完成事件不重复统计。
- A4：AI 实时上下文包含当前进程的真实管理员提权状态，不以账户所属组或模型推测代替。
- A5：非管理员状态下 AI 扫描前由宿主明确提醒慢速普通遍历风险并等待用户确认；取消不执行扫描，不自动提权；管理员不保证 MFT。
- A6：实际扫描分支回传 MFT、USN 增量或普通遍历，以及发生过的回退；get_copilot_context 与 scan_path 均能读取事实。
- A7：最近已完成扫描与最近请求状态分别提供；确认前取消、中途取消和完成明确区分，AI 能正确回答截图中的之前扫描路线问题，不以新请求取消否认旧结果。
- A8：相关自动化测试和应用窗口验收通过，重新发布到带可读时间的 outputs 目录；保留其它会话改动。

# 约束与决定
使用独立 worktree codex/chat-usage-scan-permission；工程代码优先于陈旧的模板 AGENTS.md。中文编码按 UTF-8 严格检查，不修改无关文案。所有 Git 操作独立于 Comet 执行。只捕获应用窗口，隐藏启动验收应用。
