# Tiny Harness 改造验证记录

日期：2026-10-24（Asia/Shanghai）。状态：第二轮独立代理验收 A1–A12 全部通过，Runtime 12/12；Release 209 项通过。等待用户接受结果，尚未归档。

## 交付内容

- 发布目录：outputs/SpaceMonger-Copilot_2026-10-24_21-06-26。Windows x64 自包含包，包含 .NET 10 运行时；同名 ZIP 约 102 MiB，核心程序 SHA-256 与构建清单一致。
- 磁盘工作区保持主入口，Copilot 保持在右侧。模型、扫描、查询、视图、推荐与确认清理形成原生工具闭环。
- Harness 来自 c907403bdcd35dc27e4460906cc572cfe8da1b08，9 个来源文件的 SHA-256 全部与来源.json 匹配。
- 独立源码副本在系统临时目录完成 Release 构建，0 错误；路径及日志位于下列证据目录。构建没有引用原 Loom-X 工作区。

## 自动化证据

- 改造前：Core 60、App 41，共 101 项通过。
- Comet Runtime 第一轮 Release 回归：Core 150、App 32，共 182 项通过；修复后第二轮 Core 177、App 32，共 209 项通过，0 失败、0 跳过。检查记录 release-regression 已绑定当前候选。
- 协议覆盖原生工具参数分块、多行 SSE、正文/思考即时输出、签名思考、取消、不完整流失败、HTTP 错误分类与脱敏。
- 业务覆盖扫描后继续查询、旧扫描不得冒充新扫描、路径/白名单/保护、候选大小采用本地数据、焦点范围、重叠路径、批准清单失效、缺失文件、重复删除、递归统计及导航清理。
- 手动分析原始历史与对话隔离；改变 AI 对话排除规则后，后续请求不会回放旧上下文。
- 每轮结果直接携带本轮分析产物，后续任务不能覆盖已返回结果；停止最后的模型说明也不会丢弃已经发布的建议。

## 原生应用验收

以下操作在独立 settings.dat 与回环 SSE 服务下执行，模型接入和磁盘服务都走正式应用链路。所有删除仅涉及本次创建的 fixture/cache-A 与 fixture/cache-B。

| 场景 | 实测结果 |
| --- | --- |
| 无扫描起步 | 自然语言触发真实扫描，得到 3 个文件、3 个文件夹、98340 字节 |
| 连续工具 | 读取上下文 → 扫描 → 分析证据 → 发布两项建议 → 定位文件树；同轮继续并回答 |
| 手动分析 | 请求使用 fixture-analysis-model；对话使用 fixture-chat-model；手动分析完成后输入栏恢复聊天模型 |
| Treemap | 在 .NET 10 发布包中实际绘制 64 KB 和 32 KB 矩形区域 |
| 主题与宽度 | 300 像素浅色、360 像素默认及 520 像素深色布局可读；已显示 Markdown 能随主题更新 |
| 过程阅读 | 展开扫描详情后标题位置 Y=514 → Y=514，参数和真实结果可检查 |
| 拒绝清理 | 两个缓存目录均保留，重要文件 SHA-256 不变，统计仍为 98340 字节 |
| 旧批准失效 | 在确认期间改变复选框选择，点击旧确认后不执行删除；两个缓存仍保留 |
| 确认清理 | 仅删除两项缓存，共 98304 字节；重要文件 SHA-256 不变；推荐清空，统计为 1 个文件、1 个文件夹、36 字节 |
| 停止与继续 | 停止受控长流后状态为已停止，随后下一轮对话正常完成 |
| 最终包启动 | 最终构建的 exe 再次启动并完成无扫描对话；右侧面板收起/恢复后会话与 360 像素宽度保留 |

## 证据位置

本次证据位于 work/ai-first-loomx/2026-10-24_17-00-56/（已忽略，不作为产品配置）。

- protocol.jsonl：只记录模型标识、工具名称与阶段，不记录 Key 或原始请求正文。
- portable-root.txt、portable-build-final.log：独立源码构建目录与结果。
- delivery-manual-state.json：手动分析完成后的模型与状态。
- delivery-rejected-state.json、delivery-stale-approval-state.json、delivery-cleaned-state.json：拒绝、失效批准与确认清理后的真实状态。
- retained-baseline.sha256：保留文件的校验基准。
- delivery-stopped-state.json、delivery-resumed-state.json：停止与下一轮恢复状态。
- accepted-light-300.png、accepted-dark-520.png、accepted-process-expanded.png、delivery-analysis.png、delivery-cleanup-confirmation.png、delivery-after-cleanup.png：应用渲染图。
- shipped-binary-hello.json、shipped-panel-collapsed.json、shipped-panel-restored.json：最终包的原生 UIA 观测。

