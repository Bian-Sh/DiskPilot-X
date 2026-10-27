# 样式令牌（VisionPro）

> **何时读我**：**写任何自定义样式之前**。视图一律继承宿主主题，只允许引用本表列出的资源键，**不能自己写死颜色**。

样式统一由宿主提供（卡片圆角、玻璃底、语义色）。`theme="visionpro"` 时，以下资源在窗口与气泡中均可用。

## 背景与表面

| 资源键 | 用途 |
|---|---|
| `VP.BackgroundBrush` | 窗口/页面底 |
| `VP.BackgroundAltBrush` | 次级页面底（**窗口根容器建议用它**） |
| `VP.SurfaceBrush` | 卡片表面 |
| `VP.SurfaceHoverBrush` | 悬停 / 内嵌"井"底（海报占位底） |
| `VP.SurfaceActiveBrush` | 选中态表面 |
| `VP.CardBrush` | 卡片底 |
| `VP.OverlayBrush` | 遮罩 |

## 文字

| 资源键 | 用途 |
|---|---|
| `VP.TextPrimaryBrush` | 主文字（标题、大数字） |
| `VP.TextSecondaryBrush` | 次要文字（副标题、说明） |
| `VP.TextTertiaryBrush` | 弱化文字（占位、单位） |

## 强调与语义色

| 资源键 | 用途 |
|---|---|
| `VP.AccentBrush` | 强调色（数值、条形图填充、引导条） |
| `VP.SuccessBrush` | 安全 / 可清理 |
| `VP.WarningBrush` | 谨慎 / 需复核 |
| `VP.DangerBrush` | 危险 / 勿动 |
| `VP.WarningSurfaceBrush` | 警告卡片底 |

## 边框

| 资源键 | 用途 |
|---|---|
| `VP.BorderLightBrush` | 卡片边框 |
| `VP.BorderSubtleBrush` | 分隔线、表格线 |
| `VP.BorderAccentSoftBrush` | 强调描边 |

## 圆角与字体

| 资源键 | 用途 |
|---|---|
| `VP.CardCornerRadius` | 卡片圆角 |
| `VP.ControlCornerRadius` | 控件圆角 |
| `VP.SmallCornerRadius` | 小圆角 |
| `VP.WindowCornerRadius` | 窗口圆角 |
| `VP.FontFamily` | 字体 |

## 已有控件样式（直接 `StaticResource` 引用）

| 样式键 | 控件 |
|---|---|
| `VP.Button` | 普通按钮 |
| `VP.AccentButton` | 强调按钮（主动作） |
| `VP.DangerButton` | 危险动作按钮 |
| `VP.TextBox` | 输入框 |
| `VP.CheckBox` | 复选框 |
| `VP.ComboBox` | 下拉框 |
| `VP.Card` / `VP.GlassPanel` / `VP.GlassPanelLarge` | 容器卡片 |
| `VP.DataGrid` | 数据表 |

---

## 语义色怎么用

徽章 `badge` 的文本会被宿主映射成语义色：

| 文本（不区分大小写） | 颜色 |
|---|---|
| `Safe` / `安全` / `可清理` | 绿 |
| `Caution` / `谨慎` / `复核` | 黄 |
| `Danger` / `危险` / `保留` | 红 |
| 其它 | 强调色 |

**不要**在源码里判断颜色，只给语义文本。

---

## 排版建议

- 海报卡：竖版比例约 2:3（如 132×186），一行 3–4 个最舒服
- 统计卡：宽度 120–140，大数字 20–22px
- 表格：列数 ≤ 8，超出先聚合
- 区块之间留 14px 间距，别挤在一起
