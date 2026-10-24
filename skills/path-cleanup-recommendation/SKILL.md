---
name: path-cleanup-recommendation
description: 为用户指定的路径扫描并生成可复核的清理建议。
---

# Path Cleanup Recommendation Skill

## 目的

使用当前 Harness 原生工具完成路径扫描、证据分析和建议发布，工具结果持续返回同一轮 Agent。

## 执行步骤

1. 用 resolve_path 校验用户路径，再用 get_copilot_context 获取当前 root、current_path 和 scan_id。
2. 当前扫描已覆盖目标时直接查询；未覆盖或用户明确要求重扫时调用 scan_path，并等待真实完成。覆盖已有结果的确认由宿主处理，拒绝后停止。
3. 调用 get_analysis_evidence 检查当前范围的元数据与内容指纹，必要时用 list_children、summarize_subtree、find_large_files 补充证据。
4. 用 publish_recommendations 发布候选，带当前 scan_id。每项包含 path、category、safety_rating、explanation；大小由本地扫描决定。无候选也发布空 recommendations 数组。
5. 根据工具实际接受/过滤结果回答，告诉用户建议在推荐列表中。不要把未发布候选说成已完成，不递归启动另一轮分析。

## 保护规则

- 只检查用户指定范围，遵守白名单和本地路径保护。
- 文件夹名称不足以证明可删除，结合内容、项目标记和修改时间判断。
- 本技能只生成建议；真正删除必须由用户明确要求，并通过应用绑定清单的确认。
- 不使用文本 JSON 工具协议、旧提案动作或 follow_up_prompt。
