# 版本tag发布
推送v三段数字版本tag触发Windows构建与测试，只有通过后才发布完整自包含文件夹并构建Inno安装器，应用、安装器与Release版本一致。安装EXE与SHA256上传同一个GitHub Release；tag须来自已合并main，不能把旧worktree假称main。
## 安装与权限
安装内容包含所有运行时DLL、DiskPilotX.Core及skills；不单文件或裁剪；用户应用requireAdministrator，测试版本asInvoker。安装器显示可见向导，用户手动确认安装。交付EXE与文件夹而非ZIP。
## 发布说明
使用Loom-X分节：🐞修复问题、✨新增功能、🚀优化改进，附完整变更链接。优先docs/releases/v版本.md，缺失时从上个可达版本到当前tag的真实提交自动整理；首版按全部历史或审校稿生成，不编造。
## 真实更新测试
正式首版1.0.0；测试包0.9.0，读取真实Release API，显示真实发布说明，下载实际setup.exe并进入安装就绪。检查失败、无资产或下载不完整不得误报就绪。未经实测不宣称真实提权安装通过。
