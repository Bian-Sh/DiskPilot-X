# 验收矩阵

日期：2026-10-27。A 编号对应 [spec.md](spec.md) 场景，十八项均已通过正式独立验收并由用户接受。下表保留设计期预期证据名称，不保证与最终测试类同名；实际执行、逐项理由和局限以[正式验收报告](comet/archive/2026-10-26-analysis-rule-orchestration/verification.md)为准。

| 验收 | 要求 | 核验重点 | 预期证据 | 当前状态 |
|---|---|---|---|---|
| A1 | FR01 | 应用分析请求、无扫描、重复点击、无删除 | AnalysisTaskContextTests；AnalysisConversationFlowTests；App 状态测试 | 已通过 |
| A2 | FR02 | 目录更新、损坏规则隔离、版本变化 | AnalysisRuleCatalogTests | 已通过 |
| A3 | FR03 | 同名不同用途、明确排除、旧规则兼容 | AnalysisRuleCatalogTests；本地文件树夹具 | 已通过 |
| A4 | FR04 | 阶段读取、约束必读、旧全文、预算不足 | AnalysisRuleCatalogTests | 已通过 |
| A5 | FR05 | 无规则仍分析、网络失败、隐私检索边界 | AnalysisToolBoundaryTests；AnalysisConversationFlowTests | 已通过 |
| A6 | FR06 | 必要询问、跳过取消、创建交接不可用与恢复 | AnalysisDecisionFlowTests；后台交互证据 | 已通过 |
| A7 | FR07 | 只读按操作开放，规则写入／源码／清理拒绝 | AnalysisToolBoundaryTests | 已通过 |
| A8 | FR08 | 真实过滤原因，受限与无候选分离，非固定总结 | AnalysisValidationReportTests；语义复核 | 已通过 |
| A9 | FR09 | 全部终态、部分覆盖、旧结果归属 | RecommendationsViewModelTests；CleanupProjectionTests | 已通过 |
| A10 | FR10 | 三类来源、取消不污染、当前意图优先 | AnalysisTaskContextTests；AssistantScanTests | 已通过 |
| A11 | FR11 | 摘要优先、跨会话原文授权、来源删除及隐私变化 | AnalysisContextStoreTests；ConversationHistoryTests | 已通过 |
| A12 | FR12 | 扫描／规则版本／用户补充可追溯，未执行不虚报 | AnalysisValidationReportTests；AnalysisContextStoreTests | 已通过 |
| A13 | FR13 | 明确偏好、范围、查看更正删除停用、不赋权 | AnalysisPreferenceTests | 已通过 |
| A14 | FR14 | 默认关闭、启停清除、动作区分、最小记录 | AnalysisUsageTests | 已通过 |
| A15 | FR15 | 重复门槛、冷却、不再提醒、既有创建交接 | AnalysisShortcutSuggestionTests | 已通过 |
| A16 | FR16 | 等待及发布时扫描／规则／隐私／偏好变化 | AnalysisToolBoundaryTests；AnalysisDecisionFlowTests；竞态测试 | 已通过 |
| A17 | FR17 | 旧数据可读、不迁移规则、禁改路径零差异 | AnalysisRuleCatalogTests；AnalysisContextStoreTests；候选 diff 审查 | 已通过 |
| A18 | FR18 | 独立逐项验收、隔离后台 GUI、时间命名发布包 | Runtime 检查记录；独立 Verifier；应用窗口证据；发布目录 | 已通过 |

## 证据规则

- 脚本模型用于证明任务装配、工具可用性、宿主拒绝、版本和状态，不证明真实模型必然有同等语义判断。
- 最终回答检查是否说明推荐对象、理由、风险和限制，或是否明确总结无推荐；不匹配固定中文句子。
- 必须分别保存完整无候选、硬保护全过滤、重要部分未知、发布失败和取消的结果，不能只测试 happy path。
- 真实模型体验仅在实际调用后记结果；未配置、未调用或缺少可用模型不记通过，也不隐藏限制。
- 独立验收覆盖所有 A 项；可以复用仍适用的真实检查记录，不复用旧用户授权。
- 截图只截应用窗口，使用隔离设置／对话／样本，不在真实用户缓存上试删。
- 当前阶段可验证文档结构、映射和改动边界；不能把文档检查通过写成产品验收通过。
