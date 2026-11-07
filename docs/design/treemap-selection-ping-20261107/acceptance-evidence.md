# Treemap 本轮验收证据

- 源码：独立工作树 codex/treemap-selection-ping-20261107，基础 02e66a6126674575f3fea87d47845c611c55476a，包含本轮未提交修改。
- App：808 通过、1 项既有跳过，见工作树 work/treemap-selection-ping-20261107/app-repair-final.log。
- Core：888 通过，见 core-final.log。
- CUA：原生扫描按钮后台调用成功；画布坐标被遮挡拒绝，改用真实 HandleClick/HandleContextMenu 共用处理链路。repair-multi、repair-recommendations、repair-busy-toast、repair-ping 系列为应用窗口截图与状态。
- 实际运行进程：测试包 EXE 路径读回正确，TokenElevation=false；见 acceptance/process-repair.json 与 elevation-repair.json。
- 发布环境：source_probe 成功编译 provider，摘要授权只读执行返回2项，失败源码产生行列诊断，BaseDirectory 指向最终测试包。见 repair-source-validation.json。
- 发布路径：主仓库 outputs/2026-11-07_18-43-02_Treemap多选与平滑Ping_测试包及同时间用户交付包。
- 两个实际 EXE 清单：asInvoker / requireAdministrator。App/Core全部IL、异常区域和资源一致；skills逐文件一致。见 assembly-equivalence-repair.json、package-verification-repair.json。
- 机械核对22项通过，见 final-evidence-check-repair.json。运行中的 Comet 候选检查和独立逐项验收由 Runtime 保存结果，不以本记录替代正式结果。

## 必须披露
未实际验收用户交付包UAC弹窗及提权后UI；未把被遮挡拒绝的原生画布点击记为通过；极细cell保持真实面积，只保留可辨橙色提示线宽。不删除其他会话产物、不自动Git提交/合并/推送。

首轮独立验收A2/A3不通过后，已补同根版本冲突前置检查及两条无副作用拒绝回归。新的候选继续独立验收，不将首轮结果当作通过。

## 最终逐项验收
第二轮独立只读 Verifier 对 A1–A9 全部报告通过，Runtime 保存9/9通过。桥接层不能机器证明独立性，当前等待用户接受结果，不自行归档。见 work/treemap-selection-ping-20261107/verifier-round2/runtime-final-response.json。

## 用户确认后的 Ping 调整
- 用户确认 #FEA400，最终视野与布局停稳后闪烁 1.5 秒，再 300 毫秒淡出；右下角极小目标须继续放大并避免描边裁切。
- Treemap 188 项通过，覆盖 1/1.5/2 DPI 四边像素、停稳前不闪烁、连续 Ping 重计时、卸载释放和高倍率连续缩小。App 正式候选检查 823 项通过、1 项既有跳过，Core 888 项通过。
- 新的只读扫描夹具有 81 个文件。在真实测试发布包中，右下角 5 KB 文件短边从约 1.45 DIP 放大到约 32.14 DIP，视口约 22.17x；有四边描边余量，不强制居中。记录到约 1762 毫秒可见提示（35 毫秒采样），随后目标高亮状态和透明度清零。
- CUA 原生扫描按钮及推荐列表路径按钮后台 Invoke 均成功。推荐定位前视口 1x，点击后约 22.17x，选中路径一致；前后应用窗口和 UIA 已核对。本轮不把原生画布坐标视作已验收。
- 新包统一位于主仓库 outputs/2026-11-07_23-31-00_Ping短暂高亮与角落定位_测试包与同时间用户交付包；实际清单分别 asInvoker/requireAdministrator。App 4620 个方法与 Core 4663 个方法的 IL、异常区和资源一致，skills 逐文件一致。
- 新测试包进程路径和未提权 Token 已读回，发布环境 source_probe 编译、摘要授权只读执行两项及错误行列诊断通过。未实际运行交付包 UAC 弹窗或提权后的 UI。
- 证据位于 work/treemap-selection-ping-20261107/ping-lifetime-acceptance/；发布与帧核验脚本的换行语法错误已修复，应用源码和包不受影响。最终独立验收结果以本次 Runtime 报告为准，不沿用上一轮通过结论。

