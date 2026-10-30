# 迭代纪要：从 docs 反推需求文档（2026-06-19）

## 背景

项目已有 `specs/archive/2026-10-30-001-disk-space-analyzer/spec.md`，但近期大量真实需求散落在 `docs/iteration-*`、验收 checklist、TODO 和发布记录中，缺少一份能承接当前产品状态的需求文档。

## 本次产物

- 新增 `docs/product-requirements-reconstructed-2026-06-19.md`：中文反推 PRD，覆盖产品定位、用户旅程、功能需求、非功能需求、验收基线、已知空白和来源索引。
- 更新 `specs/archive/2026-10-30-001-disk-space-analyzer/spec.md`：在 Success Criteria 前增加 2026-06-19 addendum，指向反推 PRD，避免后续只看早期 SDD spec 时遗漏近期需求。

## 反推依据

- `README.md` 的产品定位、功能列表和配置说明。
- `specs/archive/2026-10-30-001-disk-space-analyzer/` 的早期 SDD 规格、计划、任务和数据模型。
- `docs/iteration-*` 中关于导航、设置、控制台、推荐面板、聊天、中文本地化、面包屑、打包发布的迭代记录。
- `docs/checklist-conversation-ui-acceptance-2026-06-18.md` 和 2026-06-19 acceptance server 验收状态。
- 当前源码模块结构，包括 `MainWindow`、`TreemapViewModel`、`RecommendationsViewModel`、`ChatViewModel`、`SettingsViewModel`、`AcceptanceAutomationServer`、扫描/推荐/清理核心服务。

## 关键补齐点

- 将“路径选择、地址栏/面包屑、Treemap 当前根、扫描目标”的状态同步整理成独立需求组。
- 将“面包屑 dropdown 限高、滚动、虚拟化”和“Treemap 中文字体”纳入 UI 需求，而不是只留在修复纪要里。
- 将“控制台日志、开发者诊断、acceptance server、PC Use 复验”纳入维护者需求。
- 将“实质性 WPF 修改后主动发布 Windows x64 folder package”纳入交付需求。
- 明确列出当前仍需补齐的文档空白：UI 原型标注、release runbook、AI 元数据发送说明、待 PC Use 截图复验项。

## 验证

- 本次仅修改 Markdown 文档，无源码改动。
- 已用 codegraph 探查当前源码关键模块，避免只凭迭代纪要反推。
- 未运行 `dotnet build`，因为本次不涉及代码或项目文件变更。
