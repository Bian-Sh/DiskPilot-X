# 后续交互与绘制设计

## 当前事实
- 源码基线 02e66a6；已合入修复不得回退。
- 推荐路径 Click 与列表 MouseDoubleClick 共用 RecommendationActivated，当前会切换缓存来源并 NavigateToEntry。
- CleanupService 使用 VisualBasic FileSystem 的 OnlyErrorDialogs；优先复用系统平台能力。
- 会话存储由 App.xaml.cs 注册 ConversationStore，常规根为 LocalApplicationData/DiskPilot-X/Conversations，验收模式有独立根。

## 分批顺序
1. Q1/Q2/Q3/Q10：明确单击 Ping 与双击动作；历史复制沿用真实会话存储路径。
2. Q4/Q5/Q6：内容、title/titlebar、Selected/Deleted状态框分层；文字随 title 状态，不再独立阈值。
3. Q7/Q8：系统进度与真实会话误拒根因，保留批准安全。
4. Q9：核对 MangoDisk，测量按需 MFT、目录回退与内存；不先定并发策略。
5. 清理闭环、内存回归、双包发布、真实 UI 和独立审查。

## 验证约定
- 回归先写失败测试，再最小实现。真实交互用 cua-driver，只截应用。
- 执行/删除只对本任务夹具；真实对话只读，证据不复制完整私密文本。
- 所有产物到主项目 outputs，源码来源与未提交状态记录。

## 用户澄清：只要执行进度，不是二次确认
- 用户指出截图中的“确实要永久性地删除此文件夹吗”不是 Q7 要求；要求应用批准后的下一步系统删除进度条。
- AllDialogs 会打开额外的系统删除确认，是本任务的错误候选，不能交付。
- 改用 Windows IFileOperation：设置 FOF_NOCONFIRMATION，不设置 FOF_SILENT；保留回收标志、系统执行进度及取消，保持批准、保护路径、链接、指纹复核。
- 当前规格 A7/A11 不变；这是纠正未验收实现，不伪造完成、重新批准或归档。
