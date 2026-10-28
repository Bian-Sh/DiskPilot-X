# 目标
修复清理确认弹窗及文件定位，在 Treemap 与文件树加入安全删除和持续到重扫的删除标记。

# 范围
## 目录结构
### 新建
- src/DiskPilotX.App/MainWindow.FileActions.cs — 右键删除协调、确认及安全执行。
- src/DiskPilotX.App/Services/DeletedEntryState.cs — 当前扫描内已删除路径与后代标记。
- tests/DiskPilotX.App.Tests/ — 弹窗、菜单、定位与删除标记回归测试。
### 修改
- src/DiskPilotX.App/Controls/AppModalHost.* — 居中有界自适应弹窗，正文滚动且按钮常驻。
- src/DiskPilotX.App/MainWindow.Cleanup.cs — 推荐清理确认只展示数量、大小和方式。
- src/DiskPilotX.App/Views/、Controls/TreemapControl.cs、ViewModels/ — 菜单、红框、整行红线及扫描生命周期。
- src/DiskPilotX.App/Services/FileEntryShellService.cs、Localization/ — 可靠定位及菜单多语言。
### 删除
无。
### 明确不建
不引入新的删除引擎、后台监视器、自动重扫和额外依赖，复用现有清理服务及设置。

# 非目标
不修改其他会话产物，不复制主仓库未提交改动，不自动合并、推送或清理工作树。

# 验收示例
- A1：通用弹窗居中随内容上下扩展，最大高度受宿主限制，长正文可滚动，标题和按钮始终可见。
- A2：推荐清理确认不铺满路径，只强调所选项数、总大小及删除方式，永久删除提示不可恢复，默认取消。
- A3：两种视图仅文件显示打开；定位按类型显示定位文件或定位文件夹，英文为 Show in Explorer；父目录选中目标，含空格中文路径和管理员环境不依赖直接启动 explorer.exe。
- A4：两种视图均可右键删除，必须再次确认，严格采用设置方式；失败不标红并报告原因，取消不删除。
- A5：删除成功保留扫描布局，Treemap 红框和 TreeView 整行贯穿红线跨选择、缩放、排序和视图切换保留；目录后代同样标记，禁止再次打开或删除。
- A6：下一次扫描开始清除删除标记，删除本身不触发重扫；保留现有推荐清理结果统计和安全防护。
- A7：新增回归测试及现有测试通过，使用非提权测试包完成应用窗口范围内的 UI 验收。
- A8：输出独立完整文件夹的测试包和用户交付包；实际 EXE 清单分别为 asInvoker 与 requireAdministrator；发布环境校验源码规则编译、授权执行和错误诊断，记录来源并保留依赖和 skills。

# 决策
用户已明确确认完整方案并选择新建 worktree；新工作树源自 main 的 74ca8e1，不包含主目录未提交修改。用户允许本地 Comet 初始化隔离恢复；仅调整本工作树配置指向独立产物根，未动任何旧任务状态。
