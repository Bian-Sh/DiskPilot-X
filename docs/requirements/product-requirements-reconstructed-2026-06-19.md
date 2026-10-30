# SpaceMonger Next 反推需求文档（2026-06-19）

## 背景

本项目早期只有 `specs/archive/2026-10-30-001-disk-space-analyzer/spec.md` 这份 SDD 规格，但 2026-06-14 到 2026-06-19 的多轮实现已经形成了大量迭代纪要、验收 checklist、TODO 和发布记录。本文以现有 `docs/`、`README.md`、`specs/archive/2026-10-30-001-disk-space-analyzer/` 以及当前源码结构为依据，反推当前产品需求，补齐原型和需求文档之间的空白。

本文不是替代源码或 SDD spec 的唯一事实源：

- 需求真值优先级：当前源码行为 > 已验收迭代纪要 > 未验收 TODO > 早期 spec 草案。
- 视觉与鼠标命中类结论若只通过 acceptance server 内部状态验证，标记为“待 PC Use 截图复验”。
- 早期 `spec.md` 仍是功能大纲，本文补充后续 UI、导航、诊断、发布和验收需求。

## 产品定位

SpaceMonger Next 是面向 Windows 10/11 的磁盘空间分析工具，以 SpaceMonger 风格 Treemap 作为核心交互，用 AI 辅助解释磁盘占用、给出清理建议，并提供可确认的删除/回收站清理流程。

产品目标：

- 让用户用一张 Treemap 快速理解“空间被谁占用”。
- 让用户在不读取文件内容的前提下，基于元数据获得 AI 清理建议。
- 让清理行为保持用户确认、可解释、可回退优先，而不是由 AI 直接执行。
- 让 Windows 桌面端体验接近现代文件管理器：可编辑路径、Back/Forward/Up、面包屑下拉、状态同步、中文本地化。

## 用户角色

- **普通 Windows 用户**：希望找出大文件、大目录、缓存、临时文件，安全释放空间。
- **开发者/高级用户**：需要识别 build cache、包缓存、日志、重复文件，并希望能看到详细诊断与 AI 原始交互问题。
- **维护者/测试者**：需要通过日志、控制台、acceptance server、发布目录和 checklist 快速复现 UI/导航问题。

## 核心用户旅程

### UJ-01 首次启动与配置

1. 用户启动应用，应用请求管理员权限。
2. 未配置 API key 时，扫描和 Treemap 可用，AI 推荐与聊天处于不可用或提示配置状态。
3. 用户打开内嵌设置页，配置 Anthropic API key、Anthropic Base URL、模型名、是否启用 thinking、响应语言。
4. 用户保存设置后，设置页退出并回到主界面。

### UJ-02 扫描并理解磁盘占用

1. 用户通过文件夹按钮或地址栏选择目标路径。
2. 用户点击 Scan，应用按当前显式选择路径或当前 Treemap 路径确定扫描目标。
3. 扫描过程中显示进度、允许取消，并在控制台输出关键状态。
4. 扫描完成后显示 Treemap、容量统计、文件/文件夹数量、空闲空间块。
5. 用户可点击矩形钻入目录，也可使用 Back/Forward/Up 和面包屑回退或切换路径。

### UJ-03 查看 AI 推荐并执行清理

1. 用户触发 AI 分析，系统向单一 LLM provider 发送筛选后的文件元数据。
2. 推荐结果按类别与安全等级展示，支持筛选、选择、批量选择和撤销。
3. 用户点击推荐项时，Treemap 跳转/选中对应文件或目录，辅助确认上下文。
4. 用户确认清理前必须看到确认对话框。
5. 清理完成后显示汇总，并从内存树移除成功清理项、刷新 Treemap。

### UJ-04 与 AI 聊天咨询

1. 扫描完成后用户可打开右侧聊天面板，不要求先运行推荐分析。
2. 用户围绕当前磁盘占用、选中项、系统文件、清理方法进行提问。
3. Chat 使用当前 Treemap 层级和选中项子树作为上下文，但不发送完整文件树。
4. AI 可提供命令建议，但只能显示和复制，不能直接执行。

