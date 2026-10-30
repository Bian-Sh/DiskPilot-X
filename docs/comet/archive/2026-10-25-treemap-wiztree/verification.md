---
generated_from_state_version: 24
---

# 验证

## 当前结果

- 结果: **已归档**
- 验证情况: **已完成检查，验证结果已确认**
- 目标周期: 6
- 迭代: 1
- 验证器尝试次数: 1
- 完成时间: 2026-10-25T17:47:38.369Z
- 摘要: 独立只读验收 A1-A5 全部通过，未发现真实缺陷。复用当前候选 Runtime 全量回归 224 项（App45、Core179），15 项针对性回归为已有子集、10 项本地接口检查单独计数；核对完整 spec、实现、最终包和窗口截图。证据详见 work/treemap-wiztree-2026-10-25/verifier-final-report.md 与 verifier-final-evidence.json；未发送系统鼠标输入，未重跑全量。

## 验收

| 编号 | 结果 | 来源 | 验收项 | 原因 |
| --- | --- | --- | --- | --- |
| A1 | passed | brief.md | 单击标题或内容块：出现选中轮廓，根路径与布局不变。 | 独立核对 HandleClick 与 View 事件绑定，单击仅选择而不导航；当前候选接口证据覆盖目录和文件单击，根路径不变。2 像素选中框向外绘制，像素测试验证内部首列不变；布局固定两像素边距，后台只读节点实测根矩形 (2,2,807,290.5)，画布 811×294.5。 |
| A2 | passed | brief.md | 双击文件夹标题：当前根切换到该文件夹；双击文件与内容区域不进入。 | 仅 clickCount==2 且命中带可见标题的目录才触发 NodeActivated；文件、目录非标题和无标题目录不激活。控件回归及当前候选原子接口记录确认双击进入 DXCache、非标题/文件不进入并能返回。 |
| A3 | passed | brief.md | 鼠标跨块移动：细亮轮廓跟随，离开清除；选中轮廓继续保留。 | 独立核对同块移动重绘、全画布径向光照、目录标题排除区域、文件标题参与光照；像素回归覆盖纯色恢复、同块跟随、跨 cell、目录标题避光和外描边。悬停细框与选中粗框独立，移出清悬停保选择，450ms 提示定时器与布局重置清理均符合完整 spec。 |
| A4 | passed | brief.md | 相同扩展名颜色一致，不同常见类型可区分，文件夹边界紧凑。 | 文件按大小写归一化扩展名映射固定色组，未知扩展名使用确定性散列，跨深度及导航同色；目录一像素间距、深灰标题，常态填充无渐变。Core 颜色/布局回归、四状态渲染及最终包窗口截图相互印证。 |
| A5 | passed | brief.md | 回归测试通过，输出带可读时间的独立发布包，cua-driver 截图与交互验证。 | 当前 Runtime 同一候选和输入绑定的 App 45/45、Core 179/179 回执均 passed，退出码 0；复用 15 项针对性回归与 10 项当前候选本地接口记录。带时间 win-x64 自包含包存在，PID36540 路径指向该包，App/Core 发布 DLL 与 Release SHA256 一致；另用 cua-driver 仅采集目标窗口截图。未重复全量或发送系统鼠标输入。 |

## 检查

| 检查 | 命令 | 工作目录 | 状态 | 退出码 | 耗时 |
| --- | --- | --- | --- | ---: | ---: |
| App 全量回归 | test tests/SpaceMonger.App.Tests/SpaceMonger.App.Tests.csproj --no-restore --verbosity quiet | . | passed | 0 | 3920 ms |
| Core 全量回归 | test tests/SpaceMonger.Core.Tests/SpaceMonger.Core.Tests.csproj --no-restore --verbosity quiet | . | passed | 0 | 9720 ms |

### Builder 报告的证据

以下为 Builder 报告，不等同于 Runtime 检查凭据或独立验收结果。

- 交互与渲染针对性回归: passed — 15 项针对性交互与像素渲染回归通过，含外描边不覆盖内部、标题排除光照、跨 cell 光照、同块光照跟随及布局清理。
- Windows x64 自包含发布: passed — C:\Users\BianShanghai\.codex\worktrees\treemap-wiztree\SpaceMonger-Copilot\outputs\SpaceMonger-2026-10-26_01-29-51，自包含 win-x64 发布退出码 0。
- 已知限制: cua-driver 的 WPF 后台 hover/drag 不可投递；未再使用 global_input。最终通过本地验收接口调用相同交互处理逻辑，cua-driver 负责应用截图；不把接口测试声称为系统鼠标端到端测试。
- 已知限制: 保留既有布局节点数量与最小块限制，未改动扫描引擎。

## 阻塞项

_无。_

## 风险与跳过的工作

- 交互证据来自验收模式本地接口对同一控件处理逻辑的调用，不是实际系统鼠标端到端测试；本 Verifier 仅后台读取 nodes，未重放十项交互。
- 用户可并发操作窗口，截图与接口断言分别解释；不以不同时间截图推断目录或选择状态。01:29:51 以前的 final*.png 不作为最终发布包行为断言依据。
- 真实鼠标提示弹出与多 DPI 连续交互未另作端到端验证；提示延迟、坐标缩放通过实现核对。日志存在 NU1701 与既有编译告警，未导致测试或发布失败。

## 之前的迭代

| 目标周期 | 迭代 | 尝试 | 结果 | 未解决项 | 摘要 | 完成时间 |
| ---: | ---: | ---: | --- | --- | --- | --- |
| 1 | 0 | 0 | recovery | — | Native Shape artifacts changed | 2026-10-25T15:01:47.548Z |
| 2 | 1 | 0 | recovery | — | Native Shape artifacts changed | 2026-10-25T16:13:30.433Z |
| 3 | 1 | 0 | recovery | — | Native Shape artifacts changed | 2026-10-25T17:00:04.696Z |
| 4 | 1 | 1 | recovery | — | 用户追加：选中粗白框向外扩展保留内部面积；目录标题排除光照，内容区光照继续跨 cell。 | 2026-10-25T17:12:28.190Z |
| 5 | 1 | 0 | recovery | — | Native Shape artifacts changed | 2026-10-25T17:28:46.618Z |
| 6 | 1 | 1 | pass | — | 独立只读验收 A1-A5 全部通过，未发现真实缺陷。复用当前候选 Runtime 全量回归 224 项（App45、Core179），15 项针对性回归为已有子集、10 项本地接口检查单独计数；核对完整 spec、实现、最终包和窗口截图。证据详见 work/treemap-wiztree-2026-10-25/verifier-final-report.md 与 verifier-final-evidence.json；未发送系统鼠标输入，未重跑全量。 | 2026-10-25T17:47:38.369Z |



## 结论

独立只读验收 A1-A5 全部通过，未发现真实缺陷。复用当前候选 Runtime 全量回归 224 项（App45、Core179），15 项针对性回归为已有子集、10 项本地接口检查单独计数；核对完整 spec、实现、最终包和窗口截图。证据详见 work/treemap-wiztree-2026-10-25/verifier-final-report.md 与 verifier-final-evidence.json；未发送系统鼠标输入，未重跑全量。
