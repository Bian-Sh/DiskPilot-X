# 分析规则编排实施计划

> 执行约定：通过 Comet Native 完成登记和 Shape 确认后，按任务进行失败测试、最小实现、回归及独立验收。阶段内可本地连续实施，不默认派发并行实现；独立 Verifier 必须与实现者分离。本文件不替代 Runtime 的确认、状态或验收报告。

**目标：** 保留分析按钮，形成可扩展、可解释且不越权的分析闭环，再加入轻量偏好与快捷入口建议。

**结构：** 在既有 WPF／Core 分层中增加少量分析侧记录、只读规则适配和本地上下文存储，复用现有模型循环、询问卡和推荐列表。

**技术基线：** 当前工程为 .NET 10、C#、WPF、xUnit；不增加向量数据库，不以安装新依赖代替现有能力。

**依据：** [产品规格](spec.md)、[设计与禁改清单](design.md)、[验收矩阵](acceptance.md)。

## 全局约束

- 中文文档、注释与提交说明；不因终端显示问题转码或改无关中文。
- 不改规则创建模块、规则 CRUD、共享规则格式与存储；本次通过分析侧适配消费。
- 不改其他会话状态、产物或分支；不自动提交、合并、推送或清理。
- 分析不删除，规则／历史／偏好不赋权；不放松路径、隐私或源码授权保护。
- 不硬编码最终回复；界面状态本地化与模型自由表达分开验证。
- 分阶段都要真实验证；未实现、未运行、跳过和失败均不得记通过。

## 前置登记

- [x] [T001] 解决 Comet 工作树登记并导入本资料。先检查公开 CLI 的状态和诊断，保留现有工作树；不得修改其他变更的状态、移动其目录或通过删除文件绕过检查。成功创建后仅用返回的 briefPath／specsDir 写完整正式要求，映射本地 A 编号。将规则创建模块禁改边界纳入 Shape；由用户确认完整 Shape 后才开始应用实现。登记、导入和完整 Shape 确认已完成，Runtime 进入 Build，十八项验收现已全部通过并归档；详见 [README](README.md)。

## 第一阶段：分析闭环与上下文

### T002：任务及扫描来源模型

- [x] [T002] [US1] 在 src/DiskPilotX.Core/Services/Analysis/AnalysisTaskContext.cs、AnalysisTaskResult.cs 新增任务、结果与来源记录，在 src/DiskPilotX.Core/Models/ScanSession.cs 增加可选来源关联；不重构既有扫描树。覆盖 FR01、FR09、FR10、FR16、FR17。
- 测试：新增 tests/DiskPilotX.Core.Tests/AnalysisTaskContextTests.cs；先验证旧扫描缺字段、成功扫描绑定、新旧意图分离、取消不污染、结果类型约束，再最小实现。
- 交付给后续：任务标识、scanId、scope、当前目标、原扫描目标、关联消息、隐私签名；Result 生命周期与 outcome 分开。
- 检查命令：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --filter FullyQualifiedName~AnalysisTaskContextTests。

### T003：只读目录与阶段适配

- [x] [T003] [US1] 新建 src/DiskPilotX.Core/Services/Analysis/AnalysisRuleCatalog.cs 及 tests/DiskPilotX.Core.Tests/AnalysisRuleCatalogTests.cs，消费既有 IAiSkillRouter 与 CleanupSkillStore 只读 API；不修改它们。覆盖 FR02、FR03、FR04、FR17。
- 先测：新规则下轮可见、读取失败局部化、版本变化、同名冲突、旧 Markdown、缺失元数据、内容分页及保护约束必读；再实现摘要和按阶段读取。
- 交付给后续：可追溯目录快照、按需读取结果、实际版本、unknown／legacy-full／截断状态。
- 检查命令：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --filter FullyQualifiedName~AnalysisRuleCatalogTests。

### T004：分析最小权限与预览边界

- [x] [T004] [US1] 新建 src/DiskPilotX.Core/Services/Assistant/DiskAssistantService.Analysis.cs，调整 DiskAssistantService.cs 的分析注册部分；新增 tests/DiskPilotX.Core.Tests/AnalysisToolBoundaryTests.cs。覆盖 FR05、FR07、FR16。
- 先测：发现、读取、按 id 预览、ask_user 可用；save/update/delete、草稿 package、任意源码、cleanup 不能调用。验证越界、重解析点、隐私排除子树和输出二次过滤。不能保障内部遍历排除时拒绝预览并退回扫描证据。
- 公开资料调用测试使用替身：传公开主题，私密信息被拒绝；模型不支持或失败的反馈不得改成无候选。
- 检查命令：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --filter FullyQualifiedName~AnalysisToolBoundaryTests。

