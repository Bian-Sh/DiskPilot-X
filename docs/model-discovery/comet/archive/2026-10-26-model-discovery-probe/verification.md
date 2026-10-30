---
generated_from_state_version: 16
---

# 验证

## 当前结果

- 结果: **已归档**
- 验证情况: **已完成检查，验证结果已确认**
- 目标周期: 1
- 迭代: 3
- 验证器尝试次数: 1
- 完成时间: 2026-10-26T11:11:37.136Z
- 摘要: 第三轮独立只读验收完成：A1-A7全部通过；原第二轮失败探针11项和新增安全边界28项共39项经Runtime全部通过。当前完整Core415/App112正式记录及5条check证据组合摘要匹配；最新包哈希、实际App截图及嵌套认证401控制台/文件双写均核实。未修改源码、需求、设计、既有证据或手写Runtime状态；只在系统临时目录新增本轮JSON/合成探针，并经公开CLI登记/检查/提交。未派生agent、激活/终止App、截全屏、Git提交合并推送、接受结果或归档。请保留真实风险并按Runtime continuation等待用户决定。

## 验收

| 编号 | 结果 | 来源 | 验收项 | 原因 |
| --- | --- | --- | --- | --- |
| A1 | passed | brief.md | AC1：DeepSeek /anthropic 地址优先尝试官方目录；OpenAI 两种协议、Anthropic、国产/海外预设和带前缀中转均生成正确同源去重候选。 | 独立核对 ModelCatalogEndpoints 当前实现与对应已执行测试：DeepSeek /anthropic 优先 /models；OpenAI 两种协议、Anthropic 与国产/海外预设按明确域名生成目录，保留中转部署前缀，同源按路径去重且最多12项。复用当前候选正式 Core415通过记录。 |
| A2 | passed | brief.md | AC2：404/405、HTML、无效 JSON、空列表继续尝试，首个有效目录即停止；完整模型 ID 去重排序，失败保留旧列表。 | 核对 FetchAsync/Extract 和 SettingsViewModel：404/405、非完整目录JSON/HTML/空列表继续，首个有效目录停止，完整ID去重排序，只在成功时替换已有模型。复用正式测试及04手填、05回退、12最新发布包成功截图/状态和请求证据。 |
| A3 | passed | brief.md | AC3：鉴权拒绝、权限拒绝、限流、取消及时停止；单次/总体超时与响应大小有界，不跨域泄漏凭据。 | 核对鉴权403/401、429等非路径错误与取消的停止分支；默认单次8秒、总体45秒，成功读取上限1MiB、错误8KiB，候选最多12，目录客户端禁用自动重定向。正式当前测试覆盖取消、两级超时、大小上限、真实HTTP代理和跨地址重定向不转发；显式URL为用户主动指定的单地址。 |
| A4 | passed | brief.md | AC4：每次尝试及最终异常可在控制台和文件日志中检查，包含地址、状态/异常原因、耗时，敏感内容脱敏。 | 独立复查当前Sanitize完整凭据及编码长值优先、JsonDocument结构解析/敏感键遍历、嵌套字符串递归和深度上限隐藏、无效正文失败封闭，不再以正则猜JSON边界。经Runtime原样重放第二轮认证组合5项与嵌套6项全部通过；额外28项覆盖长短凭据重叠、默认/宽松编码4至16层、编码字段/数组/对象、残缺结构、认证头、URL和超JsonDocument深度及实际ResponseSummary截断边界，均通过。正式App112含9项模型发现UI/双写测试；13截图/状态和structured-real-app.log均保留地址、401、耗时和最终异常，无known-tail/space-tail/nested-private-tail/field-tail。 |
| A5 | passed | brief.md | AC5：代理控件位于模型列表之前，当前编辑中的代理主机/端口/账户/密码即时用于发现；非法自定义代理不悄悄直连。 | 独立核对XAML代理控件先于模型列表、编辑绑定和BuildEndpoint当前字段快照，账户/口令按当前编辑或解密持久值使用；非法主机/端口明确失败不直连。复用正式UI回归以及12截图中自定义代理127.0.0.1:49517成功获取2模型的实际App证据。 |
| A6 | passed | brief.md | AC6：所有候选耗尽时出现应用内弹窗，告知填写模型获取 URL 后重试；显式 URL 优先并保留原列表。 | 核对ModelCatalogExhaustedException、SettingsViewModel失败保留列表和MainWindow现有应用内ModalHost回调；显式ModelsUrl直接请求唯一地址。已用view_image确认03耗尽弹窗完整URL重试指引并保留previous-model，04手填成功；本轮Core安全修复未改变该UI路径，当前正式UI测试亦通过。 |
| A7 | passed | brief.md | AC7：自动化测试通过，使用 cua-driver 对实际应用验收，并在 outputs 生成带可读时间的自包含发布包。 | 复用本轮候选Runtime正式Core415/415、App112/112（ModelDiscoveryTests48、ModelDiscoveryUiTests9），不重复完整测试；5条正式检查均passed且按Runtime组合算法校验evidenceDigest完全匹配。已用view_image核对12/13及03/04截图与cua-driver状态；PID30092窗口19142048的进程路径为outputs/DiskPilot-X_2026-10-26_18-44-06/DiskPilot-X.exe。发布配置includedFrameworks表明自包含，实算exe SHA256为917E7898621E8BA74B536359694E593F52CA973415A53BFCC6E7FDDC0CC404B0，与发布路径/hash文件一致。 |

