using System.Text.Json;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Tests;

/// <summary>视图契约的解析与校验：非法区块忽略、超量截断、封面地址确定性构造。</summary>
public sealed class SkillUiSpecContractTests
{
    private static SkillUiSpec Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return SkillUiSpecContract.Parse(document.RootElement);
    }

    private static string Section(string kind, string items) =>
        "{\"kind\":\"" + kind + "\",\"items\":[" + items + "]}";

    [Fact]
    public void Parse_ReadsTitleSurfaceAndSections()
    {
        var spec = Parse("""
        {"title":"游戏库盘点","subtitle":"共 2 个库","surface":"window",
         "sections":[{"kind":"poster_grid","title":"占用排行","columns":4,"items":[{"title":"A","value":"1 GB"}]}]}
        """);

        Assert.Equal("游戏库盘点", spec.Title);
        Assert.Equal("共 2 个库", spec.Subtitle);
        Assert.Equal(SkillUiSurface.Window, spec.Surface);
        // 旧取值 window 等价于 auto：除入口卡外还会自动弹窗。
        Assert.True(spec.AutoOpensWindow);

        var section = Assert.Single(spec.Sections);
        Assert.Equal(SkillUiSectionKind.PosterGrid, section.Kind);
        Assert.Equal(4, section.Columns);
        Assert.Equal("占用排行", section.Title);
        Assert.Single(section.Items);
    }

    [Fact]
    public void Parse_DefaultsToEntrySurface()
    {
        var spec = Parse("""{"title":"T","sections":[]}""");
        Assert.Equal(SkillUiSurface.Entry, spec.Surface);
        // 默认只在气泡留一张入口卡，不主动弹窗打扰。
        Assert.False(spec.AutoOpensWindow);
    }

    [Fact]
    public void Parse_OldSurfaceValuesMapToEntryOrAuto()
    {
        Assert.Equal(SkillUiSurface.Entry, Parse("""{"title":"T","surface":"bubble","sections":[]}""").Surface);
        Assert.Equal(SkillUiSurface.Auto, Parse("""{"title":"T","surface":"window","sections":[]}""").Surface);
        Assert.Equal(SkillUiSurface.Auto, Parse("""{"title":"T","surface":"both","sections":[]}""").Surface);
        Assert.Equal(SkillUiSurface.Auto, Parse("""{"title":"T","surface":"auto","sections":[]}""").Surface);
    }

    [Fact]
    public void Parse_IgnoresUnknownKindAndNonObjectSections()
    {
        var spec = Parse("""{"title":"T","sections":[{"kind":"carousel"},"nope"]}""");
        Assert.Empty(spec.Sections);
    }

    [Fact]
    public void Parse_TruncatesSectionsAndItems()
    {
        var items = string.Join(",", Enumerable.Range(0, 100).Select(_ => "{\"title\":\"g\"}"));
        var sections = string.Join(",", Enumerable.Range(0, 12).Select(_ => Section("table", items)));
        var spec = Parse("{\"title\":\"T\",\"sections\":[" + sections + "]}");

        Assert.Equal(8, spec.Sections.Count);
        Assert.All(spec.Sections, section => Assert.Equal(60, section.Items.Count));
    }

    [Fact]
    public void Parse_PosterGridBuildsSteamPosterFromAppId()
    {
        var spec = Parse("""{"title":"T","sections":[{"kind":"poster_grid","items":[{"title":"CS2","appid":"730"}]}]}""");
        var item = Assert.Single(spec.Sections[0].Items);

        Assert.Equal("730", item.AppId);
        Assert.Contains("steam/apps/730", item.ImageUrl, StringComparison.Ordinal);
        Assert.True(item.HasImage);
    }

    /// <summary>
    /// 条目行内动作：uninstall 让宿主把卸载能力挂到按钮上，源码本身仍不删文件、不起进程。
    /// target 省略时回退到条目的 appid，缺省一律要求强确认（只有显式 confirm=false 才跳过）。
    /// </summary>
    [Fact]
    public void Parse_ReadsItemUninstallAction()
    {
        var spec = Parse("""
        {"title":"T","sections":[{"kind":"poster_grid","items":[
          {"title":"Short Circuit VR","appid":"970800","action":{"kind":"uninstall"}}]}]}
        """);
        var action = spec.Sections[0].Items[0].Action;

        Assert.NotNull(action);
        Assert.True(action!.IsUninstall);
        Assert.Equal("卸载", action.Label);
        Assert.Equal("970800", action.Target);
        Assert.True(action.ConfirmRequired);
        Assert.True(spec.Sections[0].Items[0].HasAction);
    }

    [Fact]
    public void Parse_ItemActionHonoursExplicitTargetAndConfirm()
    {
        var spec = Parse("""
        {"title":"T","sections":[{"kind":"table","items":[
          {"cells":["A"],"action":{"kind":"uninstall","label":"删除","target":"220","confirm":false}}]}]}
        """);
        var action = spec.Sections[0].Items[0].Action;

        Assert.NotNull(action);
        Assert.Equal("删除", action!.Label);
        Assert.Equal("220", action.Target);
        Assert.False(action.ConfirmRequired);
    }

    /// <summary>未知动作类型一律忽略：宿主只执行自己认识的动作。</summary>
    [Fact]
    public void Parse_IgnoresUnknownItemAction()
    {
        var spec = Parse("""
        {"title":"T","sections":[{"kind":"poster_grid","items":[
          {"title":"A","action":{"kind":"format_disk"}}]}]}
        """);

        Assert.Null(spec.Sections[0].Items[0].Action);
        Assert.False(spec.Sections[0].Items[0].HasAction);
    }

    [Fact]
    public void Parse_TableKeepsHeadersAndCells()
    {
        var spec = Parse("""
        {"title":"T","sections":[{"kind":"table","headers":["游戏","占用"],"items":[{"cells":["A","1 GB"]}]}]}
        """);
        var section = Assert.Single(spec.Sections);
        var headers = section.Headers;

        Assert.NotNull(headers);
        Assert.Equal(new[] { "游戏", "占用" }, headers);
        Assert.Equal(new[] { "A", "1 GB" }, Assert.Single(section.Items).Cells);
        Assert.Equal(2, section.ColumnCount);
    }

    /// <summary>
    /// 气泡里不再内联渲染数据区块，只靠入口卡展示规模摘要，
    /// 因此契约要能自行汇总出「几个区块 / 多少条目 / 多大」。
    /// stat_row 与 text 是标签，不算数据条目。
    /// </summary>
    [Fact]
    public void EntryStats_SummarizeSectionsAndItems()
    {
        var spec = Parse("""
        {"title":"T","entry_title":"入口","entry_summary":"摘要",
         "sections":[
           {"kind":"stat_row","items":[{"title":"a"}]},
           {"kind":"poster_grid","items":[{"title":"b","size_bytes":1048576,"selectable":true},
                                          {"title":"c","size_bytes":2097152}]}]}
        """);

        Assert.Equal(2, spec.SectionCount);
        Assert.Equal(2, spec.ItemCount);
        Assert.Equal(1, spec.SelectableItemCount);
        Assert.Equal(3_145_728, spec.TotalSizeBytes);
        Assert.Equal("入口", spec.EffectiveEntryTitle);
        Assert.Equal("摘要", spec.EffectiveEntrySummary);
    }

    /// <summary>
    /// 同一个游戏通常同时出现在海报网格和明细表里，直接相加会把占用翻倍
    /// （实测把 481.3 GB 显示成 962.7 GB）。按 path 去重、取同一 path 的最大值。
    /// </summary>
    [Fact]
    public void EntryStats_DoNotDoubleCountSamePathAcrossSections()
    {
        var spec = Parse("""
        {"title":"T","sections":[
          {"kind":"poster_grid","items":[{"title":"悟空","path":"D:\\Game","size_bytes":1000,"selectable":true}]},
          {"kind":"table","items":[{"cells":["悟空","D:\\Game","1000"],"path":"D:\\Game","size_bytes":1000}]}]}
        """);

        Assert.Equal(1, spec.ItemCount);
        Assert.Equal(1, spec.SelectableItemCount);
        Assert.Equal(1000, spec.TotalSizeBytes);
    }

    /// <summary>
    /// 技能源码先返回一份视图、模型又补一次 render_view 时，两份视图条目高度重叠，
    /// 气泡里必须只留一张入口卡。
    /// </summary>
    [Fact]
    public void DescribesSameContent_DetectsRedundantRender()
    {
        var skillView = Parse("""
        {"title":"Steam 游戏库","sections":[{"kind":"poster_grid","items":[
          {"title":"悟空","path":"D:\\a"},{"title":"2077","path":"D:\\b"}]}]}
        """);
        var modelView = Parse("""
        {"title":"在窗口中查看 Steam 库","entry_title":"在窗口中查看 Steam 库","sections":[{"kind":"table","items":[
          {"cells":["悟空"],"path":"D:\\a"},{"cells":["2077"],"path":"D:\\b"}]}]}
        """);
        var otherView = Parse("""
        {"title":"另一个视图","sections":[{"kind":"table","items":[{"cells":["x"],"path":"E:\\x"}]}]}
        """);

        Assert.True(skillView.DescribesSameContent(modelView));
        Assert.False(skillView.DescribesSameContent(otherView));
    }

    [Fact]
    public void EffectiveEntryText_FallsBackToTitleAndSubtitle()
    {
        var spec = Parse("""{"title":"标题","subtitle":"副标题","sections":[]}""");
        Assert.Equal("标题", spec.EffectiveEntryTitle);
        Assert.Equal("副标题", spec.EffectiveEntrySummary);
    }

    [Fact]
    public void Signature_DeduplicatesIdenticalViews()
    {
        var json = """{"title":"T","sections":[{"kind":"poster_grid","items":[{"title":"g","path":"D:\\a"}]}]}""";
        Assert.Equal(Parse(json).Signature, Parse(json).Signature);
        Assert.NotEqual(Parse(json).Signature,
            Parse("""{"title":"T","sections":[{"kind":"poster_grid","items":[{"title":"g","path":"D:\\b"}]}]}""").Signature);
    }

    [Fact]
    public void Parse_StatRowKeepsValueAndLabel()
    {
        var spec = Parse("""
        {"title":"T","sections":[{"kind":"stat_row","items":[{"title":"总占用","value":"482 GB","value_label":"11 个游戏"}]}]}
        """);
        var item = Assert.Single(spec.Sections[0].Items);

        Assert.Equal("总占用", item.Title);
        Assert.Equal("482 GB", item.Value);
        Assert.Equal("11 个游戏", item.ValueLabel);
    }

    /// <summary>
    /// 技能源码可以绕过 JSON 解析器直接构造视图，那时只有 appid、没有封面地址。
    /// 宿主必须在统一入口补齐，否则独立窗口里的海报全是灰底。
    /// </summary>
    [Fact]
    public void Normalize_FillsPosterUrlForDirectlyBuiltSections()
    {
        var spec = new SkillUiSpec
        {
            Title = "T",
            Sections =
            [
                new SkillUiSection
                {
                    Kind = SkillUiSectionKind.PosterGrid,
                    Items =
                    [
                        new SkillUiItem { Title = "赛博朋克 2077", AppId = "1091500" },
                        new SkillUiItem { Title = "已带封面", AppId = "730", Path = "D:\\Steam" }
                    ]
                },
                new SkillUiSection
                {
                    Kind = SkillUiSectionKind.Table,
                    Items = [new SkillUiItem { AppId = "730", Cells = ["x"] }]
                }
            ]
        };
        spec.Sections[0].Items[1].SetImageUrl("https://example.test/cover.jpg");

        var normalized = SkillUiSpecContract.Normalize(spec);

        Assert.Same(spec, normalized);
        var poster = spec.Sections[0].Items;
        Assert.True(poster[0].HasImage);
        Assert.Equal(SkillUiSpecContract.SteamPosterUrl("1091500"), poster[0].ImageUrl);
        // 显式封面不被覆盖
        Assert.Equal("https://example.test/cover.jpg", poster[1].ImageUrl);
        // 表格行不补封面
        Assert.False(spec.Sections[1].Items[0].HasImage);
    }
}