### T005：必要询问与既有创建入口交接

- [x] [T005] [US1] 扩展分析侧状态与 ChatViewModel 的既有卡片处理；新建 tests/DiskPilotX.Core.Tests/AnalysisDecisionFlowTests.cs，并补 tests/DiskPilotX.App.Tests/ChatViewModelProposalTests.cs。覆盖 FR06、FR16、FR17。
- 先测：无规则不强制询问；重要歧义才询问；通用／补充／跳过／取消／交接路径；只暂停未知部分；取消不重问；恢复时扫描已改变；创建模块不支持恢复时明确告知并允许继续。
- 不写 skill-creator、管理工具、规则包或创建面板。交接只表达创建意向，不模拟成功结果。
- 检查命令：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --filter FullyQualifiedName~AnalysisDecisionFlowTests；应用侧对应 Proposal 测试。

### T006：校验与发布诊断

- [x] [T006] [US1] 在 RecommendationEngine.cs、AnalysisDiagnostics.cs 和 src/DiskPilotX.App/MainWindow.Copilot.cs 边界补真实原因；新增 tests/DiskPilotX.Core.Tests/AnalysisValidationReportTests.cs，补既有 RecommendationPolicyTests.cs。覆盖 FR08、FR12、FR16。
- 先测：有输入但全部硬保护、白名单、未扫描、格式无效、重叠、核心通过但宿主再过滤、发布失败。次数和原因来自实际处理；不泄漏排除路径。
- 保留所有硬保护行为；输入重复不能诱使反复提交绕保护。宿主接受清单才是可显示建议的事实来源。
- 检查命令：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --filter 'FullyQualifiedName~AnalysisValidationReportTests|FullyQualifiedName~RecommendationPolicyTests'。

### T007：推荐列表和聊天状态一致

- [x] [T007] [US1] 修改 src/DiskPilotX.App/ViewModels/RecommendationsViewModel.cs、ChatViewModel.cs、Views/RecommendationsPanel.xaml 及 Localization/Strings*.resx，补 RecommendationsViewModelTests.cs 和 CleanupProjectionTests.cs。覆盖 FR01、FR08、FR09。
- 先测全部生命周期及结果组合；失败、取消不把旧建议当作新任务成功。重要部分未评估时显示部分结果和原因。
- 继续复用现有推荐选择／清理入口，不新增第二个清理窗口。界面有稳定状态文本，模型结论只检查语义要求。
- 检查命令：dotnet test tests/DiskPilotX.App.Tests/DiskPilotX.App.Tests.csproj --filter 'FullyQualifiedName~RecommendationsViewModelTests|FullyQualifiedName~CleanupProjectionTests'。

### T008：来源持久化与受限历史恢复

- [x] [T008] [US1] 新建 src/DiskPilotX.Core/Services/Analysis/AnalysisContextStore.cs，更新 src/DiskPilotX.App/ViewModels/MainViewModel.cs、MainWindow.Copilot.cs 的成功扫描关联和分析侧历史读取；新增 tests/DiskPilotX.Core.Tests/AnalysisContextStoreTests.cs，补 App 的 AssistantScanTests.cs、ConversationHistoryTests.cs。覆盖 FR10、FR11、FR12、FR16、FR17。
- 先测：手动／聊天／规则来源，取消不覆盖；摘要优先；原文无授权拒绝；源会话删除、隐私签名变化、坏文件与旧数据兼容；摘要不可绕过访问限制。
- 原子写入的本地小文件按任务／扫描关联，不复制扫描树和思考内容；记录目录通过既有品牌兼容路径解析，测试注入隔离目录。
- 检查命令：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --filter FullyQualifiedName~AnalysisContextStoreTests；应用侧对应 Scan／History 测试。

### T009：总流程装配与指引

- [x] [T009] [US1] 在 MainWindow.Console.cs、ChatViewModel.SendAppAnalysisAsync 和分析侧服务装配动态目录、当前目标与来源；更新 skills/path-cleanup-recommendation/SKILL.md、skills/disk-management/SKILL.md。覆盖 FR01 至 FR12、FR16、FR17。
- 新增 tests/DiskPilotX.Core.Tests/AnalysisConversationFlowTests.cs；补 tests/DiskPilotX.App.Tests/RecommendationsViewModelTests.cs，脚本模型覆盖有候选、无候选、受限、待输入和部分结果。
- 指引规定目标、发现／核实步骤和总结目的，不列死所有软件规则，不写固定收尾句；先测应用分析不通过 CreatorIntent／@ 路由再实现。
- 检查命令：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --filter FullyQualifiedName~AnalysisConversationFlowTests。