### UJ-05 维护者自测与发布

1. 维护者修改 WPF 功能后执行 build。
2. 如果有实质性 UI/应用行为改动，应主动发布 Windows x64 folder package。
3. 对导航、鼠标命中、视觉问题，优先使用 acceptance server 或 PC Use 进行验收记录。
4. 每轮工程开发应在 `docs/` 新增迭代纪要，记录根因、改动、验证命令、发布产物和遗留风险。

## 功能需求

### FR-A 启动、权限与平台

- **FR-A01**：应用必须面向 Windows 10/11，使用 WPF 桌面体验。
- **FR-A02**：应用启动必须请求 UAC 管理员权限；用户拒绝时不应进入正常扫描工作流。
- **FR-A03**：扫描和 Treemap 基础功能不得依赖 AI API key。
- **FR-A04**：应用应保留英文与 `zh-CN` 本地化资源；新增 UI 文案必须同步两套资源。

### FR-B 路径选择与导航

- **FR-B01**：用户可通过文件夹选择按钮选择扫描目标路径。
- **FR-B02**：用户可通过地址栏/面包屑查看当前路径，并可进入编辑态输入路径。
- **FR-B03**：文件夹按钮、地址栏输入、Treemap 钻入、推荐项跳转必须共用一致的路径导航入口，避免 `SelectedPath`、当前根节点和显示路径状态分裂。
- **FR-B04**：如果路径在当前扫描树内，Treemap 必须跳转到该目录并保持扫描根关系。
- **FR-B05**：如果路径不在当前扫描树内，Treemap 可进入外部路径空态，并提示需要重新分析/无数据；下一次 Scan 必须优先扫描用户显式选择的外部路径。
- **FR-B06**：Back/Forward/Up 必须更新 Treemap 当前根、地址栏/面包屑、按钮可用状态。
- **FR-B07**：重复导航到当前路径不得向后退栈压入重复记录；该项在 2026-06-19 checklist 中暴露过 C08 风险，外部路径对象重建时必须用路径字符串去重。
- **FR-B08**：根路径或驱动器路径必须保留尾部反斜杠语义，避免 `C:\` 被错误规范化为 `C:` 导致 Treemap 进入外部空节点。
- **FR-B09**：面包屑 `>` 下拉必须列出同级/子级目录，目录过多时限制高度并可滚动，不应铺满屏幕。
- **FR-B10**：面包屑下拉应使用虚拟化列表，避免大量目录导致 UI 卡顿。

### FR-C Treemap 可视化

- **FR-C01**：Treemap 矩形面积必须按文件/目录大小比例展示。
- **FR-C02**：Treemap 支持点击目录钻入、悬停 tooltip、右键基础菜单（Open in Explorer、Copy Path、Properties）。
- **FR-C03**：Treemap 必须按文件类型/目录类别着色，并保留可识别的 SpaceMonger 风格配色。
- **FR-C04**：驱动器级视图必须显示 free space sentinel，让用户同时看到已用与可用空间。
- **FR-C05**：Treemap 空态必须区分“未扫描”“扫描中”“当前外部路径无数据/需重新分析”等场景。
- **FR-C06**：Treemap 中文目录名/文件名必须优先使用可显示 CJK 的字体链，不得出现小方块。
- **FR-C07**：Treemap 容器应有现代圆角和裁剪效果，但不得牺牲命中测试准确性。

### FR-D 扫描能力

- **FR-D01**：NTFS 卷应优先使用 MFT 枚举和并行 size collection，以提升大盘扫描速度。
- **FR-D02**：非 NTFS、网络盘或不可用快速路径必须自动回退到普通目录遍历。
- **FR-D03**：扫描必须识别 junction/symlink/reparse point，避免无限递归或错误推荐 link target。
- **FR-D04**：OneDrive Files On-Demand 等 cloud placeholder 应避免触发下载，大小按安全策略处理。
- **FR-D05**：权限拒绝、锁定文件、不可访问目录必须记录并继续扫描，不应中断全局扫描。
- **FR-D06**：扫描可取消；取消后 UI 和控制台状态必须恢复到可继续操作状态。
- **FR-D07**：增量扫描可使用 NTFS USN change journal，但其行为不得破坏全量扫描结果的准确性。

### FR-E AI 推荐

- **FR-E01**：AI 推荐使用单一 Anthropic-compatible provider，API key 由用户提供并保存在本机。
- **FR-E02**：设置页必须支持 Anthropic Base URL override；为空时回退默认 `https://api.anthropic.com`，环境变量仅作为启动前 fallback。
- **FR-E03**：推荐分析只发送文件元数据，不发送文件内容。
- **FR-E04**：发送给 LLM 的上下文应聚焦最大空间占用者和已知高 ROI 模式目录（temp/cache/log/build output）。
- **FR-E05**：推荐必须包含路径、大小、类别、安全等级、可读解释。
- **FR-E06**：推荐按类别分组，组内按大小排序，并支持 category/safety filter。
- **FR-E07**：推荐面板必须支持单项 accept/dismiss、全局 Select All Safe、Deselect All Caution、按类别批量选择。
- **FR-E08**：重新运行推荐分析必须清空旧结果和旧选择状态，避免不同分析范围的 stale recommendations 混用。
- **FR-E09**：推荐 JSON 提取失败时必须保留开发者诊断信息，便于定位 LLM 输出格式问题。
- **FR-E10**：推荐项点击应能导航 Treemap 到对应路径；路径不存在或不在当前扫描树时应给出合理反馈。