## 验证边界与保留事项

- 未使用真实 Provider 凭据，也没有把真实用户磁盘元数据发往外部模型。真实 Provider 联调不能计为已通过；当前证据为受控协议与真实原生应用/磁盘服务端到端。
- CUA 的后台 PrintWindow 在本机返回空白图，因此操作及状态通过 cua-driver/UIA 验证，图像由同一应用的 RenderTargetBitmap 生成，仅包含目标应用，不截取桌面。
- 构建存在 SkiaSharp/OpenTK 兼容等警告；本机发布包的实际 Treemap 渲染已经检查。
- CodeGraph 已更新；11 个 ENOENT 条目均对应本次删除、但尚未提交 Git 的遗留实现文件。图谱可用，实际代码与构建结果优先。
- 保留 .planning、旧计划、历史 outputs 与其他会话产物。工具初始化产生的 .agents、.amazonq、.claude/skills、.codebuddy、.codex、.devin、.gemini、.github、.kiro、.qoder、.qwen、.trae-cn、.trae、.workbuddy、.zcode 目录未清理；交付前应按归属审阅，避免将工具配置误当产品代码提交。
- 本次未执行 Git 提交、推送、合并或 PR 发布。

## 第一轮修复与补充证据

- 清单版本保持轻量；批准快照包含选中目标完整路径、类型、长度、时间与属性的稳定 SHA-256。快照和删除前检查共用实现，不读文件正文、不跟随链接。
- Capture、整批批准复核与单目标复核均放入后台并传递取消。清理服务临近删除再次检查批准指纹，返回当前实际逻辑字节。
- 原生补充验收：恢复候选目录自身 mtime 后修改深层文件，批准失效；批准后新增 Program Files 后代被保留；旧扫描树未包含该后代时，也无法进入批准。见 repair-cleanup-safety-result.json。
- 修复后成功路径再次验证：仅删除新的普通测试缓存，保留文件哈希不变，推荐清空，最终统计仍为 1 文件、1 目录、36 字节。见 repair-normal-cleaned-state.json。
- 最新候选：outputs/SpaceMonger-Copilot_2026-10-24_21-06-26 及同名 ZIP。此前 19-12-22 包已标记第一轮未通过，保留为证据。
- 自动审批拒绝清理一处失败测试创建的临时目录，仅返回 blocked by policy。其 ACL 已恢复，目录保留：C:/Users/BianShanghai/AppData/Local/Temp/SpaceMonger-cleanup-snapshot-98197b7cc62d424f9461aeb24a705e6d。

- 第二轮 Runtime 回归：Core 177、App 32，共 209 项通过；当前候选 f9d34a76-f277-4b2b-9eb3-c5d830a8de46。
- 修复后的独立源码 Release 构建见 repair-portable-build.log，0 错误。

## 最终独立验收

- 第二轮独立代理逐项判定 A1–A12 全部 passed，Runtime 已接收，12/12 通过、0 failed、0 blocked。
- 独立安全探针 20 项断言通过，F1/F2 已复证关闭；来源/ZIP/新包核验通过。
- 详细报告：work/ai-first-loomx/2026-10-24_17-00-56/round2-verifier-independent-report.json。
- 当前为 Skill 协调的验收；平台无法自动证明独立执行身份，Comet 要求用户明确接受后才能归档。实际核验由单独的只读代理完成。

## 2026-10-25：Tiny Harness 命名调整

- 工程目录与程序集改为 src/TinyHarness 和 TinyHarness.dll；展示名称为 Tiny Harness，命名空间与全部源码、测试引用统一为 TinyHarness。
- 来源.json 保留 Loom-X 原始提交、原始文件名和摘要，另记录改名后的文件摘要。
- Release 全量测试通过：Core 177 项、App 32 项，共 209 项，无失败或跳过。
- Windows x64 自包含发布成功；依赖清单包含 TinyHarness，无 LoomX.Harness 依赖。
- 从新发布目录启动程序，状态接口正常，cua-driver 读取窗口和 UIA 树成功，随后关闭本次启动的进程。
- 本次发布目录：outputs/SpaceMonger-Copilot_2026-10-25_03-11-34，并生成同名 ZIP。旧发布包保留。
- 保留既有 NuGet 兼容性与编译警告；本次未进行真实 Provider 联调。
