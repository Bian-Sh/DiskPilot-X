# 按需视图契约（SkillUiSpec）

> **何时读我**：要声明界面（视图）时。一份契约驱动**独立窗口完整渲染**，
> 气泡里始终只放**一张醒目入口卡**（不内联渲染数据区块）。

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

气泡里**永远只有一张入口卡**：图标 + 标题 + 摘要 + 规模统计 + 「打开视图」按钮，整卡可点。
`surface` 只决定要不要**顺手把窗口也弹出来**：

| 值 | 气泡内 | 独立窗口 |
|---|---|---|
| `entry`（默认） | 一张醒目入口卡 | 不自动打开，用户点击入口卡才开 |
| `auto` | 同一张入口卡 | 立即自动弹出，呈现全部区块 |

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
| `headers` | string[]? | 仅 `table` 有效，列头 |
| `items` | `SkillUiItem[]` | 条目（最多 60） |

### kind 取值

| kind | 布局 | 典型用途 |
|---|---|---|
| `poster_grid` | 固定列数卡片流：竖版海报 + 标题 + 数值 + 副文本 | 游戏封面、应用图标墙 |
| `stat_row` | 一行多个大数字卡 | 扫描统计、空间占比 |
| `table` | 列头 + 等宽列的行 | 多行结构化对比 |
| `bar_chart` | 标签 + 数值条 + 数值文本 | Top N 大小排行 |
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
- `close`：关闭窗口

---

## 质量准则

1. **能结构化就别写段落**：≥3 行的数据用 `table`，要视觉冲击用 `poster_grid`。
2. **给决策信息，不只给数字**：游戏卡除了大小，尽量给出"上次游玩"——用户才知道该不该删。
3. **别堆卡片**：`poster_grid` 超过 12 个就只放前 12 个，其余交给表格区块；气泡里只留入口卡，完整数据在窗口里看。
4. **标题说人话**："游戏库占用排行" 好过 "Section 1"。
5. **能当场办完就别回传**：条目能挂 `action.uninstall` 的场景（游戏、应用盘点），
   就把动作给到卡片上，别让用户勾选 → 回传 → 再等助手分析；**流程止于卸载**。