### FR-F 清理执行

- **FR-F01**：任何删除动作必须先弹出确认对话框。
- **FR-F02**：必须支持永久删除和移动到 Windows Recycle Bin 两种模式。
- **FR-F03**：清理过程中遇到锁定、权限拒绝、已不存在路径必须跳过并记录，不应阻断其他项。
- **FR-F04**：清理结束必须显示成功、跳过、失败和实际释放空间汇总。
- **FR-F05**：清理成功后必须更新内存中的 `FileEntry` 树、总大小、Treemap 布局和推荐统计。

### FR-G 聊天面板

- **FR-G01**：右侧聊天面板可折叠/展开，展开时 Treemap 横向让位，折叠时恢复空间。
- **FR-G02**：扫描完成后即可聊天，不要求先运行 AI 推荐。
- **FR-G03**：聊天上下文必须包含当前 Treemap 可见层级和选中项子树，不能发送完整扫描树。
- **FR-G04**：聊天是 advice-only；不得执行命令或直接修改文件。
- **FR-G05**：命令片段必须以代码块呈现，并提供 Copy 按钮。
- **FR-G06**：聊天历史在当前进程内保留，应用关闭后丢弃。
- **FR-G07**：聊天应限制在磁盘空间、文件用途、清理建议和系统存储管理主题。

### FR-H 主界面、控制台与设置

- **FR-H01**：主界面应提供顶部导航工具条、扫描按钮、设置入口、控制台筛选入口、Treemap 主区域、右侧 Chat、底部 Recommendations/Console tabs。
- **FR-H02**：底部 Recommendations 面板高度可通过 splitter 拖拽调整。
- **FR-H03**：底部面板和推荐列表不得遮挡或拦截 Treemap 底部 cell 的鼠标点击。
- **FR-H04**：控制台必须显示 info/warning/error 等日志，支持筛选，并写入 `logs/console-*.log`。
- **FR-H05**：设置应以全屏覆盖/内嵌页形态呈现，避免弹窗割裂主流程。
- **FR-H06**：空态文案、按钮 tooltip、设置页字段、控制台文案必须本地化。

### FR-I 验收、自测与发布

- **FR-I01**：应用必须提供维护者可用的 acceptance automation server，用于查询状态和驱动导航、编辑、点击等自测命令。
- **FR-I02**：视觉/真实鼠标类验收必须通过 PC Use 截图或真实鼠标命中确认；acceptance server 内部状态只能作为逻辑验证。
- **FR-I03**：WPF 应用发生实质性修改后，应主动执行 Release folder publish，产物放入 `outputs/SpaceMonger-win-x64-folder-*`。
- **FR-I04**：发布包是 folder package，分发时必须保留整个目录，不应只复制单个 exe。
- **FR-I05**：每轮迭代必须在 `docs/` 记录变更、验证命令、发布产物和遗留风险。

