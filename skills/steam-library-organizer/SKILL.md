---
name: steam-library-organizer
description: "盘点本机 Steam 游戏库：自动定位客户端安装目录（注册表）、解析全部库文件夹（含多盘库）、逐个游戏读取 appid、名称、安装目录、安装大小与上次游玩时间；以海报网格卡片呈现，卡片与明细行自带「卸载」按钮，宿主用 Steam 原生卸载替用户处理，完成后把结果交给 AI 总结。只读发现，不删除任何文件。"
---

# Steam 游戏库盘点

## Purpose

帮用户搞清楚「Steam 游戏到底占了多少空间、哪些游戏最占地方、有多久没玩了」，从而决定要不要清理。

**面向小白用户**：用户不需要知道 Steam 装在哪、游戏放在哪个盘、什么是注册表。所有机器细节由 `src/Provider.cs` 经 **API Bridge** 自行发现；对话里只问人话（如「你想盘点哪个平台」）。

## 只读边界

源码以当前用户权限运行，**不是安全沙箱**，但严格只读：

- ✅ 读注册表（`context.Host.ReadRegistry`）
- ✅ 枚举目录 / 读文本文件
- ✅ 统计目录大小
- ❌ 不写文件、不删文件、不启进程、不联网

清理必须走宿主的建议清单 + `cleanup_recommendations` 确认链路，源码不得绕过。

## 发现步骤

1. **定位客户端根目录**
   依次尝试注册表 `SOFTWARE\Valve\Steam` 的 `InstallPath` / `SteamPath`（HKLM 与 HKCU，宿主自动处理 WOW6432Node）。找不到则如实返回空结果并 Log 原因。

2. **解析全部库**
   读 `<root>/steamapps/libraryfolders.vdf`，用 `"path"\s+"(...)"` 取出所有库目录；若文件缺失则只保留根目录。

3. **逐个游戏读取**
   对每个库的 `steamapps/appmanifest_*.acf`：
   - `appid` ← 文件名数字段
   - `name` ← `"name"` 行
   - `installdir` ← `"installdir"` 行 → 安装目录 = `steamapps/common/<installdir>`
   - 大小 ← **实测目录大小**（`GetDirectorySize`），比 ACF 的 `SizeOnDisk` 更能反映真实占用
   - 上次游玩 ← `localconfig.vdf` 的 `LastPlayed`（Unix 秒）；缺失则回退安装目录最后修改时间

4. **声明视图**
   返回 `AppDataDiscoveryResult(entries, view)`，`view` 为：
   - `stat_row`：游戏数 / 总占用 / 最大游戏
   - `poster_grid`：按占用降序的海报卡（含"上次游玩"）
   - `table`：全部明细（游戏 / 大小 / 上次游玩 / 所在库）
   - 每个条目都挂 `SkillUiItemAction { Kind = "uninstall", Target = <appid> }`：
     宿主据此渲染按钮，本身不做任何删除动作。

## 卸载：到此为止

游戏的处置**止步于卸载**——不再回传数据、不再勾选项、也不再对游戏目录做深度扫描与清理分析：

1. 用户点「卸载」→ 宿主弹出强确认（危险色、必须显式勾选「我确认」）。
2. 确认后宿主读取 Windows 卸载表里 Steam 自己登记的 `UninstallString`
   （形如 `steam.exe steam://uninstall/<appid>`），原样交给 Steam 执行。
3. 宿主轮询 `appmanifest_<appid>.acf` 与安装目录判断是否真的卸载完成。
4. 结果（是否完成 / 卸载前占用 / 释放空间）连同事实交回 AI，由 AI 给一句收尾总结。

源码侧保持只读：**不删文件、不启进程**，卸载能力全部由宿主提供。

## 使用方式

- 保存后宿主会提示授权运行；运行前展示完整源码与包摘要。
- 气泡里只有一张入口卡，点「打开视图」进入完整界面：**在这个界面里直接卸载，流程就此结束**。

## 约定

- 不硬编码任何本机安装路径；一律经 Bridge 发现。
- 找不到时如实说明并给出下一步（如让用户确认是否已安装），不编造数据。
