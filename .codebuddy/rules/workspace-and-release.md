---
description: 本项目的工作树与发布目录统一约定
alwaysApply: true
---

# 工作树与发布目录

执行本项目任务前读取仓库根目录 `AGENTS.md`；它是统一规则源，本文件仅为 WorkBuddy / CodeBuddy 提供自动加载入口。遵循其中的应用发布、提权清单、验收和多会话产物边界，不使用历史模板替代实际代码。

- 主仓库：`D:/AppData/Visual Studio/Projects/DiskPilot-X`。
- 新 worktree：`D:/AppData/Visual Studio/Projects/DiskPilot-X/.worktrees/<任务名>/`；先检查现有工作树，不覆盖其他任务、不嵌套创建、不擅自搬迁旧工作树。
- 所有测试包、用户交付包和交付压缩包：统一使用主仓库的 `D:/AppData/Visual Studio/Projects/DiskPilot-X/outputs/`，子目录采用 `<可读时间>_<任务名>_<测试包或用户交付包>`。
- 即使从 worktree 构建，也必须将上述绝对路径显式传给发布命令，不能使用 worktree 自己的相对 `outputs/` 或客户端私有输出目录。
- 打包前核对源码目录、分支、基础提交和未提交改动；新 worktree 不继承主目录未提交修改，不把旧工作树包称为最新 `main`，不擅自覆盖其他会话源码。
- 客户端无法指定 worktree 目录时先说明限制；不得静默偏离约定或手动搬动托管工作树。旧包与其他会话产物保持不动。