## 检查

| 检查 | 命令 | 工作目录 | 状态 | 退出码 | 耗时 |
| --- | --- | --- | --- | ---: | ---: |
| Core 完整回归 | test tests/DiskPilotX.Core.Tests --no-restore --nologo --verbosity minimal --logger trx;LogFileName=core.trx --results-directory .planning/model-discovery-probe-20261026/test-results | . | passed | 0 | 8966 ms |
| App 完整回归 | test tests/DiskPilotX.App.Tests --no-restore --nologo --verbosity minimal --logger trx;LogFileName=app.trx --results-directory .planning/model-discovery-probe-20261026/test-results | . | passed | 0 | 3606 ms |
| 第三轮重放第二轮原始认证与JSON失败样例 | -NoProfile -File C:\Users\BianShanghai\AppData\Local\Temp\model-discovery-verifier-auth-json-23c1fa48-bd0f-439a-98aa-c0e659ac10aa.ps1 -ProjectRoot C:\Users\BianShanghai\.codex\worktrees\model-discovery-probe\DiskPilot-X | . | passed | 0 | 475 ms |
| 第三轮重放第二轮原始嵌套JSON失败样例 | -NoProfile -File C:\Users\BianShanghai\AppData\Local\Temp\model-discovery-verifier-nested-json-6bbb95b5-67f1-428a-8e5e-c58d9d132ae7.ps1 -ProjectRoot C:\Users\BianShanghai\.codex\worktrees\model-discovery-probe\DiskPilot-X | . | passed | 0 | 475 ms |
| 第三轮长短凭据重叠与结构化失败封闭安全边界 | -NoProfile -File C:\Users\BianShanghai\AppData\Local\Temp\model-discovery-r3-boundaries-98d32dc1-d3ed-4bf2-b80e-e61f81d478c8.ps1 -ProjectRoot C:\Users\BianShanghai\.codex\worktrees\model-discovery-probe\DiskPilot-X | . | passed | 0 | 525 ms |

### Builder 报告的证据

以下为 Builder 报告，不等同于 Runtime 检查凭据或独立验收结果。

