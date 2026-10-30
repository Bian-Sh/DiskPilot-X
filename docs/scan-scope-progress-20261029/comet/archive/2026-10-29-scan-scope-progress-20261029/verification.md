---
generated_from_state_version: 8
---

# 验证

## 当前结果

- 结果: **已归档**
- 验证情况: **已完成检查，验证结果已确认**
- 目标周期: 1
- 迭代: 1
- 验证器尝试次数: 1
- 完成时间: 2026-10-29T01:15:40.406Z
- 摘要: 独立只读复核当前候选完成，A1-A5 全部通过，无必须修复问题或证据阻塞。复用三项有效 Runtime 检查，并独立核对差异、测试/发布PDB源码校验和、全量TRX、当前非提权进程状态/UIA、实际EXE清单、业务程序集等价性和发布源码探针。未启动管理员包、未执行真实Steam扫描或清理；未修改实现/规格/验收条件。

## 验收

| 编号 | 结果 | 来源 | 验收项 | 原因 |
| --- | --- | --- | --- | --- |
| A1 | passed | brief.md | A1 子目录首次扫描（包括带尾分隔符）只进入目录遍历，不尝试 MFT、不产生虚假 MFT 失败原因；实际数量及大小一致。 | 独立审阅 IncrementalFileScanner 的 useMft 卷根门控与 Fallback 条件，子目录和尾分隔符回归实际断言路线/无假回退/数量/大小；复用 scan-core-regression。原目录日志只有 FileScanner、无 Attempting MFT scan；后台 CaptureWatermark 仅建立目标树 FRN 索引，不枚举整盘 MFT。 |
| A2 | passed | brief.md | A2 卷根识别正确，整盘 MFT 和同路径 USN 增量分支保持可用；支持权限不足时真实回退及取消。 | 卷根 D:\、D:/、路径归一化和子目录判定测试通过；MFT 路线与同路径水位 USN 优先分支、取消回归通过。独立核对权限失败返回 null、上层真实回退及取消先于完成的代码链，原 USN 实现未改。未执行真实整盘 MFT/USN 扫描，不将分支测试声称为提权实盘验收。 |
| A3 | passed | brief.md | A3 整盘索引进度明确标注卷与索引记录数，不作为目标目录计数；构树/大小统计使用目标子树计数；简体、繁体、英文与无头入口一致。 | MftEnumerator 以卷根和 IsVolumeIndex=true 报告索引记录；构树/链接不沿用整盘总数，ParallelSizeCollector 仅计算目标树。普通与无头入口均调用 ScanProgressFormatter，简/繁/英资源及相关断言吻合；复用 scan-progress-localization。 |
| A4 | passed | brief.md | A4 单元回归通过，并使用非提权测试包 UI 扫描原始目标，得到 447 个文件、15 个文件夹（含根目录）、282794037 字节；不读取整盘 MFT。 | 复用三项有效 Runtime passed 记录，并独立解析全量 TRX：核心690通过/0失败，界面518通过/0失败/1既有跳过。PID49296路径和非提权令牌当前核实；只读 state 为447文件、15目录（含根）、282794037字节，后台UIA计数一致，结合原UI交互记录/WPF截图/日志确认无MFT；未重跑全量或另扫目标。 |
| A5 | passed | brief.md | A5 测试与管理员包均完整文件夹发布、包含运行时 DLL 和 skills；实际清单分别 asInvoker/requireAdministrator；真实发布目录中验证源码规则编译、授权的只读执行及失败诊断。记录真实源码来源，不宣称未做的提权后 UI 验收。 | 两个真实发布目录各453文件，运行时DLL与skills保留；直接读取EXE RT_MANIFEST分别asInvoker/requireAdministrator，哈希匹配原记录。Portable PDB校验和绑定当前源码/测试；实际3个业务DLL的全部8252方法IL/局部签名/异常区间、归一化MVID元数据、托管资源等价且进程未提权。只读读取发布进程源码探针确认编译/授权夹具执行及CS1519行列诊断；源码基线45fb5ba及未提交状态如实记录。未启动用户包或验收UAC/提权UI。 |

