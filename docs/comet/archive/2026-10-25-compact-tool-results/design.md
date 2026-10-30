# 工具结果精简设计

## 代码事实

FileTreeAgentToolBase.EntryJson 被五个文件树查询工具共用，统一输出绝对路径、name、extension、时间、child_count 和三项布尔 flags。AgentJson.Options.WriteIndented=true，但 DiskAssistantService 将结果重新解析为 JsonNode，随后 ToJsonString() 紧凑序列化后发送，因此不能把去缩进当成主要节省来源。FileTreeQueryService.FindByPath 目前仅规范化并比较完整路径。

## 默认结果

保留 list_children 顶层 path 作为一次性的绝对基准，添加 path_format=relative，children[].path 仅返回相对路径。跨层搜索使用一次性的 base_path，同样标记相对路径。默认条目保留 path、type、size_bytes，目录附 child_count；只有存在异常时附 flags。details=true 返回修改时间等详情，find_by_path 默认仍适合获取单项详情。

```json
{"ok":true,"path":"D:/项目/DemoUnityProject","path_format":"relative","count":2,"children":[{"path":"Library","type":"directory","size_bytes":2097152,"child_count":1},{"path":"ProjectSettings","type":"directory","size_bytes":33,"child_count":1}]}
```

示例中的正斜杠只是展示方式；实现应统一约定分隔符并覆盖 Windows 根目录和 UNC 测试。

## 深层结果

提供 format=tree 的可选结果，公共父目录只出现一次，单子目录链合并为一段。例如 a/b/c 下面两个命中文件可表示成一个目录头和两条文件行。保留命中排名或明确排序对应关系；中间目录仅作定位结构。树形输出仍使用 JSON 外壳声明基准、count、truncated。不要同时输出完整 entries 与完整 tree，避免反向增加 token。

## 后续调用与安全

工具说明明确：完整路径=本次结果的基准+相对路径。第一阶段不引入隐式相对路径输入；如为操作便利增加 base_path 参数，在查询及白名单验证之前统一解析，禁止依赖进程工作目录。清理或导航的真实操作对象始终是解析后的完整路径。

## 其他工具

逐项审计实际模型结果，优先减少列表中的重复路径、冗余说明和大块可按需读取的内容。注册表、技能源文件、分析证据、推荐和用户决策结果分别有业务语义，不用通用删空/删 false 处理。先保留错误、取消、隐私、风险与执行结果，再确定可缩减字段。

## 验证与交付

先补路径还原、详情、异常标志、截断、同名与深层结果的契约测试；补实际发送给模型的集成断言。记录固定数据集压缩前后的紧凑 JSON 长度，若未运行 tokenizer 仅报告字符/字节变化。运行相关 Core 测试和受影响 App 测试，重新发布 outputs/日期时间_工具结果精简，通过 cua-driver 在应用窗口验收。

## 当前边界

已存在其他需求的状态修改、文档修改和 .planning/app-product-review-20261025/，本需求不改动或清理它们。用户已确认方案；实现、最终打包、Core 312 项与 App 77 项回归及独立六项验收均已完成，当前等待用户验收结果。
