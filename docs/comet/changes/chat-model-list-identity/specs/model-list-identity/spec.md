# 模型列表与 CLI 身份
落实用户提出的六项 chat 与 AI 设置调整，模型展示与实际缓存一致，CLI 指纹可见、可刷新。

# 范围
空态胶囊常驻背景；无候选模型时不显示推断模型；设置只管理模型列表；图标刷新与自定义模型获取 URL；CLI 身份菜单逐项刷新和版本展示；chat 显示已启用身份。

## 目录结构
### 新建
- tests 下的模型目录与 CLI 刷新回归测试。
### 修改
- ChatPanel、ChatViewModel、ApiSettingsSection、SettingsViewModel、AiOptions、本地化资源。
- AppSettings、ModelEndpointConfig、ModelCatalogService、CliIdentityService。
### 删除
无。
### 明确不建
不修改 Loom-X、不引入依赖、不清理其他会话文件、不提交 Git。

# 非目标
不重构模型请求协议，不修改现有分析模型设置，不更改用户已保存的对话模型。

# 验收示例
- AC1：空态问题默认有紧凑胶囊背景，悬停可辨，两端半圆。
- AC2：模型缓存为空或已选模型不在缓存时，chat 显示选择模型提示；真实候选可选并持久化。
- AC3：模型列表使用刷新图标和 ToolTip；可保存完整 HTTP(S) 获取 URL，留空沿用协议默认路径，复用凭据和代理。
- AC4：AI 设置显示模型列表，不再提供独立对话模型输入框。
- AC5：CLI 下拉项右侧有刷新入口，Claude/Codex 手动刷新可重复执行，失败保留旧值；Grok 明确使用内置指纹，与 Loom-X 一致。
- AC6：CLI 菜单和当前选择展示身份与版本，chat 仅在启用身份时展示，刷新后请求头与展示一致。
- AC7：针对状态、请求地址、指纹刷新做回归，cua-driver 验收应用窗口，outputs 生成上海时间命名自包含包。