## 检查

| 检查 | 命令 | 工作目录 | 状态 | 退出码 | 耗时 |
| --- | --- | --- | --- | ---: | ---: |
| 扫描范围与路线回归 | test tests/DiskPilotX.Core.Tests/DiskPilotX.Core.Tests.csproj --no-build --nologo --filter FullyQualifiedName~ScanScopeRegressionTests\|FullyQualifiedName~ScannerExecutionTests --verbosity normal | . | passed | 0 | 1739 ms |
| 三语言进度与共享格式化回归 | test tests/DiskPilotX.App.Tests/DiskPilotX.App.Tests.csproj --no-build --nologo --filter FullyQualifiedName~ScanProgressLocalizationTests --verbosity normal | . | passed | 0 | 1727 ms |
| 真实发布、UI与源码规则证据核验 | -NoProfile -File work/scan-scope-progress-20261029/核验发布证据.ps1 | . | passed | 0 | 652 ms |

### Builder 报告的证据

以下为 Builder 报告，不等同于 Runtime 检查凭据或独立验收结果。

- 核心全量: passed — 690通过，0失败，0跳过，TRX已保存
- 界面全量: passed — 518通过，0失败，1既有跳过，TRX已保存
- 真实发布环境与UI: passed — 目标447/15/282794037，Steam只读规则与失败诊断、实际清单和令牌已核验
- 已知限制: 未运行管理员包的UAC交互或提权后UI；已实际读取其EXE清单并比较业务程序集。
- 已知限制: 非提权测试进程不能读取真实USN；USN保留由匹配水位的分支测试、原实现未改及回归证实，未额外扫描整盘。
- 已知限制: 已有国家旗帜彩色像素测试跳过，NU1701与无关可空警告未修改。

## 阻塞项

_无。_

## 风险与跳过的工作

- 管理员用户交付包未启动；结论仅涵盖其实际 requireAdministrator 清单和发布内容一致性，不涵盖 UAC 交互或提权后 UI。
- 真实整盘 MFT 和真实 USN 增量未执行；保留能力的结论来自当前源码分支、未改的底层实现、匹配水位/取消回归及权限失败语义核对；非提权原目录日志明确 USN 不可用。
- cua-driver PrintWindow 本次仍返回纯色，不作为视觉成功证据；复用已查看的应用内 WPF 截图，并以当前后台完整 UIA、只读状态与日志交叉验证。
- App 全量中的既有国家旗帜彩色像素测试为 NotExecuted，不计通过；已有 NU1701/无关可空警告未修改。

## 之前的迭代

| 目标周期 | 迭代 | 尝试 | 结果 | 未解决项 | 摘要 | 完成时间 |
| ---: | ---: | ---: | --- | --- | --- | --- |
| 1 | 1 | 1 | pass | — | 独立只读复核当前候选完成，A1-A5 全部通过，无必须修复问题或证据阻塞。复用三项有效 Runtime 检查，并独立核对差异、测试/发布PDB源码校验和、全量TRX、当前非提权进程状态/UIA、实际EXE清单、业务程序集等价性和发布源码探针。未启动管理员包、未执行真实Steam扫描或清理；未修改实现/规格/验收条件。 | 2026-10-29T01:15:40.406Z |



## 结论

独立只读复核当前候选完成，A1-A5 全部通过，无必须修复问题或证据阻塞。复用三项有效 Runtime 检查，并独立核对差异、测试/发布PDB源码校验和、全量TRX、当前非提权进程状态/UIA、实际EXE清单、业务程序集等价性和发布源码探针。未启动管理员包、未执行真实Steam扫描或清理；未修改实现/规格/验收条件。
