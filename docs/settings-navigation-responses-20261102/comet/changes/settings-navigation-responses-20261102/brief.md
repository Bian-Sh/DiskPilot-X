# 目标
修复 DiskPilot-X 的四项已报告缺陷，保持现有交互和思考历史语义，交付经测试的完整文件夹发布包。

# 范围
AI 设置模型列表沿用更新说明的内部优先、边界传给外层的滚轮路由；修复面包屑父链冗余节点；修复 Responses reasoning 历史回传缺少 summary 的非法请求；修复思考等级轨道点击后文字、实际配置、持久化与请求参数不同步。

## 目录结构
### 新建
- tests/DiskPilotX.App.Tests/ 与 tests/DiskPilotX.Core.Tests/ 中与四项缺陷对应的最小回归测试。
- docs/settings-navigation-responses-20261102/ 中的规格与设计记录；主仓库统一 outputs 目录下本次测试包与用户交付包。
### 修改
- src/DiskPilotX.App/Views/SettingsSections/ApiSettingsSection.xaml 与代码后置：复用更新说明滚轮路由。
- src/DiskPilotX.App/ViewModels/TreemapViewModel.cs 与确有必要的 MainWindow.Navigation.cs：规范父路径与面包屑身份。
- src/DiskPilotX.Core/Services/Llm/OpenAiModelClient.cs：保留 reasoning summary 与加密内容并构造合法历史。
- src/DiskPilotX.App/Views/ChatPanel.xaml、代码后置及确有必要的 ChatViewModel：统一滑条等级回写。
- 构建及验收脚本仅在现有入口不能覆盖发布要求时做最小调整。
### 删除
无。
### 明确不建
不新增协议客户端、导航架构、依赖或通用设置框架；不操作 Loom-X；不清理其他会话产物。

# 非目标
不删除思考历史、不改变已选模型或模型能力定义；不执行真实用户文件清理；不修改主仓库未提交改动；不自动合并、推送、发布到远端或清理工作树。

# 约束与决策
用户于 2026-11-02 明确允许新建独立工作树。本次源码基于 main 的 8cb9e9417e0be5ae7fa6e025de138ad9475f4ad4，位于主仓库 .worktrees/settings-navigation-responses-20261102，分支 codex/settings-navigation-responses-20261102；不包含主目录未提交的主题与 Tooltip 修改。
仅为隔离继承的旧 Comet 任务，在本工作树备份配置后使用独立 artifact_root；不修改其他任务或主仓库流程状态。
发布必须自包含文件夹，PublishSingleFile=false、PublishTrimmed=false、SelfContained=true；测试包实际 EXE 清单为 asInvoker，用户包为 requireAdministrator；分别强制重建并读取实际 EXE 资源验证。
UI 验收使用非提权测试包及 cua-driver/cua，只截图应用；无 live NewAPI 凭据时，以模拟 HTTP 契约回归说明验证边界，不宣称真实中转站联调通过。

# 验收示例
- A1：模型列表在中间位置只滚内部，上下边界和内容不足一屏时按方向滚外层设置页，模型选择、排序和编辑不回归。
- A2：从带尾部分隔符及无尾部分隔符的同一目录导航父级，不产生同路径重复节点；面包屑名称和路径对应，已有扫描树导航及前进后退保持可用。
- A3：先以回归复现 reasoning 回传缺失 summary；修复后有摘要时完整保留、无摘要时构造合法空数组，同时保留 id、encrypted_content 和工具调用对应关系；多轮工具请求不因本地丢字段产生所示 400，普通 Responses 与 Chat Completions 回归通过。
- A4：滑条默认位置直接点击轨道、拖动、键盘操作均同步等级文字与 ViewModel；保存重载和请求参数与所选等级一致，默认等级沿用原有省略参数语义，模型切换的程序回流不误写配置。
- A5：相关单元和控件回归通过，非提权真实发布测试包完成针对四项修复的可自动化 UI 验收，独立只读 Verifier 逐项评估，不把未执行检查记为通过。
- A6：真实文件夹发布环境完成 validate_skill_source 及经授权的 run_skill_source 等价只读规则执行，并检查错误诊断；两包实际 EXE 清单、运行时 DLL、Core 与 skills 内容满足要求，交付主仓库 outputs 下完整用户包文件夹和 EXE，不生成 ZIP。

- A7：未选、旧模型失效或切换接口时仅在新目录成功后选验证模型；当前有效聊天模型优先；失败、空目录、配置变化期间不发送过期或默认模型请求，不覆盖聊天模型及模型专属思考设置。
- A8：验证进度、成功与失败实际展示本次模型；目录成功与生成失败独立；401 鉴权、403 权限、404 模型、400/422 参数、429 限流、网络/超时/上游故障的文案不混同为密钥无效；日志有结构化诊断且不泄漏 API Key、代理口令、URL 凭据或查询密钥。
- A9：新增回归先红后绿、重新双清单文件夹发布并验证；完成本次修复后向用户指定的 ERP8 既有对话发送可复用经验，要求其检查 AI 实现相关 worktree 后跟进，不代替该对话直接修改 ERP8，也不把未验证推测说成根因。

# 新增已确认范围：API 验证直觉与诊断
用户在 2026-11-02 明确同意以下行为并要求完成后将经验发到 ERP8 既有对话：移除验证链路的隐藏默认模型；验证先刷新同配置真实目录，优先使用当前聊天模型（目录仍包含时），否则用返回列表第一项仅作连接测试，明确显示使用的模型，不改聊天选择或持久化为测试模型；目录失败或无可用模型时停止并说明，不猜模型继续请求。鉴权、模型、参数、限流、网络和上游失败分别展示；记录脱敏后的端点、实际模型、协议、代理、状态码、错误类别/代码和上游摘要。

