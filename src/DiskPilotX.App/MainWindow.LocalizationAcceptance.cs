using System.IO;
using System.Windows;
using System.Text.Json.Nodes;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Services;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Skills;
using Microsoft.Extensions.DependencyInjection;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private JsonObject? localizationProbe;
    private Window? localizationFixture;

    internal object AcceptanceLocalization(string? language)
    {
        if (!AcceptanceAutomationServer.IsEnabled()) throw new InvalidOperationException("验收入口未启用。");
        if (language is not null) L.SetLanguage(language);
        return L.GetState();
    }

    internal object AcceptanceLocalizationFixture(string? kind)
    {
        if (!AcceptanceAutomationServer.IsEnabled() || _chatViewModel is null) throw new InvalidOperationException("验收入口未启用。");
        if (kind == "modal-info")
        {
            _ = ModalHost.ShowAsync("常规提示", "这是一条常规提示，正文保持左对齐。", Controls.ModalMessageType.Info, Controls.ModalButtonFlags.Positive);
            return new { ok = true, kind };
        }
        if (kind is "cleanup-single" or "cleanup-multiple" or "cleanup-permanent" or "cleanup-long")
        {
            // 只预览本次验收固定文案，不创建清理计划或执行文件操作。
            var count = kind == "cleanup-multiple" ? 10 : 1;
            var items = Enumerable.Range(0, count).Select(index => new DiskPilotX.Core.Models.CleanupRecommendation
            {
                TargetPath = @"D:\验收夹具\" + (kind == "cleanup-long" ? new string('长', 900) : "Cache"),
                Size = 282800000
            }).ToList();
            _ = ModalHost.ShowCleanupConfirmationAsync("确认清理", items,
                kind == "cleanup-permanent" ? DiskPilotX.Core.Enums.DeletionMode.PermanentDelete : DiskPilotX.Core.Enums.DeletionMode.MoveToRecycleBin,
                "确认清理");
            return new { ok = true, kind };
        }
        if (kind == "chat")
        {
            AcceptanceSetCopilotWidth(620);
            var message = new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = """
                排版回归：正文 `outputs`、`SpaceMonger-Copilot_2026-10-26_17-32-43` 与中文保持基线。

                - 每次运行时按当时的时间重新判断哪份最新，而不是写死某个路径。
                - 保留 `main-theme-2026-10-27-114100`，这里的列表文字不拉伸。

                1. 我检查过本地已保存的清理 Skill 列表，这条 `diskpilot-x-outputs-snapshots` 目前还没有保存成技能包。
                2. 如果你记忆中的 `outputs` 应该还有多份旧快照，那实际磁盘情况与预期不符。

                彩色表情：😀 ✅ 🎉 🚀 👩🏽‍💻 👨‍👩‍👧‍👦 ❤️ 1️⃣
                """
            };
            message.Segments.Add(new ChatSegment(ChatSegmentKind.Text) { Text=message.Text, Status=ChatSegmentStatus.Completed });
            _chatViewModel.Messages.Add(message);
            return new { ok = true, kind };
        }
        var tables = new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new() { ["title"]="Localization verification", ["label"]="Directories", ["safe"]="Safe mode", ["include"]="Include nested directories", ["section"]="Read-only inventory" },
            ["zh-CN"] = new() { ["title"]="本地化验收", ["label"]="检查目录", ["safe"]="安全模式", ["include"]="包含子目录", ["section"]="只读盘点" },
            ["zh-TW"] = new() { ["title"]="本地化驗收", ["label"]="檢查目錄", ["safe"]="安全模式", ["include"]="包含子目錄", ["section"]="唯讀盤點" }
        };
        localizationFixture?.Close();
        if (kind == "view")
        {
            var spec = new SkillUiSpec { Title="{loc title}", Subtitle="{loc section}", Localizations=tables,
                Sections=[new SkillUiSection { Kind=SkillUiSectionKind.Table, Title="{loc section}", Headers=["{loc label}","Size"],
                    Items=[new SkillUiItem { Title="Local data", Path=@"D:\验收目录", Cells=["Local data","4 KB"], Selectable=true, Selected=true, SizeBytes=4096 }] }],
                Actions=[new SkillUiAction { Kind="close", Label="{loc safe}" }] };
            localizationFixture = new SkillWindow(spec) { Owner=this };
        }
        else
        {
            localizationFixture = new SkillTaskWindow("{loc title}", """<StackPanel><TextBlock Text="{loc label}"/><TextBox Name="roots" Text="用户输入保留 / User input retained"/><CheckBox Name="include" IsChecked="True" Content="{loc include}"/><ComboBox Name="mode" SelectedValue="safe"><ComboBoxItem Value="safe" Content="{loc safe}"/></ComboBox></StackPanel>""",
                new JsonObject { ["scope_path"] = Path.GetTempPath() },
                (_, _, _) => Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true, ["message"]="只读验收结果 / Verification result retained" }), tables) { Owner=this };
        }
        localizationFixture.Show();
        return new { ok=true, kind, title=localizationFixture.Title };
    }

    internal object AcceptanceSourceProbe()
    {
        if (!AcceptanceAutomationServer.IsEnabled()) throw new InvalidOperationException("验收入口未启用。");
        var settings = Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS");
        if (string.IsNullOrWhiteSpace(settings)) throw new InvalidOperationException("源码验收必须使用独立设置目录。");
        localizationProbe = new JsonObject { ["running"] = true };
        _ = RunProbe();
        return localizationProbe;

        async Task RunProbe()
        {
            try
            {
                var root = Path.Combine(Path.GetDirectoryName(settings)!, "只读源码盘点夹具");
                Directory.CreateDirectory(Path.Combine(root,"library-a"));
                Directory.CreateDirectory(Path.Combine(root,"library-b"));
                var store = App.Services!.GetRequiredService<CleanupSkillStore>();
                var service = new CleanupSkillSourceService(store);
                var package = store.Save(new CleanupSkillPackage
                {
                    Id="localization-runtime-probe", Title="发布环境只读源码盘点", Description="只枚举本次验收新建的两个目录", SkillMarkdown="# 发布环境只读源码盘点", Source="""
                    using System;
                    using System.Linq;
                    using System.Threading;
                    using System.Threading.Tasks;
                    using DiskPilotX.Core.Services.Skills;
                    public sealed class Inventory : IAppDataProvider
                    {
                        public Task<AppDataDiscoveryResult> DiscoverAsync(AppDataDiscoveryContext context, CancellationToken token)
                        {
                            token.ThrowIfCancellationRequested();
                            var state = context.Host.GetLocalizationState();
                            using var subscription = context.Host.SubscribeLocalizationChanged(next => context.Host.Log(next.CultureName));
                            var entries = context.Host.EnumerateDirectories(context.Parameters!["root"])
                                .Select(path => new AppDataEntry(path, state.CultureName, path, 0)).ToArray();
                            return Task.FromResult(new AppDataDiscoveryResult(entries));
                        }
                    }
                    """
                }, overwrite:true);
                var valid = await service.ValidateSourceAsync(package.Id);
                var fields = new Dictionary<string,string> { ["root"] = root };
                var authorization = service.Authorize(package.Id, package.Digest);
                var output = await service.RunDiscoveryAsync(authorization, new DefaultSkillHostBridge(package.Id, parameters:fields), parameters:fields);
                var invalid = store.Save(package with { Id="localization-invalid-probe", Source="public class Broken { ??? }" }, overwrite:true);
                var errors = await service.ValidateSourceAsync(invalid.Id);
                localizationProbe = new JsonObject
                {
                    ["running"]=false, ["ok"]=valid.Success && output.Entries.Count==2 && !errors.Success && errors.Diagnostics.Any(d=>d.Line.HasValue && d.Column.HasValue),
                    ["validated"]=valid.Success, ["contract"]=valid.Contract, ["count"]=output.Entries.Count,
                    ["culture"]=output.Entries.FirstOrDefault()?.Name, ["diagnostic_count"]=errors.Diagnostics.Count,
                    ["diagnostics"]=System.Text.Json.JsonSerializer.SerializeToNode(errors.Diagnostics), ["base_directory"]=AppContext.BaseDirectory
                };
            }
            catch (Exception error) { localizationProbe = new JsonObject { ["running"]=false,["ok"]=false,["error"]=error.ToString() }; }
        }
    }

    internal object? AcceptanceSourceProbeState() => localizationProbe;
}
