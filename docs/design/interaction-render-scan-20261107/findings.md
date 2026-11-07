# 前期核查与继续入口

## 源码边界
- 工作树：D:/AppData/Visual Studio/Projects/DiskPilot-X/.worktrees/interaction-render-scan-20261107
- 分支：codex/interaction-render-scan-20261107
- 基线：02e66a6126674575f3fea87d47845c611c55476a（开始时最新本地 main）。
- 应用代码未修改；仅本工作树 Comet 配置及本任务规格、设计、记录新增。
- 主仓库两份未提交需求文件及 context-governance 未跟踪目录保持不动；旧标题任务仍未归档，不处理它。

## 当前验证
- App 基线：758 通过、0 失败、1 跳过（国家旗帜彩色像素测试），总计 759，48 秒。
- Core 基线：888 通过、0 失败、0 跳过，21 秒。
- 这是未修改代码的基线，不是 Q1–Q10 的新增功能验收。
- 应用未修改，因此未创建新测试包或用户交付包。

## Q8 的真实会话证据
- 常规存储根由 App.xaml.cs:124–125 和 AppCompatibility.ResolveDataPath 确定。
- PowerShell LocalApplicationData 与资源管理器 Shell.Application 原生命名空间 Self.Path 均核对为 C:/Users/BianShanghai/AppData/Local/DiskPilot-X/Conversations。
- 开始核查时最新文件：4d130ec7a19a44a3ba4df75303210391.json，只读；未读取 Codex 对话代替，未复制完整私密对话。
- 模型工具结果第 21 条 publish_recommendations 成功，第 25 条 cleanup_recommendations 返回宿主误拒；发布后选择仍在，不是没有推荐。
- 强候选因果链：RecommendationsViewModel.SetExternalRecommendations 只清 IsWaitingForExternalRecommendations，不清 IsAnalyzing；SetAnalysisTask 会按 Running 再置 IsAnalyzing；ExecuteSharedCleanupAsync 入口把 IsAnalyzing 与磁盘忙碌合并拒绝。此时确认窗口尚未调用，不能解释成用户未批准。
- 尚未用失败回归或断点证明具体运行分支，不宣称已修复。实现应区分 AI 分析生命周期、真实扫描/发布/删除忙碌，而不是删除安全守卫。

## Q9 的当前代码证据
- MangoDisk WindowsPlatform.fast_analysis_records 委托 windows/file_layout/mod.rs:253 的 analyze_records。
- parser.rs 使用 FSCTL_QUERY_FILE_LAYOUT；输出缓冲 8 MiB。收集卷布局后 resolve_directory_path 与 prepare_analysis 裁剪所选根与保护路径。
- analyze_records 遇名称排除、排除根、显式禁用及不支持卷/平台错误返回不支持或失败；上层使用目录扫描，避免混合半份原生结果。
- 已检查这条 Windows fast-analysis 管线，没有发现 rayon/par_iter/std::thread 的直接并行实现；不据此推断 MangoDisk 全项目没有并发。
- DiskPilot-X ScanRoutePolicy 在目录缺历史范围/MFT成本样本时默认 DirectoryTraversal；MFT 保守优势还须达到 25% 且至少 2 秒。这是目录首次走普通扫描的一条确定策略路径，但尚未读取当次扫描日志证明用户 D:/AppData 命中了哪个原因码。
- IncrementalFileScanner.SaveMftState 及目录扫描后的 CaptureWatermark 保留 CachedSession 与 FRN 索引，门槛 500000 节点；Changes.DetachCachedState 克隆旧树。应测并减少这种结果缓存，而不是把清理批准证据一并删除。
- 快扫耗时、内存、线程收益尚未实测，不宣称已得性能结论。

## Q1–Q7/Q10 的核查
- 推荐路径 Click 与列表 MouseDoubleClick 共用 RecommendationActivated，实际入口切缓存扫描并导航；须拆 Ping 与双击动作。
- TreemapControl 当前在状态框前绘制 DrawSemanticTitles，删除框 2.5 px；须顺序调整并细化 Deleted。
- folder 标题 FormatLabel 返回 null 时只留背景；文件标签仍由底图栅格化，须对照连续边界效果验证。
- CleanupService 使用 UIOption.OnlyErrorDialogs，应优先复用系统删除/回收窗口。
- Q10 应复制 ConversationStore 校验 GUID 后生成的真实 JSON 绝对路径，不让 UI 自行猜存储目录。

## 流程状态与恢复
- Comet 当前 Shape/await-user，stateVersion=2，A1–A12 全部待验。
- 已整理完整规格与小步方案；按 Comet 与 brainstorming 的显式确认门槛等待当前完整方案确认，不能伪造审批直接 Build。
- 用户确认后在上述工作树继续：comet native next interaction-render-scan-20261107 --summary <真实确认摘要> --confirmed --expected-state-version 2 --expected-action confirm-shape。
- 默认分批：Q1/Q2/Q3/Q10 → Q4/Q5/Q6 → Q7/Q8 → Q9 → 清理回归、双包发布、真实 UI、独立审查。
- 旧工作树、分支、旧验收记录保全包不属于本任务处理范围。
