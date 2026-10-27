# 分析入口与动态规则编排

日期：2026-10-27。

## 交付结果

两阶段实现已完成：分析入口动态发现并按需消费规则，无专用规则时仍可通用分析；必要时询问用户，区分无建议、受限、部分完成、失败和取消。扫描来源、受限历史、明确确认的偏好、默认关闭的行为统计及克制的快捷入口建议均已接入。

Comet 正式验收 A1—A18 全部通过，用户已接受结果并授权提交、合入本地 main 和归档。Runtime 已完成归档，状态为 done。Git 交付独立手动执行，不推送远端、不清理工作树或其他会话产物。

| 文档 | 用途 |
|---|---|
| [spec.md](spec.md) | 两阶段范围、非目标、十八项功能要求与验收场景 |
| [design.md](design.md) | 代码证据、只读消费契约、状态、历史与轻量记忆设计 |
| [tasks.md](tasks.md) | 十六项实施任务及交付条件 |
| [acceptance.md](acceptance.md) | 验收矩阵与正式结论索引 |
| [requirements-map.md](requirements-map.md) | 对话需求来源、排除项与默认值 |
| [verify_spec.py](verify_spec.py) | 可在新检出目录执行的文档结构和正式规格一致性检查 |

## 正式归档

- [正式 brief](comet/archive/2026-10-26-analysis-rule-orchestration/brief.md)
- [归档目标规格](comet/archive/2026-10-26-analysis-rule-orchestration/specs/disk-analysis-orchestration/spec.md)
- [正式验收报告](comet/archive/2026-10-26-analysis-rule-orchestration/verification.md)
- [归档状态](comet/archive/2026-10-26-analysis-rule-orchestration/comet-state.yaml)
- [已同步主规格](comet/specs/disk-analysis-orchestration/spec.md)

归档目录名由 Runtime 按 UTC 日期生成，因此为 2026-10-26；本地交付日期为 2026-10-27。

## 验证与限制

正式验收候选的 Core 520 项、App 179 项测试通过，共 699 项；六项 Runtime 检查通过。独立只读 Verifier 已逐项评估十八项验收。后台 GUI 使用隔离数据与受控回环模型，未使用用户真实外部模型配置实测，不以脚本证明真实模型判断质量或主题归一化质量。

最终独立发布目录：outputs/2026-10-27_04-26-19_分析规则编排，入口 DiskPilot-X.exe，使用时保留整个目录。发布包、后台 GUI 原始证据和本机过程记录留在原工作树，不纳入源码提交。

仓库根可运行：

    python -X utf8 docs/analysis-rule-orchestration/verify_spec.py
    dotnet test src/DiskPilotX.sln

文档检查不调用模型、不启动应用，也不代替产品验收。历史纯文档阶段的 --documents-only 边界检查另需原工作树过程记录，不作为新检出的默认检查。

## 协作边界

规则创建模块和规则 CRUD 由其他会话重构，本次不修改；分析只消费既有读接口并交接可审核草稿，不自动创建规则或执行清理。原有 docs/comet 保持原位，没有迁移或修改其他变更。

仅本工作树的 .comet/config.yaml 独立 artifact_root 配置不纳入提交、不合入 main。主工作区中其他会话的未提交文件保留。
