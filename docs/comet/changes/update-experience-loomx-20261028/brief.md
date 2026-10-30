# 目标
参考 Loom-X 完善 DiskPilot-X 更新体验，复用现有更新服务，提供可滚动日志与统一状态。

# 范围
右上角更新入口、悬浮日志、设置更新页、关于页纯状态展示。用户已在对话中以“开干”确认五项方案、独立工作树和验证打包，并以“是”授权仅隔离新工作树中的阻塞旧流程记录副本。

## 目录结构
### 新建
- src/DiskPilotX.App/Views/SettingsSections/UpdateSettingsSection.xaml：更新设置和版本历史。
- src/DiskPilotX.App/Views/UpdateEntry.xaml：标题栏入口与日志预览。
- tests/DiskPilotX.App.Tests/UpdateExperienceTests.cs：状态与交互回归。
### 修改
- src/DiskPilotX.App/Services/UpdateService.cs 与 ViewModels/UpdateViewModel.cs：共享状态、设置、历史、错误反馈。
- src/DiskPilotX.App/MainWindow*、Views/SettingsPage*、Views/AboutPage*：入口接线与状态展示。
- src/DiskPilotX.Core/Models/AppSettings.cs：更新检查和代理偏好。
- src/DiskPilotX.App/Localization/Strings*.resx：新增界面文字。
- src/DiskPilotX.App/DiskPilotX.App.csproj：明确测试与交付清单。
### 删除
不删除产品文件或其他会话产物。
### 明确不建
不更换 UI 框架、不新建另一套更新器、不复制主目录未提交修改、不自动执行更新安装。

# 非目标
不合并主分支、不推送、不清理既有输出；不声称已验收真实 UAC 弹窗或生产安装。

# 验收示例
- A1：发现更新时右上角显示下载入口，下载进度、完成待安装与失败重试状态一致。
- A2：hover 显示更新日志，宽约 380、最大高 420 且受窗口可用高度约束，长日志可滚动。
- A3：指针离开按钮、跨空白区域、进入弹窗、滚轮及拖动滚动条均不关闭；进入弹窗外其他可交互控件时关闭。
- A4：设置更新页对齐 Loom-X：版本、自动检查、更新代理、手动检查、版本历史与日志，支持加载更多及失败重试。
- A5：关于页更新按钮仅展示状态，不再执行检查、下载或安装，所有入口共享状态。
- A6：单元回归和非提权发布包 UI 验证通过；文件夹包包含依赖和 skills，实际清单分别为 asInvoker / requireAdministrator，发布环境源码规则验证和只读执行通过。