## 最终确认的一次冲击效果（2026-11-08）
- 本节取代上节的周期闪烁方案，历史证据保留、不作为本轮效果结论。颜色仍为 #FEA400；最终镜头与 LOD 布局停稳后，前约 80 毫秒强烈压实边框，随后光晕轻微外扩并连续衰减，总长约 800 毫秒。真实 cell 和地图不抖动、不因冲击改变面积。
- TDD 先运行旧实现得到 6 项失败，实现后 Treemap 188 项通过；新增实际渲染帧测试后 Ping 22 项通过。Runtime 当前候选全套检查：App 824 项通过、1 项既有跳过；Core 888 项通过；发布包与帧证据核验通过。
- 新测试发布包的右下角 5 KB 文件短边由约 1.45 DIP 放大到约 32.14 DIP，视口约 22.17x；边框和轻微扩散留有余量，只做必要平移、不强制居中。帧采样记录约 730 毫秒可见提示，随后提示状态归零，普通白色选择保留。停稳后的视口不改变，主边框透明度单调衰减、光晕外扩。
- 新包的 CUA 原生扫描和推荐路径按钮后台 Invoke 均已核对。推荐定位从 1x 平滑追踪到约 22.17x，选中真实目标路径一致；应用窗口截图分别保存强亮、扩散和消失阶段。
- 新双包统一位于主仓库 outputs/2026-11-08_00-02-08_Ping冲击扩散_测试包及用户交付包；实际清单为 asInvoker / requireAdministrator。App 4625 方法与 Core 4663 方法的 IL、异常区和资源一致，skills 逐文件一致。
- 新测试包进程 Path 与未提权 Token 已读回；真实发布环境源码 provider 编译、授权只读执行两项以及错误源码行列诊断通过。仍未实际启动用户交付包 UAC 或验收提权后 UI，不把清单核验说成提权 UI 验收。
- 本轮证据位于 work/treemap-selection-ping-20261107/ping-impact-acceptance/ 和 ping-impact-tests/；Runtime 候选为 6fef10b4-0214-4fa5-b19b-b6b4f1ebcc3b。新独立只读 Verifier 逐项核验 A1–A9，其结论以本轮 Runtime 保存结果为准，不复用此前的验收 verdict。

## 独立验收后的取消边界修复
- 首次冲击候选独立验收为 8 项通过、A5 失败：先 Ping 存在目标，再请求不存在路径，旧提示未立即取消。已在宿主两种定位入口先取消旧镜头及提示，再进行目标检查；普通选择和视口不因此改变。
- 新增不存在路径与同扫描激活失败两个真实行为回归，旧实现两项失败；修复后 Ping 24 项通过。新发布包中，活跃提示从 opacity=1，在失败请求返回时立即成为 path=null、opacity=0、waveOpacity=0、animating=false，普通白色选择及视口保留。
- 再次打包到主仓库 outputs/2026-11-08_00-31-05_Ping冲击最终修复_测试包及用户交付包。新包后台 CUA、源码规则、实际双清单、App/Core IL 和 skills 一致性及冲击帧核验通过；可见提示采样约 749 毫秒，右下角目标短边约 32.14 DIP。
- 修复证据位于 work/treemap-selection-ping-20261107/ping-impact-repair-acceptance/；全套 Runtime 检查与新的独立逐项验收待本候选保存结果，不将前轮 8 项通过直接当本轮结论。
- 修复候选 d2a1ae54-19d2-4012-8fa5-c2c44358b9ff 的 Runtime 全套检查已保存：App 826 项通过、1 项既有跳过，Core 888 项通过，发布包与帧核验通过。新的独立逐项验收仍单独判断 A1–A9。

## 用户确认恢复原地图边界
- 上一个修复候选已由独立 Verifier 逐项核验 9/9 通过，但尚未接受或归档。用户随后明确改为恢复原地图边界钳制、允许边缘橙框与光晕裁剪；已正式更新并再次确认范围，不沿用旧范围的验收结论。
- 移除 Ping 为描边额外放宽的平移范围与状态字段。最终 Ping 定位与后续手动平移均不越过原地图边界；内侧目标仍只作必要平移，边缘自然裁剪，小 cell 自动放大和单次冲击不变。保留小于 1x 层级过渡被打断时的上下界排序，避免 Clamp 抛异常。
- 五项边界变更回归先 RED 失败，修复后 Treemap 191 项通过；涵盖两个角落、1/1.5/2 DPI、手动钳制和原有平滑/选择等回归。
- 新发布包 outputs/2026-11-08_00-59-02_Ping原边界钳制_测试包及用户交付包。真实右下角 5 KB 文件仍约 32.14 DIP、22.17x，位置贴边且 pan 位于原边界内；单次冲击可见采样约 743 毫秒。新的 missing 请求立即清空旧提示，普通选择和视口保留。
- 新包后台 CUA 扫描与推荐路径定位、源码 provider 编译及授权只读执行两项、失败源码行列诊断通过。新测试包进程 PID62336、Path 与未提权 Token 已读回。两个实际 EXE 清单 asInvoker / requireAdministrator，App/Core 方法 IL、异常区、资源与 skills 逐文件一致。
- 当前证据位于 work/treemap-selection-ping-20261107/ping-boundary-acceptance/；Runtime 当前全套检查与新的独立逐项验收另行保存。未启动用户交付包 UAC 或验收提权 UI；无 Git 提交、合并、推送与其他会话产物清理。

