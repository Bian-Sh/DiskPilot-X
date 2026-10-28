# 本地化与聊天呈现交付记录

日期：2026-10-28（Asia/Shanghai）。工作分支 `codex/localization-chat-typography`；未提交、未合并、未推送。原工作目录的已有文件不动，复制基线详见 `.planning/localization-chat-typography/baseline-files.json`。

## 已实现
- 统一语言快照：偏好、有效文化、实际资源语言与支持列表；auto、无效语言安全回退；默认/当前线程同步；资源查询不依赖读取线程。
- skill：模型工具 `get_localization_state`；Provider 的 `GetLocalizationState()` / `SubscribeLocalizationChanged(...)`；Detector 的 `context.Localization`；旧桥默认接口兼容。
- 内置 i18n JSON、用户包 localizations 和视图 localizations；文案参与摘要；旧包不增加空字段，不破坏旧摘要。
- 表单及结果视图使用 `{loc key}`，语言变化更新显示不重建控件；输入、参数稳定值、勾选及任务结果保留，关闭解除订阅。
- 聊天 Markdown 采用原生 Span/Run 和列表，修正代码上浮与异常文字拉伸；长代码可换行且保留选择复制；emoji 使用现有 SkiaSharp 绘制彩色字形。
- 三个资源目录键一致（455/455），补充结果视图固定宿主文案本地化；繁体卫星程序集随包发布。

## 验证证据
- App 全量：337 通过、0 失败、1 跳过；Core 全量：617 通过、0 失败。见 `full-app-test.log`、`full-core-test.log` 和 trx。
- 语言两个初始失败用例、动态 UI 目录初始失败、旧 ComboBox 绑定回归均有先红后绿记录。旧表格测试改为验证原生文字而非旧 UIContainer。
- 真实文件夹发布环境源码：成功验证 provider 契约，授权后只枚举验收独立目录，返回 2 个真实目录；非法源码返回契约及 CS1519，保留行列诊断。见 `source-probe-result.json`。
- 最终非提权测试进程由非提权 PowerShell 用 Start-Process Hidden 启动，实际 Path 与包匹配，父子进程 Elevated=false。见 `acceptance-process.json`。
- cua-driver 后台 UI 验收：深色宽栏、浅色窄栏、表单中英繁切换及输入/结果保留、结果视图英繁切换及勾选保留。只捕获目标 app。见 `ui-acceptance-summary.json` 与 acceptance PNG。
- 两包各 442 文件；实际 EXE 清单分别 asInvoker / requireAdministrator；依赖和 skills 内容逐文件一致，差异仅 DiskPilot-X.exe 与 DiskPilot-X.dll 的清单。见 `bundle-verification.json`、`package-content-comparison.json`。

## 包路径
- 测试包：`C:\Users\BianShanghai\.codex\worktrees\localization-chat-typography\DiskPilot-X\outputs\2026-10-28_06-49-11_本地化与聊天排版_测试包_非提权`
- 用户交付包：`C:\Users\BianShanghai\.codex\worktrees\localization-chat-typography\DiskPilot-X\outputs\2026-10-28_06-49-11_本地化与聊天排版_用户交付包_管理员`

完整文件夹交付，不单独复制 EXE。显式 SelfContained=true、PublishSingleFile=false、PublishTrimmed=false。测试清单与交付清单独立。首次发布发现共享增量缓存泄漏测试清单，已留样标为不可交付；最终脚本改为同一产物目录串行 Rebuild，两包实际清单和依赖哈希重新检查通过。

## 已知边界
- 当前 Windows Segoe UI Emoji 不提供彩色国家旗帜；保留原始字符并可选择复制，相关彩色旗帜用例明确跳过。常用表情、肤色、ZWJ 家庭、心形和键帽已验证彩色像素。不宣称覆盖字体尚不支持的全部 Unicode emoji。
- 用户交付包只核实实际 requireAdministrator 清单及内容一致性，没有实际触发 UAC 或验收提权后 UI；不把非提权测试替代为提权验收。
- 既有 SkiaSharp/OpenTK NU1701 与卸载窗 nullable 警告仍在；未增加 NuGet 依赖，也不在本次顺带改写。
- Comet 在隔离 worktree 的独立文档根管理本次变更；因随 Git 检出的旧无效记录会被误认为当前活动变更，不改变主目录流程状态。


## 使用接口

```csharp
var state = context.Host.GetLocalizationState();
using var subscription = context.Host.SubscribeLocalizationChanged(next =>
    context.Host.Log("宿主语言：" + next.CultureName));
```

动态 UI 优先使用 `localizations` 与 `{loc key}`，由宿主负责语言切换后的显示更新。完整契约见 `skills/skill-creator/references/bridge-api.md` 与 `task-ui.md`。订阅只用于技能自己的执行生命周期，不应为 UI 刷新长期保留源码实例。


## 独立验收结论

独立只读 Verifier 已逐项核查 A1～A6，Runtime 接受六项通过结果。另行定向复测 App 29 通过、1 个国家旗帜彩色用例跳过，Core 2 通过。完整原报告为 `comet/changes/localization-chat-typography/verification.md`。Comet 通用 Skill bridge 无法自行证明独立会话身份，按流程保留为等待用户确认；没有自动接受最终结果、归档、提交或合并。
