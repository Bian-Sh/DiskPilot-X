# 按需视图契约（SkillUiSpec）

> **何时读我**：要声明界面（视图）时。一份契约驱动**独立窗口完整渲染**，
> 气泡保留**一张醒目入口卡**，并直接内联明确声明的 `bar_chart` / `pie_chart` / `donut_chart`；表格和海报等复杂区块留在窗口。

## 顶层字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `title` | string | 视图标题（窗口标题栏 + 气泡内标题） |
| `subtitle` | string? | 副标题 |
| `surface` | `entry` / `auto` | 呈现面，默认 `entry`（旧值 `bubble`/`window`/`both` 仍兼容） |
| `entry_title` | string? | 气泡入口卡标题，留空回退到 `title` |
| `entry_summary` | string? | 气泡入口卡摘要 |
| `theme` | string | 固定 `"visionpro"`，继承宿主主题 |
| `sections` | `SkillUiSection[]` | 区块数组（最多 8） |
| `actions` | `SkillUiAction[]` | 窗口底部按钮（最多 3） |

## `surface` 语义

气泡里保留**一张入口卡**，有 `bar_chart` / `pie_chart` / `donut_chart` 时额外直接绘制图表：图标 + 标题 + 摘要 + 规模统计 + 「打开视图」按钮，整卡可点。
`surface` 只决定要不要**顺手把窗口也弹出来**：

| 值 | 气泡内 | 独立窗口 |
|---|---|---|
| `entry`（默认） | 一张醒目入口卡 + 图表（若声明） | 不自动打开，用户点击入口卡才开 |
| `auto` | 同一张入口卡 + 图表（若声明） | 立即自动弹出，呈现全部区块 |

> 旧值映射：`bubble` → `entry`，`window`/`both` → `auto`。

入口卡文案用 `entry_title` / `entry_summary`；两者留空时回退到 `title` / `subtitle`。
规模统计（几个区块、多少条目、占用合计、可勾选数量）由宿主自动汇总，源码不用填。

## `SkillUiSection`

| 字段 | 类型 | 说明 |
|---|---|---|
| `kind` | 见下表 | **必填**，决定布局 |
| `title` | string? | 区块标题 |
| `subtitle` | string? | 区块副标题 |
| `columns` | int | 仅 `poster_grid` 有效，每行卡片数 1–6，默认 3 |
| `headers` | string[]? | 旧表格列头，继续兼容 |
| `table_columns` | 列配置[]? | 内置表格列配置，最多六列；非空时优先于 headers |
| `items` | `SkillUiItem[]` | 条目（最多 60） |

### kind 取值

| kind | 布局 | 典型用途 |
|---|---|---|
| `poster_grid` | 固定列数卡片流：竖版海报 + 标题 + 数值 + 副文本 | 游戏封面、应用图标墙 |
| `stat_row` | 一行多个大数字卡 | 扫描统计、空间占比 |
| `table` | 内置主题表格：自适应列、三态全选、反选、选择汇总与长表滚动 | 多行结构化对比 |
| `bar_chart` | 标签 + 数值条 + 数值文本 | Top N 大小排行 |
| `pie_chart` | 饼状扇区 + 颜色图例 + 数值/百分比 | 少量类别的构成占比 |
| `donut_chart` | 环形扇区 + 中心合计 + 图例 | 构成占比与合计 |
| `text` | 标题 + 段落 | 说明、结论 |

## `SkillUiItem`（各区块按需取用字段）

| 字段 | 类型 | 用于 |
|---|---|---|
| `title` | string? | 主标题（游戏名 / 统计名 / 条目标签） |
| `subtitle` | string? | 副文本（**上次游玩时间**、说明、结论） |
| `value` | string? | 主数值文本（大小、数字） |
| `value_label` | string? | 数值单位/说明 |
| `badge` | string? | 语义色徽章文本（Safe / Caution / 危险…） |
| `path` | string? | 本地绝对路径，供回传与清理定位 |
| `appid` | string? | 应用标识；**宿主据此自动构造海报地址** |
| `image_url` | string? | 显式图片地址（优先于 appid） |
| `metric` | number | 条形图数值，按区块内最大值归一 |
| `size_bytes` | number | 占用字节数，用于汇总勾选总量 |
| `selectable` | bool | 是否允许用户在窗口里勾选 |
| `cells` | string[] | `table` 每行单元格 |
| `action` | `SkillUiItemAction?` | **条目行内动作**（卡片/行尾按钮，见下） |

> **海报提示**：给 `appid` 就够，宿主会确定性拼出竖版封面（`library_600x900_2x.jpg`），源码不用关心 CDN 地址。

## `SkillUiItemAction`（条目行内动作）

