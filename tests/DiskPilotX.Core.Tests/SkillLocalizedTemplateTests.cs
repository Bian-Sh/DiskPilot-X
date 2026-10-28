using DiskPilotX.Core.Services.Skills;
using DiskPilotX.Core.Models;
namespace DiskPilotX.Core.Tests;
public sealed class SkillLocalizedTemplateTests
{
    private static SkillTextCatalog Texts => new(new Dictionary<string,Dictionary<string,string>>
    {
        ["en-US"] = new() { ["count"]="{0:N0} games at {1}", ["ago"]="{0} days ago", ["played"]="Last played: {0}", ["date"]="{0:yyyy-MM-dd}", ["title"]="Inventory" },
        ["zh-CN"] = new() { ["count"]="{1} 中有 {0:N0} 个游戏", ["ago"]="{0} 天前", ["played"]="上次游玩：{0}", ["date"]="{0:yyyy年MM月dd日}", ["title"]="盘点" }
    });
    [Fact] public void 参数保留原值直到按宿主文化呈现()
    {
        var value=SkillTextCatalog.Bind("count",1234,@"D:\游戏");
        Assert.Equal(@"1,234 games at D:\游戏",Texts.Resolve(value,"en-US"));
        Assert.Equal(@"D:\游戏 中有 1,234 个游戏",Texts.Resolve(value,"zh-CN"));
    }
    [Fact] public void 嵌套模板与日期能动态切换()
    {
        var value=SkillTextCatalog.Bind("played",SkillTextCatalog.Bind("ago",3));
        Assert.Equal("Last played: 3 days ago",Texts.Resolve(value,"en-US"));
        Assert.Equal("上次游玩：3 天前",Texts.Resolve(value,"zh-CN"));
        Assert.Equal("2026年10月28日",Texts.Resolve(SkillTextCatalog.Bind("date",new DateTime(2026,10,28)),"zh-CN"));
    }
    [Fact] public void 普通路径和不可信格式不能触发执行或崩溃()
    {
        Assert.Equal(@"D:\{not-expression}",Texts.Resolve(@"D:\{not-expression}","zh-CN"));
        Assert.Equal("盘点",Texts.Resolve("{loc title}","zh-CN"));
        Assert.Equal("{loc64 !!!}",Texts.Resolve("{loc64 !!!}","zh-CN"));
        Assert.Equal("missing",Texts.Resolve(SkillTextCatalog.Bind("missing",1),"zh-CN"));
    }
}
