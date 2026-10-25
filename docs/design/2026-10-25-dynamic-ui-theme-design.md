# 动态任务界面样式统一

动态任务窗复用 WindowTitleBar，使用各自 Window.Title；主窗口的设置和侧栏入口仅在主窗口显示。保留任务窗收起后重开、只读预览和宿主确认链路。

窗口、卡片、控件和局部圆角分别使用 VP.WindowCornerRadius、VP.CardCornerRadius、VP.ControlCornerRadius、VP.SmallCornerRadius。主视图、聊天卡片、AskUser 和动态 UI 引用同一组资源。窗口最大化使用直角并限制在当前显示器工作区；还原后恢复主题圆角。

动态输入框、按钮、下拉框、复选框和表格使用既有 VP 主题；AskUser 复用按钮和输入模板。表头、单元格、选中态和提示颜色均使用动态资源，切换主题后无需重新开窗。

验收包括 WPF 资源切换回归、相关测试、后台应用窗口检查、深浅主题截图和 outputs 自包含发布。
