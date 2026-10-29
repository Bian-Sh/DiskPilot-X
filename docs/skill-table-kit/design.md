# 技能表格内置组件设计

## 已确认范围
用户于 2026-10-28 确认方案及独立工作树实施。仅此工作树的 Comet artifact_root 使用 docs/skill-table-kit，不迁移旧记录。

## 选择的实现
复用原生 WPF DataGrid，局部主题模板提供圆角容器、表头、轻分隔线、悬停与勾选态；不引入新依赖。组件接收 SkillUiSection，数据与表头共享同一组列，不再各用 UniformGrid。

## 数据契约
保留 headers/cells；可选 table_columns 声明 header、width（auto/固定数值/比例*）、min_width、max_width、align（left/center/right）、wrap。旧表格默认中间列随内容测量、首列和末列按比例伸展。列数最多六列。选择只修改 SkillUiItem.Selected，不改变清理与回传契约。

## 交互
表头三态复选框与各可选行同步；部分选中点击变全选；反选仅影响当前表格的可选行。组件在表格顶部工具条提供局部选择摘要、反选（避免被外层长内容滚动遮住）；窗口底栏继续提供跨区块汇总。不可选表不展示选择控件，空表有提示。长表内部滚动并保留表头。键盘与自动化控件命名可访问。

## 外壳
去掉技能窗口标题上方的蓝横线。避免原生 DWM 圆角与自定义圆角叠加产生两套轮廓；复用同一圆角策略、最大化归零，并检查真实应用截图。

## 实施顺序
1. 契约与选择/布局回归测试先失败。
2. 最小契约与 DataGrid 组件，接入共享模板与本地化。
3. 修复圆角与标题，更新 skill-creator 契约及示例。
4. 全量回归，发布两个完整文件夹包，验证真实清单与源码编译执行。
5. cua-driver 应用窗口验收，独立 Verifier 审核，保留证据；未经用户验收不合并或归档。

## 独立验收修复记录
2026-10-29 首次独立验收发现：旧表格在最大化达到 MaxWidth 后恢复，WPF 比例列仍保留失效显示宽度。补充真实 SkillWindow 最大化还原测试，先复现 483 → 180，再在表格视口宽度变化后以原比例值重新测量；保留用户已调整成固定宽度的列。修复后该测试通过，原失败报告与截图保留。

## 第三轮视觉修复方案（用户已要求继续）
基线同步到main 74ca8e1。窗口底色改为不透明主题底色，边缘只保留宿主主题轮廓，禁用多余原生边框色以避免黑边；用真实截图而非仅半径断言验收。聊天卡阴影下沉到独立背景兄弟层，前景文字不带Effect，使用Display/ClearType与像素布局。动态标题栏隐藏品牌区，主标题与窗口按钮合并顶区，保留Window.Title。共享区块模板使用简单主题圆角条代替系统ProgressBar，条宽绑定可用宽度及有限正metric；SkillUiSpec仅暴露BarChart区块给聊天，其他仍留窗口。Steam Provider补显式占用图表，文档和工具说明同步。

