# 目标
保存并加载历史对话，参考 Loom-X 的会话持久化，允许恢复后继续提问。

# 范围
在独立 worktree 实现本地会话存储、模型上下文与消息展示恢复，以及新增对话复合按钮。

## 目录结构
### 新建
- src/SpaceMonger.Core/Services/Assistant/ConversationStore.cs
- src/SpaceMonger.App/ViewModels/ChatViewModel.History.cs
- 对应的 Core 与 App 测试
### 修改
- DiskAssistantService、ChatViewModel、ChatPanel 与本地化资源
### 删除
无。
### 明确不建
独立历史按钮、云同步、历史编辑管理系统。

# 非目标
不复制 Loom-X 的完整管理界面，不改变磁盘扫描和清理规则。

# 验收示例
- 非空对话自动保存，重启应用后可从历史列表打开，并携带原模型上下文继续对话。
- 点击复合按钮左侧约 90% 主体开始空白新对话，旧对话仍能从右侧“∨”历史列表加载。
- 历史列表按最近更新时间排序，显示首条用户消息标题、时间与空状态，长列表可滚动。
- 运行期间不能新建或切换会话；取消、失败、损坏文件及白名单变更不会破坏下一轮对话或泄露历史上下文。
- 消息文本、工具调用展示可恢复；不落盘 API Key，不恢复过期交互确认与磁盘对象引用。
- 完成相关测试及应用窗口验收，重新打包到 outputs 的可读时间目录。