# 动态 UI 主题统一验收

## 实现

- 动态任务窗复用 WindowTitleBar，标题绑定 Window.Title，隐藏应用级设置与侧栏按钮。
- 主界面、聊天卡片、AskUser 与任务窗复用窗口/卡片/控件圆角资源。输入和按钮模板集中在 VisionProTheme；动态下拉、复选框和候选表格复用同一主题。
- 表头、表格行、单元格、错误与提示颜色使用动态资源；长路径换行，大小和说明保持可见。
- 原生最大化按当前显示器工作区定位，不吞掉 WPF 的最小尺寸约束；圆角变化同步裁切，关闭时移除属性监听。
- 后台任务窗隐藏时恢复普通状态，最大化后收起可再次打开，保留输入和预览结果。

## 自动验证

- App 55 项通过，Core 264 项通过。日志：work/ui-theme-2026-10-25/app-tests.log、core-tests.log。
- 新回归确认任务窗自绘标题栏、共享控件样式、打开后画刷与圆角更新、表头动态颜色、原生最小尺寸。
- 两个窗口边界回归先失败后通过；失败记录：work/ui-theme-2026-10-25/window-regression-red.log。

## 发布包与 GUI

发布目录：D:\AppData\Visual Studio\Projects\SpaceMonger-Copilot\outputs\2026-10-25_22-11-31_动态UI主题统一

进程路径与 coreclr.dll 均指向发布目录，含 Microsoft.CodeAnalysis.CSharp.dll。提供“使用本机配置启动.cmd”，对子进程清除验收变量，正常加载用户配置。

使用 cua-driver 后台 UIA 打开任务窗、填写独立样本路径并只读预览。找到 1 项日志（36 bytes）；照片与日志均保留。结果记录：work/ui-theme-2026-10-25/gui-results.json、retained-files.json。

同一个任务窗完成浅色/深色切换，截图：final-task-light.png、final-task-dark.png。最大化后边界 3440×1392（当前工作区），关闭后重开恢复 780×700，原输入与预览结果仍存在。所有截图只截目标应用，没有截桌面或切换前台。

## 边界

沿用 cleanup-skill-creator 工作区，未提交、合并或清理其他会话产物。原有 .gitignore、创建器实现和设计文件均保留。本轮只调整样式、窗口行为与相关回归；已有编译兼容性和空值警告未扩大处理。
