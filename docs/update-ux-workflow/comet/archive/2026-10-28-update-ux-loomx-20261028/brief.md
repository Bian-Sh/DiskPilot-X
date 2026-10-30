# 目标
第一步完成并验证app更新模块，再由原生Git合入main。用户于2026-10-28明确要求分两步：先合更新模块，再从main实现安装包构建Action并发布v1.0.0。本change只负责第一步的实现与验证，不代办Git交付。
# 范围
共享更新状态、右上角入口、Loom-X风格紧凑无按钮日志浮层、设置更新分区、版本历史、自动检查与代理偏好、关于页只读状态。补齐点击弹窗外任意位置关闭，以及安装就绪时点击入口启动可见安装向导。
## 目录结构
### 新建
src/DiskPilotX.App/Controls/UpdatePopupPolicy.cs及ReleaseNotesScroll.cs负责浮层命中与滚轮；Converters/ReleaseNotesMarkdownConverter.cs只影响日志排版；Services/IUpdateService.cs提供可测试更新契约；Views/SettingsSections/UpdateSettingsSection.xaml及代码负责更新设置和历史；MainWindow.UpdateAcceptance.cs提供隔离验收；tests/DiskPilotX.App.Tests/UpdateExperienceTests.cs覆盖回归；docs/update-ux-design.md记录设计。
### 修改
WindowTitleBar、MainWindow、UpdateService、UpdateViewModel、SettingsPage、AboutPage、App.xaml.cs、AppSettings、三语资源和设置导航测试。
### 删除
无。
### 明确不建
第一步不创建GitHub Actions、安装包工程或发布tag；不引入新UI框架，不删除其他会话文件。
# 非目标
不合入主仓库其他会话未提交内容，不清理旧包，不自动安装。Git提交和合并使用原生命令，不由Comet归档代办。第二步从已合并main实现tag构建、Loom-X式Release Notes和v1.0.0，并生成更低版本测试包。
# 验收示例
- A1：发现新版本时右上角显示更新入口；待下载仅图标，不显示版本号；进度、失败、重试、就绪与设置页共享状态，检查与下载互斥。
- A2：日志浮层宽380、最大高度420，正文13行高19且紧凑，无任何按钮或下载状态；正文滚轮有效；移入浮层、滚动条或外部空白保持；移入外部可交互控件、点击弹窗外任意位置、失活或关闭时隐藏；不吞掉外部点击。
- A3：设置更新分区包含当前版本、自动检查、更新代理、手动检查、双栏历史、加载更多和加载/空/失败重试；分页失败保留列表、选择与页码，偏好持久化。
- A4：关于页更新区域只展示状态，无检查、下载、取消、重下或安装命令，其它关于信息保留。
- A5：更新回归、应用与Core测试通过；安装就绪时点击标题栏入口调用安装器，显示完整安装向导，启动失败或取消提权不关闭应用；无用户操作不启动安装。
- A6：主仓库outputs中生成完整自包含测试包与交付包，实际EXE清单为asInvoker/requireAdministrator；验证非提权UI、日志滚轮路由及真实发布源码规则编译、授权执行和失败诊断；明确未做的物理鼠标与管理员UI实测。
# 约束与决策
源码基于94ef690的codex/update-ux-loomx-20261028；合并前吸收main已提交变更，保留主仓库未提交修改。发布使用SelfContained=true、PublishSingleFile=false、PublishTrimmed=false，只保留文件夹和EXE不生成ZIP。2026-10-28用户明确选择先app合main、后Action从main发布，分离两步，不将旧工作树直接打tag冒充main。