## 补齐普通 Ping 缓动逐帧钳制
- 上一个原边界候选已保存 App 826 通过、1 跳过，Core 888 通过及独立 9/9 通过，但 Verifier 同时记录两帧瞬态越界。为完整落实已确认要求，继续补齐普通 Ping 动画的逐帧 ClampPan；层级交叠空间过渡保持原行为。
- 新真实动画采样回归先 RED 失败，加入逐帧钳制后 Treemap 192 项通过。此前关于瞬态越界的限制属于旧候选，不可沿用为本轮修复结论。
- 重新发布到主仓库 outputs/2026-11-08_01-14-36_Ping逐帧边界钳制_测试包及用户交付包；对应证据目录为 work/treemap-selection-ping-20261107/ping-frame-clamp-acceptance/，继续验证当前包和所有普通 Ping 采样帧。
- 当前候选 4a73f40f-a4bb-4bc4-9400-b5415368d3bd 已保存 Runtime App 827 通过、1 项既有跳过，Core 888 通过、package 核验通过。新独立只读 Verifier 核验 A1–A9 各一次全部通过，真实 CLI exitCode=0、stateVersion=35、9/9 passed，当前等待用户接受，不自行归档。
- 最终 49 个真实采样帧均在原地图边界的浮点容差内，包含 18 个动画中间帧；单次冲击提示采样约 726 毫秒，目标短边约 32.14 DIP，视口约 22.17x。Verifier 重新读取实际双清单、同候选 IL/资源、19 个 skills 文件及未提权进程。UAC 弹窗/提权 UI 未实测的限制保留。
- 独立结果和证据分别为该目录的 independent-verifier-797e15ea-final-result.json、independent-verifier-797e15ea-evidence.json、independent-verifier-797e15ea-cli-final.json。以上是当前候选结论，不复用旧候选的通过判定。

## 稳定布局后一次追踪与删除调试菜单（2026-11-08）
- 用户新反馈推动需求再确认；上节的逐帧边界通过不足以证明没有中途回摆。本轮实际追溯发现缩放结束触发 LOD 重排，旧目标几何改变后再次定位；改为运动前后台收敛最终布局，只提交一次稳定几何，按最终几何及原边界计算落点并一次平滑追踪。
- 新增真实 80 文件布局轨迹和右键菜单回归，旧实现先 RED 失败。准备失败与外部取消的过渡截图释放回归也先 RED 后修复。首次 Arrange 的延迟尺寸事件在相同尺寸下不再作废准备结果；真实尺寸变化仍保留过期结果拒绝。最终开发期 Treemap、手动推荐、忙碌 toast 聚焦检查 228 项通过。
- 新包位于主仓库 outputs/2026-11-08_02-05-01_Ping稳定落点与移除调试菜单_测试包及同时间用户交付包；实际 EXE 清单为 asInvoker / requireAdministrator。源码来自同一本任务工作树、基础 02e66a6126674575f3fea87d47845c611c55476a，仍未提交。
- 新测试包 PID12640 的 Path 和 TokenElevation=false 已读回，CUA 后台扫描按钮使状态变为 81 文件；推荐路径按钮后台 Invoke 后从 1x 到约 22.08x，目标为真实 079.bin。49 个放大后的采样帧几何和布局提交数保持一致（Commits=1），目标接近方向无反向回摆；所有普通动画采样帧仍受原地图边界约束。
- 极小文件短边放大到 32 DIP；最终停稳后冲击提示采样约 814 毫秒，光晕外扩后单调衰减；白色选择保留。边缘自然裁剪，不为光晕越界留白。强亮、扩散、消失与 CUA 应用窗口截图均保存。
- 实际发布包 BuildContextMenu 返回菜单不含 Ping；正常“定位文件”（资源管理器入口）仍保留。该菜单证据来自真实共用处理链路，不声称原生画布右键坐标已成功送达。
- 发布环境 provider 源码编译、摘要授权只读执行两项及错误行列诊断通过；App 4635 与 Core 4663 个方法的 IL、异常区域及嵌入资源在双包一致，skills 逐文件核对通过。证据目录 work/treemap-selection-ping-20261107/ping-stable-acceptance/。
- 上述为开发和真实发布证据；当前完整 Runtime 回归及新的独立 A1–A9 验收另行保存，不沿用旧候选通过结论。未实际启动用户交付包 UAC 或验收提权后的 UI；未提交、合并、推送，也未清理其他会话产物。
- 当前候选 ad463d49-6c01-4ac2-8347-af5c21e3c8db 的 Runtime 完整检查为 App 832 通过、1 项既有跳过，Core 888 通过、0 失败。新独立只读 Verifier 对 A1–A9 全部各一次 passed，公开 CLI exitCode=0、stateVersion=41、9/9 通过，等待用户接受结果；未执行 accept-result 或归档。
- 独立核查读回原始50采样点（其中49个放大后点）、实际双EXE清单、每包453文件及19个skills，并补 TinyHarness 方法IL/异常区/资源一致性核对。详细事实、限制与Runtime回执见 ping-stable-acceptance/independent-verifier-73ab0389-report.md 及同前缀 evidence.json、runtime-return.json。