## 放大截图后的圆角纠正
不能以无黑色像素等同于圆角合格。用户放大图暴露 WPF 圆角 Border/Clip 与 WindowChrome 人工 HRGN 仍叠出白色错位轮廓。最终改为 DWM 唯一原生外轮廓：GlassFrameThickness=-1、WindowChrome.CornerRadius=0，内容 Border 圆角/边框为0且无 Clip；DWM 圆角偏好 Round，BorderColor=None。保留不透明主题底层、缩放边界和最大化约束。验收包含应用窗口放大四角，不再使用旧的主题半径等于 HRGN 断言，旧测试改为禁止多重裁切。
## 空白更新按钮纠正
动态窗口复用标题栏时，UpdateButton旧逻辑只绑定IsUpdateEntryVisible，无UpdateVM时回退Visible并留下MinWidth46。现在以ApplicationToolsVisibility和UpdateVM.IsUpdateEntryVisible双条件控制，缺值统一Collapsed；结果窗与任务窗测试先复现Visible，再验证Collapsed/ActualWidth0，另覆盖主窗口有更新时仍显示。
## 透明材质继承（用户确认）
每窗根据自己的DWM材质状态设置背景：成功启用时窗口底层透明、唯一内容底层使用带最低8%主题色的统一透明度，关闭或失败时窗口底层实色且内容底层透明，避免同一半透明色叠加两遍。表格使用该窗局部的轻量材质底板，而不是全局不透明BackgroundAlt。前景Opacity恒为1。结果窗和任务窗共用HostWindowChrome订阅全局主题变化；不存强引用窗口列表，关闭时退订。保留DWM唯一外轮廓，禁止重新启用HRGN或WPF圆角Clip。验收以目标窗口WGC合成帧比较低/高透明度、明暗、关闭材质及输入选择保留。
## 后台保持通透实现决定
用户明确要求主题设置新增默认关闭的KeepGlassWhenInactive。探针否决了两条成本不合适的路线：旧Accent接口在本环境未保持预期材质；Windows App SDK独立Backdrop探针能保持材质但覆盖WPF前景，不能据此交付，也不把探针依赖加入正式工程。最终使用标准WM_NCACTIVATE非客户区绘制通知保持DWM材质的活跃外观：只固定非客户区绘制状态，不发送WM_ACTIVATE、不调用SetForegroundWindow，真实Window.IsActive和输入焦点保持不变。Win32探针已显示后台玻璃、前台句柄始终相同且不是目标窗口。启用时挂本窗口消息钩子，禁用时移除并按真实IsActive恢复绘制，关闭时退订；仅材质启用成功时生效。所有实际主题色与Opacity仍由统一主题服务管理，不改配色。
## 标题栏按钮顶边对齐（2026-10-29）
用户截图指出技能窗口按钮偏下。真实旧包UIA测量与回归均证实共享36高标题栏中的32高按钮默认垂直居中，产生2 DIP顶部空隙。共享VP.TitleBarButton显式VerticalAlignment=Top，最小化/最大化/关闭统一贴标题栏顶边，宿主与两类动态窗口同源；不增加负Margin、不移动标题文字、不改变按钮32高或窗口非客户区策略。新增三类布局回归先失败（Y=2），再验证Y=0，发布包UIA核对真实屏幕坐标。

## 2026-10-29 共享图表与滚轮扩展（用户已确认）
main基线更新632a5f9，保留本任务改动。共享DataGrid滚轮附加行为通过主题默认启用，边界规则从更新页复用，不改浮层ReleaseNotesScroll的封闭滚动语义。原生冒泡事件只发给一个目标，保持逻辑/像素滚动步长，最近外层不跨窗口。
图表新增SkillChartControl：bar_chart迁入同一控件，pie_chart/donut_chart采用WPF矢量Geometry，不加第三方图表包；旧Text等枚举数字保持。数据共用items.title/value/metric，不新增图表配置字段；环中心合计为metric的数值合计，单位由区块副标题说明，饼环有限正值先除最大值再求和避免溢出；图例与悬停提示使用相同颜色和百分比，全部无效明确空态。环中心合计按安全格式显示，主题变化更新派生色，关闭/卸载退订，文字不随整层Effect或Opacity合成。
Steam只调整区块顺序stats/posters/table/chart。图表组件只响应显式声明，不给其他技能自动插入图表。

## 图表轻量动效（用户已确认）
主线同步330a40a，沿用WPF CubicEase/EaseOut，不增加skill配置或第三方包。SkillChartControl持有一次共享RevealProgress动画（400ms），只更新图形：条形ScaleX、饼环按每片原起止角插值，文字仍静态锐利。每个Section实例只首次Loaded/替换数据时播放；主题/语言/尺寸重建图形保持当前progress，不重置时间。
扇区hover使用本地画刷ColorAnimation和TranslateTransform，150ms提亮/向外4 DIP；图例使用同一激活函数。独立透明命中扇区保留最终几何，避免移动的图形边界造成hover闪烁。条形仅本地画刷提亮，不影响原宽度绑定。替换图形/卸载时停止旧画刷/变换时钟，生命周期内订阅SystemParameters，Windows ClientAreaAnimation关闭立即结束并回到最终态，卸载退订。测试分别验证动画中间值、终值、重播边界、悬停联动/复原与系统禁动策略。

### 最终时钟起点约束
真实发布空闲后复测否决生产中的SeekAlignedToLastTick策略（会沿过期合成tick提前完成）。RevealProgress使用私有SetValue基值先置起点，HoldEnd保证完成态，由ClockController.Begin重新排定开始；移除/卸载时Remove旧时钟并恢复1。测试才用Seek抽样，生产不Seek，不追加自定义定时器。
