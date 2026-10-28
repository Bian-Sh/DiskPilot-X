# v1.0.0 tag发布设计
## 来源与顺序
用户选择先app合main再构建发布。起点为已合main的de7fe85；不包含主仓库其他会话未提交修改。Git操作使用原生git/gh，不由工作流管理器代办。
## 实现
- release.yml只在v* tag或手动诊断运行，tag严格校验v三段数字；Windows .NET10测试App/Core，然后从tag注入版本发布。
- publish-release.ps1在绝对输出目录创建完整folder；SelfContained=true/PublishSingleFile=false/PublishTrimmed=false，串行Rebuild，禁止覆盖既有输出。Inno Setup安装整个folder，用户应用requireAdministrator，生成setup.exe和SHA256，无ZIP。
- new-release-notes.ps1优先版本审校Markdown；否则按上个可达版本tag的提交区间归类三节，附Full Changelog，Git提交文本不作为脚本执行。
- 首版v1.0.0说明依据main真实现有能力人工审校；不包含未提交功能。
- 0.9.0测试包用独立清单和设置，通过真实GitHub API发现/下载1.0.0；不注入模拟版本，安装器启动参数和异常由回归覆盖，不无人值守安装。
## 验证
脚本fixture临时Git仓库验证自动分类和覆盖，Action静态契约测试；App/Core回归；本地实际folder发布双清单/文件内容/源码探针；tag推送后读取实际GitHub运行与Release资产校验。

## 校验与验收补充
更新资产如提供GitHub的sha256 digest，下载完成前验证并缓存摘要；相同长度但摘要不符的EXE不进入就绪，重启使用缓存也复核。老版本无digest时保留长度校验兼容。新增真实检查/下载验收命令仅在已有隔离acceptance入口内，不调用seed、不自动安装。
第一步main已为de7fe85；第二步测试脚本与Actionlint、本地实际版本/清单通过后原生Git合main、推送并打v1.0.0，发布依赖真实Actions结果，不用本地产物伪称CI成功。
