# C# 窗口与动态结果技能 JSON 本地化设计

## 用户纠正后的范围
只做两个实际包含 C# 动态视图的 skill：内置 Steam 游戏库盘点与用户保存的 Unity 缓存盘点。Unity 的 Detector.cs 实际实现 IAppDataProvider，按代码契约而不是文件名判断。纯 Markdown 技能、outputs 快照规则及模型指令文档不改。

## 设计
- 参考 Loom-X 插件，每个技能自带英文、简中、繁中 JSON；沿用现有 SkillTextCatalog 与宿主语言事件，不把技能文案硬编码回宿主。
- 覆盖这两个技能的入口、任务表单、视图、统计、表头、状态、按钮和风险说明。含数量、路径、日期的句子使用模板加参数；保留真实数据及稳定参数值。
- 切换语言只刷新现有控件，不重新发现磁盘、不清空输入/勾选/结果。旧纯文字与包接口兼容。
- Unity 已保存用户包先备份和记录哈希，再以新版本保存；保留历史版本和搜索/缓存安全边界，不自动执行源码或清理。
- 补失败测试再最小实现，重新测试、双包发布及发布环境/cua验收。

## 旧目录处理
用户已授权：只删除确认为空的 skills/unity-project-cleanup。历史文档依赖旧宿主硬编码 DiscoverUnityLibraries 与 read_unity_registry_context，对应源码在复制基线前已删除。空目录已在当前 worktree 删除，不触碰主目录，不删用户 unity-cache-check。

## 非目标
不翻译纯Markdown技能，不改快照保留策略，不恢复宿主硬编码Unity功能，不提交合并或清理其他会话产物。


## 实施契约
- `SkillTextCatalog.Bind(key, args...)` 只序列化数据为有界模板标记，`Resolve(value,culture)` 在显示时解析；支持嵌套模板与日期，不执行代码或路径。静态键沿用 `{loc key}`。窗口重渲染只更新显示绑定。
- `AppDataDiscoveryContext.Localizations` 为可选末尾参数，由源码服务注入该已确认包的 JSON 文案，不把独立规则文案塞入宿主 resx。
- 内置技能从自有 i18n 读取；用户新版本同时保存 manifest.localizations 和独立 i18n/*.json，一致性及摘要均校验，旧格式保持兼容。
- 目录只本地化真实含源码技能的 skill.name/skill.description，不改原始模型文档；App显式传入稳定语言状态，语言变更刷新相关入口。
- 选择回传时解析展示文本，不把编码模板交给用户或模型；路径/参数值始终保持原始值。
