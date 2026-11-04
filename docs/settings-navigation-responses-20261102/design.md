# 四项缺陷最小修复设计

## 工作区隔离
源码根：D:/AppData/Visual Studio/Projects/DiskPilot-X/.worktrees/settings-navigation-responses-20261102。基础提交 8cb9e9417e0be5ae7fa6e025de138ad9475f4ad4；不含主目录未提交修改。旧 Comet 记录为 Git 继承副本，仅改变本工作树的 artifact_root，保留全部旧任务目录。

## 已发现的实现证据
1. 更新说明已通过 NestedScrollWheel.Forward 处理边界，ModelOrderScroll 未接入，优先复用而不改通用路由。
2. CreateExternalEntry 在去除尾分隔符前调用 Directory.GetParent；Windows 上 GetParent(项目目录加尾分隔符) 返回同名无尾分隔符目录，形成一层同身份父子。需先规范目录身份再求父级，同时保留根目录。
3. ResponsesAccumulator 只保存 reasoning 的 id 与 encrypted_content，重构历史 JSON 时遗漏 summary。修复应保留数组内容，不只补字段名称，不删除思考历史。
4. ThinkingSlider_ValueChanged 以鼠标捕获或键盘焦点作为回写门槛，IsMoveToPointEnabled 的直接轨道点击可能在捕获前改变 Value；需区分用户输入与程序回流，不简单取消所有保护。

## 验证方法
先增加失败回归再最小修复；以实际请求 JSON、HwndSource 控件事件与真实测试包 UI 分层验证。测试包/用户包使用不同构建输出、强制重建并读取各自 EXE 清单；真实发布运行只读源码技能规则。未完成检查明确记录，不提前声明通过。

## 非目标
不重构导航系统，不新增兼容开关，不删除 reasoning 历史，不复制其他会话修改，不合并或推送。

## 已确认补充：API 验证
移除 BuildEndpoint 的默认模型解析依赖，目录配置允许空模型；生成验证必须在同版本目录刷新成功后显式传模型。目录仍按缓存/刷新已有契约管理，不因本次测试改变聊天选择。新增验证模型与结果描述状态；沿用 ModelErrorKind 分类并补结构化脱敏日志；文案复用现有本地化体系，提供三语资源。首次失败回归覆盖同步时序、失败目录不得借旧列表、模型选择变化、空模型与隐藏 Claude 回退、诊断秘密脱敏。
