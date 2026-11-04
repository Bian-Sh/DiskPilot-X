---
description: 通过对话选择范围，只读发现 Unity 项目的 Library，按需获取活动和 Hub 线索，交宿主清单确认。
---
# Unity 项目缓存盘点

## 任务入口

不要调用 show_skill_panel，不存在第一个参数弹窗。用户没有提供范围时，用 ask_user 在对话内收集 roots（一个或多个本地绝对目录，分号分隔）与 depth（1–8，默认6）。已经给出的参数不重复询问，不添加 scope_path 兜底。

roots 为空、全非法或不存在时，返回空结果及原因，绝不扩大到全盘；最多32个发现根。源码运行仍经过宿主可信源码确认，只读、不删除。

## 发现与最少信息

运行本技能源码，parameters 包含 roots/depth。以 Assets 与 ProjectSettings 同时存在作为项目结构证据，不把 Unity Hub 当唯一发现来源。

本次目标仅 Library，不把 Temp、obj、Logs、Build(s)、录制或项目本体混入候选。默认回传每个 Library 路径和少量占用摘要；完整统计表、版本和本地结果视图留在宿主，不要求反向传给 AI。

## 按需判断

仅当需要比较保留优先级时，再运行同一源码，parameters.mode=inspect，roots 为最多6个已发现的 Library 绝对路径。可选 hub_projects_file 指向明确允许读取的本地 Hub 项目记录；缺省尝试本地标准记录，失败/不支持时标 unknown，不扩大查找用户资料。

返回该项目少量目录时间与 Hub 匹配状态。时间只是近似内容变化线索，不等于真实最近开发时间；Library 导入时间不能冒充开发活动。Hub listed/not_listed/unknown 分开，未列出或时间较旧都不能单独证明可删除，不上传完整 Hub 清单或项目正文。

## 扫描与推荐

对用户选定的 Library 路径调用 analyze_paths，无头结果留在内存，宿主自动在对话里提供扫描入口，不必先显示到 Treemap。默认返回小摘要；需要判断子目录时才携带批次 scan_id 与具体 path 调用 get_analysis_evidence。

多次调用可累积当前会话范围；publish_recommendations 默认 append，merge 为同义兼容值，重复路径去重。只有用户明确选择时使用 replace，不能反复要求覆盖。

候选路径必须来自真实扫描证据。Library 可以重建但有重新导入代价，category=BuildCache，safety_rating=ReviewFirst；不得称为无风险、不得替用户直接删除。用户最终在宿主清单上确认后才执行。

历史快照只是查看入口，不是实时磁盘或旧清理授权。内存结果重启/回收后失效，由用户显式重新扫描；打开历史不自动扫描或请求模型。