## 第二阶段：偏好与克制的快捷入口建议

### T010：用户确认偏好

- [x] [T010] [US2] 在分析侧上下文存储中增加明确偏好记录和受限读取／写入操作，新建 tests/DiskPilotX.Core.Tests/AnalysisPreferenceTests.cs。覆盖 FR13、FR16。
- 先测：明确确认才保存、临时回答不保存；范围匹配；查看、更正、删除、停用；当前要求优先但不静默重写；偏好不能改保护、清理授权或泄漏范围。
- 通过既有聊天交互提供控制，不新建规则编辑器或全局记忆平台。
- 检查命令：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --filter FullyQualifiedName~AnalysisPreferenceTests。

### T011：可选行为线索

- [x] [T011] [US2] 增加分析侧最小行为记录与固定时间源测试；新增 tests/DiskPilotX.Core.Tests/AnalysisUsageTests.cs。覆盖 FR14、FR16。
- 先测默认关闭、显式启用、关闭／清除、取消失败不记成功、查询／分析／清理区分、无原始路径／对话内容；读取错误不得阻塞主分析。
- 只从实际操作完成事件累计，不回放旧会话补历史，不因为偏好保存而偷偷启用行为统计。
- 检查命令：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --filter FullyQualifiedName~AnalysisUsageTests。

### T012：快捷入口建议与拒绝冷却

- [x] [T012] [US2] 增加分析侧建议门槛与拒绝状态；新增 tests/DiskPilotX.Core.Tests/AnalysisShortcutSuggestionTests.cs。覆盖 FR15、FR16、FR17。
- 先测：近 30 天同主题同动作三次成功；30 天最多一次；暂时拒绝冷却；不再提醒；主任务失败／取消／等待时不建议；未启用统计不声称长期频率；接受仅交接既有创建模块。
- 不改快捷入口创建、删除、更新能力，不在确认前显示已创建入口。
- 检查命令：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --filter FullyQualifiedName~AnalysisShortcutSuggestionTests。

## 联调、验收和交付

- [x] [T013] [US1] 第一阶段候选完成后运行 Core／App 相关回归，检查 A1—A12、A16、A17；使用隔离样本复现 Steam 中“均被保护过滤”的情况，不读取或删除真实 Steam 文件。覆盖 FR01 至 FR12、FR16、FR17。
- [x] [T014] [US2] 第二阶段候选完成后验证 A13—A15 及对第一阶段的影响，固定时间验证冷却和清除，不把当前记录补成历史频率。覆盖 FR13、FR14、FR15、FR16。
- [x] [T015] 对当前完整候选执行独立只读 Verifier，逐项核对 A1—A18；经 Comet 请求和记录可安全重跑的完整测试命令，不能用实现者自评替代。覆盖 FR01 至 FR18。
- [x] [T016] 运行隔离后台 WPF，优先用 cua-driver 验证状态、询问恢复和偏好控制；仅截应用窗口，核验进程 Path。发布独立时间命名包并校验入口与必要资产。覆盖 FR18。

## 完整验证命令与条件

- 文档：python docs/analysis-rule-orchestration/verify_spec.py；当前纯文档阶段增加 --documents-only 做路径边界检查。
- Core：dotnet test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj。
- App：dotnet test tests/DiskPilotX.App.Tests/DiskPilotX.App.Tests.csproj。
- 完整方案：dotnet test src/DiskPilotX.sln。
- 发布：PowerShell 先执行 $stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'，再执行 dotnet publish src/DiskPilotX.App/DiskPilotX.App.csproj -c Release -r win-x64 --self-contained true -o (Join-Path 'outputs' ($stamp + '_分析规则编排'))；同名输出存在则停止，不覆盖。
- 若独立工作树缺少主工作区未提交的 Directory.Build.props，不拷贝或提交别人的文件；先记录实际基线问题，再局部解决本变更的构建配置或协调依赖。
- 正式候选 Core 520 项、App 179 项共 699 项测试通过；十八项独立验收通过，后台 GUI 及时间命名发布包已完成。真实外部模型体验未执行，详见 README 与正式验收报告。