## 非功能需求

- **NFR-01 性能**：大目录扫描、面包屑下拉、推荐列表滚动都必须避免 O(全量 UI 元素) 的卡顿。
- **NFR-02 安全**：默认不推荐 OS 关键目录、用户文档标准目录、active binaries、正在使用的文件。
- **NFR-03 隐私**：AI 只接收元数据；用户应能从设置和说明中理解发送范围。
- **NFR-04 可诊断性**：扫描、AI 请求、JSON 解析、清理结果和 UI 自测必须有可追踪日志。
- **NFR-05 本地化**：中文资源文件真实编码必须保持正确；不得因 PowerShell 显示乱码而重写无关中文。
- **NFR-06 可维护性**：新增需求或修复必须沉淀到 `docs/`，并在必要时同步 `specs/archive/2026-10-30-001-disk-space-analyzer/spec.md`。

## 已知空白与待补齐项

| 编号 | 空白 | 来源 | 建议补齐方式 |
| --- | --- | --- | --- |
| GAP-01 | 缺少统一的产品需求文档 | 多个 iteration docs 分散记录功能 | 以本文作为当前 PRD 基线，后续迭代增量追加或拆分 |
| GAP-02 | 原 `spec.md` 未覆盖近期导航、面包屑、控制台、设置页、发布和自测要求 | 2026-06-15 至 2026-06-19 docs | 在 `spec.md` 增加 addendum 指向本文，并逐步回填 FR |
| GAP-03 | UI 原型缺失 | 截图仅覆盖主界面和 AI 面板 | 补 `docs/ui-prototype-notes.md`，用截图标注区域和交互状态 |
| GAP-04 | C02/C10/C12/C14/C15/C16/C17 仍需 PC Use 截图复验 | `checklist-conversation-ui-acceptance-2026-06-18.md` | 重启/修复 PC Use 后执行真实鼠标和截图验收 |
| GAP-05 | C08 重复外部路径导航去重曾确认 bug | 2026-06-19 acceptance server 记录 | 若源码未修复，应补测试覆盖 `NavigateToExternalPath` 路径字符串去重 |
| GAP-06 | Release 包策略多次记录但未形成正式操作手册 | 多个 `iteration-release-exe-package-*` | 汇总成 `docs/release-runbook.md` |
| GAP-07 | AI 数据发送范围需要更明确的用户可见说明 | README 与 spec 有描述但分散 | 在设置页或 README 增加“发送哪些元数据”说明 |

## 验收基线

- `dotnet build .\src\SpaceMonger.sln --no-restore` 应通过；既有 `NU1701` warning 可记录但不应阻断。
- 导航验收至少覆盖：文件夹选择、树内路径、树外路径、Back/Forward/Up、重复路径去重、根路径尾斜杠。
- Treemap 验收至少覆盖：中文字体、底部 cell 命中、推荐面板不遮挡、面包屑 dropdown 滚动。
- AI 验收至少覆盖：无 API key 禁用、Base URL override、JSON 提取失败诊断、推荐结果刷新旧状态。
- 发布验收至少覆盖：Release folder publish 成功、输出目录可启动、文档记录路径和 warning。

## 文档来源索引

- `README.md`：产品定位、功能列表、运行/配置说明。
- `specs/archive/2026-10-30-001-disk-space-analyzer/spec.md`：早期 SDD 功能规格、用户故事、FR/SC/Assumptions。
- `specs/archive/2026-10-30-001-disk-space-analyzer/plan.md`：技术上下文与项目结构。
- `docs/iteration-*`：2026-06-14 至 2026-06-19 的实际实现与修复记录。
- `docs/checklist-conversation-ui-acceptance-2026-06-18.md`：UI 异常验收项与 2026-06-19 acceptance server 复验状态。
- `docs/todo-navigation-treemap-followups-2026-06-17.md`、`docs/todo-ui-acceptance-findings-2026-06-18.md`：未完成和待复验需求。
- 当前源码：`src/SpaceMonger.App/`、`src/SpaceMonger.Core/`、`tests/`。
