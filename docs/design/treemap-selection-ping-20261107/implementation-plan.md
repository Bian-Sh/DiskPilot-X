# Treemap 交互实施计划

> 执行者：采用独立子任务实施和只读复核；先写失败测试，再作最小实现，最后核对发布包。

**目标：** 交付多选、手动推荐、忙碌 toast 与地图式平滑 Ping。
**架构：** 复用 WPF/SkiaSharp、现有选择集合和 CleanupRecommendation；不新增依赖。宿主统一清理反馈，控件负责动画，视图模型负责实体与布局。
**技术栈：** .NET 10、WPF、SkiaSharp、xUnit。
**规格：** ../../treemap-selection-ping-workflow/comet/changes/treemap-selection-ping-20261107/specs/treemap-selection-ping/spec.md

## 任务一：手动推荐与忙碌反馈
- [x] 先在 RecommendationsViewModel 的批次/来源测试基础上增加重复、父子、保留选择和手动来源测试，观察失败。
- [x] 实现追加方法、宿主当前扫描与来源保护接入、非模态 toast 和删除忙碌反馈；不修改 Treemap 控件/视图/视图模型。
- [x] 运行局部测试，核对原 CleanupPlan 和保护检查保持不变。

## 任务二：多选与平滑 Ping
- [x] 测试现有 Ctrl 选择和右键行为，补视口最小平移、大小自适应、橙色独立描边、动画中间帧及打断测试，观察失败。
- [x] 修正选择与菜单接入；实现当前层级优先、异步布局稳定目标和可取消缓动动画。
- [x] 接入现有 focus_path、推荐/引用定位并测试失效路径。

## 任务三：验收和发布
- [x] 运行 App/Core 全测试及构建，记录实际结果。
- [x] 发布非提权测试包，用 cua-driver 仅应用窗口验收，验证发布环境 validate_skill_source 和安全 run_skill_source。
- [x] 独立只读 Verifier 逐项核对当前候选和证据，修复后新一轮复核。
- [x] 发布 requireAdministrator 完整用户交付文件夹，验证两个 EXE 的真实 manifest 和必要 DLL/skills。
