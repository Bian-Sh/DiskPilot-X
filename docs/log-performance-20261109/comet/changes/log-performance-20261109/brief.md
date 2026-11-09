# 目标

复刻 Loom-X 日志系统的有效机制，在 DiskPilot-X 中解决切换控制台卡顿和日志洪峰造成的响应与内存风险。普通日志和独立崩溃日志必须落盘，以 Tab 分列供表格软件读取。

# 范围

日志采集、显示缓存、批量刷新、控制台虚拟化与交互、滚动文件输出、崩溃诊断、回归测试以及真实发布包验收。参考 Loom-X 的日志模块和 ConsoleView，但不迁移 Avalonia 或浏览器模块，也不照搬其无界待处理队列。

## 来源覆盖

覆盖边界仅为用户指定的 Loom-X 日志系统及控制台必要依赖。其他 Loom-X 功能为非目标。

| 来源 | 读取状态 | 保留行为 | 目标规格 | 验收 | 覆盖状态 |
| --- | --- | --- | --- | --- | --- |
| LoomX/Logging/RuntimeLogBuffer.cs | 完整 | 有界历史缓存与线程安全采集 | specs/logging/spec.md，缓存 | A1 | 已覆盖 |
| LoomX/Logging/LoggingBootstrap.cs | 完整 | 缓冲滚动落盘、Tab 分列 | specs/logging/spec.md，文件格式 | A4 | 已覆盖 |
| LoomX/ViewModels/ConsoleViewModel.cs | 完整 | 200 ms 合并、搜索与等级筛选 | specs/logging/spec.md，刷新和交互 | A1、A3 | 已覆盖 |
| LoomX/Views/ConsoleView.axaml | 完整 | 虚拟列表、复制、回到最新 | specs/logging/spec.md，视图 | A2、A3 | 已覆盖 |
| LoomX/Views/ConsoleView.axaml.cs | 完整 | 用户阅读历史时不强制滚动、切页恢复 | specs/logging/spec.md，交互 | A3 | 已覆盖 |
| 用户本轮补充 | 完整 | 必须落盘、独立崩溃日志、Tab 分列且不串列 | specs/logging/spec.md，文件格式和崩溃 | A4、A5 | 已覆盖 |
| 用户确认的控制台 UI 调整 | 完整 | 圆角搜索框、暗淡 placeholder、右侧搜索或圆底清空、目录入口移至菜单、菜单决定等级统计组 | specs/logging/spec.md，视图与交互 | A9 | 已覆盖 |

## 目录结构

### 新建

- src/DiskPilotX.App/Logging/ 内的缓存、TSV 格式化及必要集合辅助模块：有界输出和分列落盘。
- tests/DiskPilotX.App.Tests/ 日志性能、TSV、崩溃和滚动回归：验证真实行为。
- docs/log-performance-20261109/design.md：记录实现方案与测试入口。

### 修改

- src/DiskPilotX.App/Logging/UiLogSink.cs、AppLog.cs、UiLogEntry.cs：批量显示、缓存限制、缓冲文件输出。
- src/DiskPilotX.App/MainWindow.Console.cs、MainWindow.xaml、MainWindow.xaml.cs、MainWindow.ViewModels.cs：虚拟化、搜索、等级筛选、复制和滚动状态。
- src/DiskPilotX.App/Diagnostics/CrashDiagnostics.cs、验收服务及相关诊断分部：崩溃落盘与仅验收模式的可控日志夹具。
- src/DiskPilotX.App/App.xaml.cs、Localization/：生命周期和新增控制文案。
- tests/DiskPilotX.App.Tests/ConsolePanelTests.cs 等受影响测试：保留既有主题与操作约束。
- .comet/config.yaml：仅本工作树使用独立产物根，隔离继承的其他任务状态。

### 删除

无。不删除其他会话源码、规格、输出或用户日志。

### 明确不建

- 不新增日志框架、MVVM 重构或数据库：沿用 Serilog、WPF 和系统集合。
- 不迁移 Loom-X 的 Avalonia 界面或无界队列。

# 非目标

不修改扫描、分析、清理或安全授权业务；不默认降低详细日志等级；不清理用户旧日志或其他发布包；不执行真实用户数据删除；不自动提交、合并、推送、归档接受结果或生成 ZIP。