源码**只声明动作**，真正的落地执行一律由宿主完成：源码既不删文件、也不启进程。

| 字段 | 类型 | 说明 |
|---|---|---|
| `kind` | `uninstall` / `open_path` | 动作类型 |
| `label` | string? | 按钮文字，缺省 `卸载` / `打开位置` |
| `target` | string? | 目标：`uninstall` 填 **appid**（省略时取条目的 appid），`open_path` 填路径（省略时取 path） |
| `confirm` | bool | 是否要求强确认，**缺省 true**；只有显式写 `false` 才跳过 |

- `uninstall`：宿主读 Windows 卸载表里该应用自己登记的卸载命令（Steam 就是 `steam.exe steam://uninstall/<appid>`），
  先弹**强确认**（危险色 + 必须显式勾选），确认后交给应用自身卸载，完成与否会回传给 AI 做总结。
  卸载类动作会渲染成**危险色按钮**，和其它动作一眼就能区分。
- `open_path`：在资源管理器里定位该路径（非破坏性）。

> **带 uninstall 就不要再做清理分析**：宿主已经能在这一屏里把事情办完，再把目录回传去 analyze / publish
> 只会让用户多绕一圈。应用盘点类视图应当**止于卸载**。

## `SkillUiAction`

| 字段 | 类型 | 说明 |
|---|---|---|
| `kind` | `preview` / `continue` / `close` | 动作类型 |
| `label` | string | 按钮文字 |

- `preview`：请助手预览选中条目的清理候选（先扫真实数据，不清理）
- `continue`：把勾选结果回传给助手继续（可进入清理分析，清理前仍须确认）
  **仅在用户明确要继续清理分析时使用**；带 `uninstall` 的视图禁止携带 `preview`/`continue`
  （宿主会在 Normalize 里剥掉，应用盘点止于卸载）。
- `close`：关闭窗口

---

## 质量准则

1. **能结构化就别写段落**：≥3 行的数据用 `table`，要视觉冲击用 `poster_grid`。
2. **给决策信息，不只给数字**：游戏卡除了大小，尽量给出"上次游玩"——用户才知道该不该删。判定"有没有开启过"要用数据源头的语义值（如 Steam appmanifest 的 `LastPlayed=0` 就是"从未启动"），别拿文件时间戳硬凑一个"5 年前"。
3. **别堆卡片**：`poster_grid` 超过 12 个就只放前 12 个，其余交给表格区块；气泡保留入口卡与明确声明的条形图，其余完整数据在窗口里看。
4. **标题说人话**："游戏库占用排行" 好过 "Section 1"。
5. **能当场办完就别回传**：条目能挂 `action.uninstall` 的场景（游戏、应用盘点），
   就把动作给到卡片上，别让用户勾选 → 回传 → 再等助手分析；**流程止于卸载**。
6. **规则返回了 view 就别再 render_view**：技能源码（provider）返回的 `view` 宿主会直接呈现，
   模型再补一次 render_view 只会在气泡里多出第二张入口卡（用户明确不要）。


## 内置表格套件：创建技能直接声明

不要在 ui.xaml 重建结果表格，也不要生成 WPF Window 或事件。Provider 返回 SkillUiSpec 的 Table 区块即可；仅收集输入参数的任务表单仍走原契约。

旧 headers/cells 无需迁移：中间列默认随内容测量，首末长文本列获得剩余空间。需要精确布局时使用 table_columns：

| JSON 字段 | C# 属性 | 约束 |
|---|---|---|
| header | Header | 列头，支持 {loc key} |
| width | Width | 字符串 auto、固定像素（如 100）或比例（如 2*），比例 >0 且 ≤10 |
| min_width | MinWidth | 32–1600，默认 64 |
| max_width | MaxWidth | 不小于最小宽，最大 1600，默认 1600 |
| align | Align | left / center / right，默认 left |
| wrap | Wrap | 是否换行，默认 false；不换行时省略并显示完整提示 |

列宽会约束在 min_width/max_width 中；非法输入安全降级。最多六列，cells 按列顺序填写。数值/容量列建议右对齐、盘符列 auto，详情列 2* 并 wrap=true。缩窄到最小列宽总和以下时可横向滚动，不压坏数据。


table_columns 示例（所在区块 kind=table）：

```json
{"kind":"table","table_columns":[{"header":"项目","width":"1.3*","min_width":140},{"header":"所在盘","width":"auto","min_width":64,"max_width":96},{"header":"Unity 版本","width":"auto","max_width":160},{"header":"缓存合计","width":"100","align":"right"},{"header":"缓存构成","width":"2*","min_width":180,"wrap":true}],"items":[{"title":"示例项目","path":"E:/Unity/Example","selectable":true,"size_bytes":1048576,"cells":["示例项目","E:/","2022.3","1 MB","Library 1 MB"]}]}
```

