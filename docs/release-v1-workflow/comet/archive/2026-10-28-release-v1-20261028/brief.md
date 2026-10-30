# 目标
落实用户第二步：从已合入app更新模块的main实现安装包构建Action，再发布v1.0.0；用低版本测试包验证真实更新。
# 范围
tag触发Windows CI测试与自包含文件夹发布、Inno Setup安装器、SHA256、按Loom-X分节的Release Notes与版本注入；公开Release及低版本真实更新验证由原生git/gh交付。
## 目录结构
### 新建
.github/workflows/release.yml负责测试构建与tag发布；installer/DiskPilotX.iss负责完整文件夹安装；scripts/publish-release.ps1和new-release-notes.ps1负责可重复版本打包与说明；scripts/test-release.py负责脚本与工作流回归；docs/releases/v1.0.0.md负责首版审校说明；docs/release-design.md记录流程与验收。
### 修改
必要时UpdateService与回归补充CI资产兼容/校验，现有本地发布脚本增加可配置测试版本；README发布说明。
### 删除
无。
### 明确不建
不引入外部LLM密钥、WiX或ZIP交付，不搬移用户数据，不复制主仓库其它会话未提交修改。
# 非目标
不静默安装，不擅自改变主仓库其它未提交内容，不用Comet执行Git提交、合并、推送或打tag。
# 验收示例
- A1：推送v版本tag自动Windows构建和测试；tag版本同时注入程序集、安装器与Release，失败不发布。
- A2：文件夹发布self-contained且不单文件/裁剪，实际应用清单requireAdministrator，Inno完整安装器与SHA256产物保留全部运行时、契约与skills。
- A3：说明按Loom-X的🐞修复问题、✨新增功能、🚀优化改进和Full Changelog组织；首版审校文件覆盖自动稿，后续按前后版本真实提交归类，无外部AI服务。
- A4：脚本回归、App/Core回归通过；本地实际双包清单、非提权源码规则与低于1.0.0版本注入被验证。
- A5：以合并后main提交创建并发布v1.0.0，GitHub Action成功，公开Release安装器/校验与日志可访问；低版本包通过真实API发现1.0.0并下载准备，不用模拟版本替代；真实提权后安装未测明确披露。
# 决策与约束
用户已确认先app合main、再从main构建Action并发布v1.0.0；第一步main=de7fe85，第二步工作树由该main直接创建。Git操作原生命令，不由Comet归档代办。发布只生成安装EXE和完整文件夹，不生成ZIP。