- 结构化与认证组合核心回归: passed — ModelDiscoveryTests共48项通过；新增15项中旧实现13项失败，修复后全部通过。structured-redaction-red/green.log。
- 嵌套与认证组合日志双写: passed — ModelDiscoveryUiTests共9项通过；新增双写先失败后通过，structured-double-sink-red/green.log。
- 最新发布App实测: passed — 12-structured-release.png正常获取2模型，13-structured-console.png及structured-real-app.log证明四层嵌套+Bearer/Basic合成错误401保留诊断且不残留敏感尾部。
- 已知限制: 未使用用户真实凭据逐家联网验证；常见服务商和中转兼容性以自动化用例和本机模拟验证。
- 已知限制: 原有 NU1701 与 Markdown 空引用等编译警告不属于此次改动。

## 阻塞项

_无。_

## 风险与跳过的工作

- 未用真实账户逐家联网验证供应商；目录兼容性依据当前自动化测试及本机代理模拟，不能外推未知站点必然有模型目录。
- 深度预算按结构/字符串递归共同计数，深层、无效或截断结构化正文保守隐藏，可能减少上游错误细节；请求地址、状态、耗时和异常类型仍保留。
- 已有NU1701包兼容性及Markdown空引用编译警告未在此次只读验收中处理。
- 宿主采用skill-coordinated验证，是否接受独立验收结果由Runtime要求的用户确认决定；本Verifier不替用户接受结果或归档。

## 之前的迭代

| 目标周期 | 迭代 | 尝试 | 结果 | 未解决项 | 摘要 | 完成时间 |
| ---: | ---: | ---: | --- | --- | --- | --- |
| 1 | 1 | 1 | fail | A4 | 独立只读验收完成：A1/A2/A3/A5/A6/A7 通过，A4 因 JSON 转义凭据脱敏泄漏失败。正式完整测试 Core 394、App 108 均通过，最终 exe 哈希匹配；追加 Runtime 探针证明两个敏感值脱敏断言失败。请 Runtime 返回 Build，Builder 修复 ModelCatalogService.Sanitize 并补齐回归后重新提交候选。未修改源码、需求、设计或既有验收证据，未接受结果、归档或执行 Git 操作。 | 2026-10-26T10:03:36.789Z |
| 1 | 2 | 1 | fail | A4 | 第二轮独立只读复验完成：逐项评估 A1-A7，A1/A2/A3/A5/A6/A7 通过，A4 失败。当前正式完整回归 Core400/App111 均通过且最新包哈希匹配；原修复样例确已通过，但追加两个 Runtime 有界探针分别确认认证/JSON 组合 3 项和嵌套 JSON 4 项合成凭据尾部泄漏。请 Runtime 返回 Build 修复 A4 后提交新候选。仅在系统临时目录写入启动/检查/结果 JSON 与合成探针，既有源码、需求、设计和证据未手工修改；未派生 agent、操作 GUI、接受用户结果、归档或执行 Git 提交/合并/推送。 | 2026-10-26T10:32:43.710Z |
| 1 | 3 | 1 | pass | — | 第三轮独立只读验收完成：A1-A7全部通过；原第二轮失败探针11项和新增安全边界28项共39项经Runtime全部通过。当前完整Core415/App112正式记录及5条check证据组合摘要匹配；最新包哈希、实际App截图及嵌套认证401控制台/文件双写均核实。未修改源码、需求、设计、既有证据或手写Runtime状态；只在系统临时目录新增本轮JSON/合成探针，并经公开CLI登记/检查/提交。未派生agent、激活/终止App、截全屏、Git提交合并推送、接受结果或归档。请保留真实风险并按Runtime continuation等待用户决定。 | 2026-10-26T11:11:37.136Z |



## 结论

第三轮独立只读验收完成：A1-A7全部通过；原第二轮失败探针11项和新增安全边界28项共39项经Runtime全部通过。当前完整Core415/App112正式记录及5条check证据组合摘要匹配；最新包哈希、实际App截图及嵌套认证401控制台/文件双写均核实。未修改源码、需求、设计、既有证据或手写Runtime状态；只在系统临时目录新增本轮JSON/合成探针，并经公开CLI登记/检查/提交。未派生agent、激活/终止App、截全屏、Git提交合并推送、接受结果或归档。请保留真实风险并按Runtime continuation等待用户决定。
