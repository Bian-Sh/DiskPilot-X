# 清理 Skill Creator 实现计划

> 使用 subagent-driven-development 完成分工实现与独立复核。范围来自已确认设计及用户“先干”补充；所有代码、注释、产物文档中文。

- [x] T1：移植 Loom-X 结构化 askuser 契约、ViewModel 与 WPF 卡片；先做行为测试，再实现。独立写集为新建 UserDecision/AskUser 文件和测试，主流程由协调者接线。
- [x] T2：实现 Skill 包存储、结构化规则/只读预览、资料与可信源码编译工具；先做边界测试。写集为 Services/Skills、新工具、相关测试和 Core.csproj。
- [x] T3：接入 Creator、刷新目录/路由、聊天空态引导和滚动规则列表；实现受约束 XAML 任务窗口和消息任务入口；接入 askuser 与工具。
- [x] T4：运行核心与 WPF 测试，修复回归，独立只读验收全部 AC。
- [x] T5：用 cua-driver 验收应用窗口；生成 outputs 可读上海时间目录中的自包含包并报告限制。

## 实现判断

现有活动需求要求隔离，因此使用 codex/cleanup-skill-creator 工作树。普通规则和面板不提升到源码执行；源码是明确授权的可信模式，不宣称系统级隔离。未获授权的 git 合并/提交不执行。
