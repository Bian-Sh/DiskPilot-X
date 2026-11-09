# 日志性能与落盘实现计划

目标：保持 Serilog/WPF，以新日志驱动合并刷新、回收虚拟化和有界预览，提供可分列的真实日志与独立崩溃诊断。

规格：comet/changes/log-performance-20261109/specs/logging/spec.md。

约束：仅独立工作树；文案中文；不改业务；不删其他产物；自包含文件夹发布；双清单分别验证；不提交或合并。当前会话逐项执行，最终由 Comet 要求的新只读 Verifier 验收。

## 本轮控制台局部 UI

已确认：圆角搜索容器内左侧 placeholder、右侧搜索/圆底清空切换；目录入口移入右肩菜单；原总数改为菜单控制的等级 icon 与搜索结果条数。复用 Segoe MDL2 Assets、主题资源和现有筛选，统计只在批次或筛选变化时遍历有界集合，不增加轮询。先做真实 WPF 红测，再实现并重打双包验收；不改变日志预算、虚拟化和历史锚点。

- [x] 真实 WPF 红测在缺少 placeholder 时失败，新增交互测试与既有筛选、复制、清空和回收虚拟化测试通过。
- [x] 实现圆角搜索、图标切换、清空焦点、菜单目录与搜索结果等级统计。
- [x] 应用回归 873 通过、1 个既有跳过；核心回归 893 通过。
- [x] 新双包：主 outputs/2026-11-10_02-53-00_日志搜索UI_{测试包,用户交付包}，453 个文件，实读双清单和托管内容等价。
- [x] 新包后台 UI、十万条洪峰、历史锚点、五次切页、空闲停止、长日志、源码编译与只读运行、正常退出、受控崩溃及 TSV 解析通过。
- [x] 当前候选提交 Comet，6 项最终 Runtime 检查全部通过。
- [x] 新只读 Verifier 已逐项评估 A1-A9，全部通过，Comet 等待用户接受结果；未归档或执行 Git 交付。

证据：主 outputs/2026-11-10_02-55-00_日志搜索UI_验收 与 2026-11-10_03-00-00_日志搜索UI_退出验收。预算仍为协作让出而非硬实时：本机当前夹具记录单批最大 16.4173 ms、最大 128 次操作，包含 WPF 同步通知、统计更新及运行时停顿；不能宣称严格小于 8 ms。统计集合受 5000 条与文本容量限制，无新增空闲计时器。用户包仅验证 requireAdministrator 实际清单与内容等价，未实际运行提权后 UI。

## 任务 1：有界、按需合并采集

- [ ] 先增加日志洪峰回归：十万条输入不逐条通知 UI，隐藏时集合不变。
- [ ] 运行 `dotnet test tests/DiskPilotX.App.Tests --filter FullyQualifiedName~LogPerformanceTests`，确认旧实现失败。
- [ ] 在 UiLogSink 内用锁保护 Queue，最多 5000 条、4 Mi 个 UTF-16 字符，单条预览最多 1024 字符；没有无界 Dispatcher 队列。
- [ ] SetActive(true) 接入窗口可见状态；首次激活 Flush，后续新日志只启动一次 200 ms 单次 Timer，Flush 无版本变化则返回，Dispose 释放 Timer。
- [x] 首轮独立验收发现整快照 Reset 缺少时间预算，改为有界差量增删：每批最多 128 次操作，约 8 ms 后让出；同步通知无法中途抢占，计入实际耗时并在下一次操作前检查预算。
- [x] 界面通知与过滤在采集锁外执行；仅新日志启动 200 ms 合并窗口，未完成批次以 1 ms 单次调度继续，追上最新后停止调度；清空版本阻止陈旧快照复活。
- [ ] 运行洪峰、并发、清空、空闲和生命周期测试。

## 任务 2：TSV 与崩溃

- [ ] 增加按标准 Tab 解析器逐列还原中文、引号、换行和异常的红测。
- [ ] LogTsvFormatter.Format 写七列；FileLifecycleHooks 为新文件写 BOM 和中文表头；AppLog.Configure 使用 buffered、1 秒刷新、10 MiB 滚动、进程独立文件及 SelfLog 独立故障报告。
- [ ] CrashDiagnostics 直接写 TSV 和原可读报告，MiniDump 失败单独报告；finally 释放写入门闩，普通主日志 best-effort 同步关闭刷新。
- [ ] 运行 TSV、周期刷新、退出刷新与重复异常测试。

## 任务 3：真实虚拟化控制台

- [ ] 使用 ListBox 的原生 ScrollViewer、VirtualizingStackPanel 和 Recycling，避免外部无限高度 ScrollViewer。
- [ ] 保持单行主题文本、复制与上下文菜单；预览有限行；新增紧凑搜索、数量和跳尾按钮，六等级筛选独立。
- [ ] 批次更新仅按跟随状态滚动一次，向上阅读暂停，切换页保留状态；接入 Closed 和 UiSink.Dispose。
- [ ] 运行控制台与主题回归。

## 任务 4：发布验收与独立验证

- [ ] 在仅验收环境增加 log_fixture/log_state/log_crash 指令，受控异常只作用于本任务测试进程。
- [ ] 用 publish-release.ps1 新建主 outputs 的测试与交付完整目录，分别实读 EXE 清单。
- [ ] Start-Process -WindowStyle Hidden 启动测试包，验 Path 和权限；cua-driver 只捕获应用窗口，测试日志洪峰、反复切页、搜索、筛选、历史滚动和清空。
- [ ] 读回周期与退出文件、标准解析 TSV、独立崩溃报告、MiniDump；运行源码 probe 覆盖只读 run_skill_source 与失败诊断。
- [ ] 提交 Comet 候选及可重复的 Runtime 检查，由独立只读 Verifier 核对 A1-A8，等待用户接受结果。