# 已确认决策

- 用户已明确确认前轮方案与从当前 main 建立独立 worktree，并补充日志必须落盘、有崩溃日志、使用 Tab 分列。
- 源码基础为 main 的 5f6d2371d6b2c26c4c90fa3aef3f7ee0c7c3d623，工作树与 main 不存在提交落后；主目录未提交文档不复制到新目录。
- 普通日志为 UTF-8 BOM 的 TSV，带列头；Tab、换行和引号使用标准分隔文本字段引用，完整消息与异常堆栈独立成列。
- 崩溃记录独立、同步写出并刷新；保留可读报告与 MiniDump，不依赖已经可能故障的 UI 日志队列。
- 界面缓存按条数与文本总量限制；超长消息用预览，完整内容仍落盘。隐藏控制台不刷新集合，打开后获取最新有界快照。
- 沿用主程序主题和日志语义色；提供搜索、完整等级筛选、复制和尾部跟随，阅读历史时不被拉回最新。
- 测试包 asInvoker、用户交付包 requireAdministrator，均为 self-contained 文件夹发布；最终只声明实际完成的验收。
- 搜索框使用圆角，左侧暗淡“搜索”为 placeholder；空内容时框内右侧为搜索 icon，有内容时替换为圆底清空 icon，点击清空并保留输入焦点。
- 工具栏不再显示文件夹按钮，“打开日志目录”移到右肩“...”菜单。原 xx/yy 改为日志等级 icon 与条数的多组统计，菜单勾选同时决定列表筛选和显示哪些组；按当前搜索结果计数，勾选等级即使 0 条也显示，默认 Info/Warning/Error/Fatal。

# 验收示例

- A1：至少十万条日志及持续并发输出时，缓存与待刷新数据有明确条数和文本总量上限；每次 UI 处理有预算，清空后旧待处理日志不会重新出现，隐藏面板不刷新集合。
- A2：在真实测试包打开与反复切换控制台、调整窗口、滚动时使用回收虚拟化，可视日志容器数量明显小于缓存条数；输出洪峰后进程仍存活且 UI 可操作，记录耗时与内存而不虚构跨设备性能保证。
- A3：搜索、各日志等级筛选、复制、清空、回到最新可用；阅读历史时新日志不强制滚动，切页保留跟随状态；超长消息不拖垮布局，主题和既有清空入口保持。
- A4：真实发布环境普通日志按 Tab 分列，UTF-8 BOM 与列头存在；中文、Tab、换行、引号、异常类型和完整堆栈可由标准分隔文本解析器逐列还原；滚动、周期刷新和退出刷新生效，显示淘汰不丢失对应文件记录。
- A5：仅对本任务验收进程触发可控未处理异常，退出后独立崩溃 TSV 与可读报告存在，含时间、来源、进程、异常类型、消息、完整堆栈和运行环境；保留 MiniDump 或明确记录生成失败；主日志尽可能同步刷新，报告失败不递归崩溃。
- A6：日志文件输出故障有独立可读诊断，不能通过同一个坏掉的日志链反复记录；非致命或未观察任务异常不永久关闭后续崩溃采集，关闭和刷新不遗留定时器或事件订阅。
- A7：测试与用户交付包位于主仓库 outputs 的新时间目录，完整依赖和 skills 保留；实际 EXE 清单分别为 asInvoker 与 requireAdministrator，真实发布环境 validate_skill_source 和经授权的只读 run_skill_source 成功，错误源码诊断完整。
- A8：日志、控制台和受影响生命周期回归通过；源码来源、分支、基础提交和未提交状态可追溯，主仓库其他改动和其他会话产物保持不变，独立只读 Verifier 逐项评估全部验收后等待用户接受结果。
- A9：真实 WPF 和发布包验证圆角搜索、左侧暗淡 placeholder、框内右侧搜索/圆底清空切换及清空后焦点；右侧无文件夹按钮，目录入口在“...”菜单；原 xx/yy 位置按菜单勾选显示各等级 icon 与搜索结果条数，包含 0 条，全六组和窄窗口下不重叠，搜索、等级筛选及新增日志计数保持同步。