源码使用同一套契约：

```csharp
new SkillUiSection
{
    Kind = SkillUiSectionKind.Table,
    TableColumns =
    [
        new SkillUiTableColumn { Header = "项目", Width = "*", MinWidth = 140 },
        new SkillUiTableColumn { Header = "磁盘", Width = "auto", MaxWidth = 96 },
        new SkillUiTableColumn { Header = "容量", Width = "100", Align = "right" },
        new SkillUiTableColumn { Header = "详情", Width = "2*", Wrap = true }
    ],
    Items = rows
};
```

每行提供 Selectable=true 才可勾选。宿主自动提供表头三态全选与表内反选，仅修改当前表格可选行，不改变其它区块或不可选行；窗口底栏自动汇总，回传沿用原 SkillUiItem。不要为全选、反选或主题再声明 actions。actions 只声明窗口业务操作。表格不提供排序、筛选和编辑，不能向用户承诺这些能力。

## 聊天条形图
需显式声明 kind=bar_chart，items每项的metric使用真实可比较数值（如字节），value只是显示文本（如8.7 GB），不能只给value期待宿主解析单位。海报item带metric不会自动生成条形图。正数条形按区块最大值归一并随可用宽度伸缩；零、负数、非有限数不绘制条形，不伪造占用。图表同时可见于聊天和完整窗口；表格/海报仍不内联。避免过长，源码可明确说明只展示前N项（例如Steam前8项）。

## 统一透明主题
技能结果窗口和任务窗口自动跟随宿主全局透明材质开关、透明程度与明暗主题，表格底板由宿主统一处理。技能只声明数据，不设置整窗 Opacity，不为每个技能生成 GlassEnabled 等未披露字段；文字和业务控件保持正常不透明度。关闭透明或系统无法启用材质时自动使用实色主题背景，保持原生窗口按钮和单一圆角轮廓。
主题设置中的后台保持通透默认关闭；用户开启后，主窗口和技能窗口在后台仍保留玻璃材质的活跃外观，不获取输入焦点。该选项保存在宿主主题配置，技能不自行声明或覆盖。

## 宿主内置图表（共享组件，不自绘）

三类图表均由宿主SkillChartControl渲染，聊天和完整弹窗使用同一套组件。技能只声明数据，不附带绘图代码，不要求额外图表包。

- `bar_chart`：按最大metric比较大小，适合排行。
- `pie_chart`：有限正metric占合计比例，适合少量类别的构成。
- `donut_chart`：同饼图，并在中心显示metric数值合计；单位用subtitle说明。
- items共用title、metric、value；value是可读的带单位数值，metric必须同量纲且来自真实数据。title/value可用技能本地化词典。
- 饼环统一提供颜色图例、数值与百分比提示；明暗主题、透明背景和语言切换由宿主处理。不能通过Selectable让饼图参与清理勾选。
- 零值、负值和非有限值不生成条形/扇区；没有正数时显示空态。单项正确绘制全圆/环，多项极大数安全归一化，禁止伪造比例。
- 不为了“好看”默认追加图表。只有用户需要比较/占比时声明；图片类盘点优先封面，排行放在明细之后。占比类别建议不多于6项，其余由技能基于真实数据合并为“其他”，不要静默丢掉总量。

```json
{
  "title": "空间构成",
  "surface": "entry",
  "sections": [
    {
      "kind": "donut_chart",
      "title": "缓存构成",
      "subtitle": "合计单位：GB；示例数据，不代表当前扫描结果",
      "items": [
        { "title": "Library", "metric": 8, "value": "8 GB" },
        { "title": "Temp", "metric": 3, "value": "3 GB" },
        { "title": "Logs", "metric": 1, "value": "1 GB" }
      ]
    }
  ]
}
```

源码使用`SkillUiSectionKind.PieChart`或`SkillUiSectionKind.DonutChart`和相同`SkillUiItem`字段，旧BarChart/Text枚举数字不变。

## 宿主表格滚轮

内置table和任务面板DataGrid自动使用宿主条件滚轮转交规则：可滚时表内滚，边界或短表/空表时交外层。不要在某个技能里另写滚轮事件、叠加ScrollViewer或修改原生滚动步长；不需要额外JSON字段。

## 宿主图表动效
三类图表自动使用宿主轻量CubicEase/EaseOut：首次展示或更换数据时约400ms载入，悬停约150ms提亮/扇区轻微浮出，图例与扇区联动，移出复原。文字和图例不跟随整体缩放，真实比例和布局不变。主题、语言、窗口尺寸或滚动不会重播载入；Windows关闭客户区动画时直接最终态。无需技能声明动画字段，不要额外注入动画代码或周期性重建视图来触发动效。