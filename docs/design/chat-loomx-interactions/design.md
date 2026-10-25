# 聊天窗口交互设计

日期：2026-10-26。状态：已按用户确认完成实现与发布，进入独立验收。

## 当前实现与参考

当前 ChatMessage.ApplyActivity 已按 Agent 事件保留 Segments，ChatPanel 直接顺序渲染。思考 Expander 中是普通 TextBlock，没有独立高度约束。消息底部显示 CompletedAtText 和文字复制按钮；用户消息不填写完成时间，因此缺少气泡时间。MarkCompletedAt 会覆盖 Timestamp，需要拆分创建与完成两个概念。

工具结果的 JsonDetailConverter 已使用 UnsafeRelaxedJsonEscaping；工具参数在 ChatMessage 的 assistant 快照处理里直接连接 ArgumentsJson，绕过格式化。这是中文仍显示 Unicode 转义的具体入口。修复应在安全投影之后解析并重新序列化，避免破坏路径和反斜杠。

参考 Loom-X 的 LoomX/ViewModels/AssistantViewModel.cs：ChatMessageViewModel.Process、RefreshElapsed、Finish 和 Project；其过程块最终完成后冻结计时并默认折叠，带工具调用的完成事件仍属于中间步骤。LoomX/Views/AssistantView.axaml 提供父级过程折叠、气泡 DisplayTime 和复制图形路径。Avalonia 控件不直接迁入 WPF。

## 展示和生命周期

保留 Segments 作为完整有序记录，新增少量可绑定状态和过程/最终正文展示投影，避免引入新消息协议。最终正文边界必须来自实际事件，而不是正文关键词或字符串猜测。运行时过程展开，最终 assistant 正文完整输出后将边界前内容默认折叠，正文继续显示。纯文本响应没有空的过程详情；失败、取消和等待用户保持真实状态。已有技能任务卡继续显示并可打开。

使用现有 UI Dispatcher 节奏每秒刷新活动消息耗时，控件卸载后停止刷新，终态保存固定完成时间。Timestamp 保持创建时刻，新完成字段单独保存；计时不得为负。历史 DTO 新增字段保持可选，旧快照不要求迁移，缺失数据只按现有记录恢复。

## 思考滚动

思考内容置于有 MaxHeight 的纵向 ScrollViewer，长内容不会无限撑高过程区。记录距底部距离：底部追加后跟随，上翻后不自动拉回；滚动到容器边界时允许消息列表继续滚动。程序引发的过程折叠不得误走“用户主动展开”处理，避免冻结外层自动跟随。滚动和订阅随控件生命周期释放。

## 参数、复制和时间

仅在 UI 显示链路处理安全 JSON，使用已有 System.Text.Json 和现有脱敏投影，不改变实际工具参数。复制使用现有按钮和 WPF Path，继续使用中文 tooltip 与 AutomationProperties.Name。每条气泡底部显示 HH:mm，完整本地日期时间放入 tooltip，结束时还可查看冻结耗时。

## 验证与交付

先补充能暴露当前差异的核心回归检查，再实施小范围修改。覆盖多步 final 边界、终态耗时、中文参数与脱敏、快照恢复以及旧记录兼容。完成 WPF 构建和现有相关测试后，发布到 outputs/SpaceMonger-Copilot_yyyy-MM-dd_HH-mm-ss；使用 Start-Process -WindowStyle Hidden 启动绝对 exe，并通过进程 Path 校验实例。用 cua-driver 对应用窗口验收折叠、滚动、复制和时间，保存必要的应用截图，再交由独立验收会话核对全部接受条件。

## 空态候选补充

用户于 2026-10-26 确认六项方案并要求一并改善空态按钮。全部候选入口复用一个 WPF 胶囊按钮样式，使用现有主题画刷、圆角和悬停/按下/聚焦反馈，保持原有提示与命令。


## 实现与验收记录

- 已复用既有 Segments，增加过程/最终正文投影、独立完成时间及可选历史字段；没有新增应用依赖。
- 已修复参数入口绕过中文格式化的问题；已有安全投影和脱敏仍先于显示格式化。
- 已完成三项初始失败回归，再扩展到八项消息生命周期检查；Core 开发期全量 297 项通过。
- App 原有 75 项通过，另新增一个真实 STA/WPF 布局测试，覆盖独立思考滚动的底部跟随、上翻保持与恢复跟随。
- CUA 已在实际发布包上点击空态胶囊、父级过程与复制图标，观察 0s→33s→57s 计时及最终正文完成后的默认折叠；剪贴板保存了完整最终正文。
- 最终发布包浅色和深色主题均已检查胶囊背景、文字与布局。后台窗口被覆盖时，CUA 截图可能纯黑；此时使用项目已有 capture 验收接口生成应用自身 RenderTargetBitmap，未切换前台或截取桌面。
- 历史字段兼容、创建时间不被覆盖、冻结耗时及最终边界的还原由 Core 和 App 测试验证；旧数据缺失的时间不伪造。
- 最终发布包：C:\Users\BianShanghai\.codex\worktrees\chat-loomx-interactions\SpaceMonger-Copilot\outputs\SpaceMonger-Copilot_2026-10-26_06-11-08
- 本次窗口截图、复制结果、测试日志与 SHA256：C:\Users\BianShanghai\.codex\worktrees\chat-loomx-interactions\SpaceMonger-Copilot\outputs\chat-loomx-验收_2026-10-26_05-23-36
- 独立验收使用后续 Comet Verifier；此处的开发检查不代替独立结论。


## 独立验收后的边界修复

- Emoji：System.Text.Json 即使使用 UnsafeRelaxedJsonEscaping 仍转义非 BMP 字符。对规范化后的 JSON 逐字符处理，只还原有效 Unicode 代理对，跳过双反斜杠；字面 Unicode 路径和控制字符语义有回归检查。
- 旧终态：单条 SavedChatMessage 无法知道父会话的 State，因此在真实 DiskAssistantService.RestoreConversation 入口将根终态传给最后助手；其后若有新用户消息则不回填，避免误改之前成功轮次。单条已有 ProcessStatus 仍优先，旧记录不伪造完成时间。
- 新增六个边界回归样例，关注消息交互检查共 14 项通过。正式复验改用实际历史恢复入口，覆盖取消、失败、中断、完成以及新问题尚未产生助手的情况。
- 修复包已重新发布并通过路径校验启动，CUA 确认四类候选入口，应用窗口截图见“修复包-空态窗口.png”；SHA256 记录见 package-sha256-repair.json。
