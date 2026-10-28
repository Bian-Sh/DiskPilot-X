using System.Text.Json;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Skills;
namespace DiskPilotX.Core.Tests;
public sealed class SkillCatalogJsonLocalizationTests
{
    [Fact] public void 仅有源码界面的技能目录响应文化且不改原始模型文档()
    {
        var root=Path.Combine(Path.GetTempPath(),"skill-catalog-"+Guid.NewGuid().ToString("N"));
        try
        {
            var builtin=Path.Combine(root,"builtin");var skill=Path.Combine(builtin,"demo-ui");
            Directory.CreateDirectory(Path.Combine(skill,"src"));Directory.CreateDirectory(Path.Combine(skill,"i18n"));
            File.WriteAllText(Path.Combine(skill,"SKILL.md"),"# Original title\n\n## Purpose\nOriginal safety instructions");
            File.WriteAllText(Path.Combine(skill,"src","Provider.cs"),"// 桥接源码夹具");
            File.WriteAllText(Path.Combine(skill,"i18n","en-US.json"),"""{"skill.name":"Inventory","skill.description":"Read only"}""");
            File.WriteAllText(Path.Combine(skill,"i18n","zh-TW.json"),"""{"skill.name":"盤點","skill.description":"唯讀"}""");
            var plain=Path.Combine(builtin,"plain-doc");Directory.CreateDirectory(Path.Combine(plain,"i18n"));
            File.WriteAllText(Path.Combine(plain,"SKILL.md"),"# 原文说明\n\n## Purpose\n纯Markdown不改");
            File.WriteAllText(Path.Combine(plain,"i18n","en-US.json"),"""{"skill.name":"Do not apply to Markdown"}""");
            var culture="en-US";var provider=new FileSkillPromptProvider([builtin],Path.Combine(root,"user"),()=>culture);
            Assert.Equal("Inventory",provider.GetSkillCatalog().Single(item=>item.Id=="demo-ui").DisplayName);
            var raw=provider.GetRawContent("demo-ui");culture="zh-TW";
            Assert.Equal("盤點",provider.GetSkillCatalog().Single(item=>item.Id=="demo-ui").DisplayName);
            Assert.Equal(raw,provider.GetRawContent("demo-ui"));
            Assert.Equal("原文说明",provider.GetSkillCatalog().Single(item=>item.Id=="plain-doc").DisplayName);
            File.WriteAllText(Path.Combine(skill,"i18n","en-US.json"),"{");
            Assert.Equal("Original title",provider.GetSkillCatalog().Single(item=>item.Id=="demo-ui").DisplayName);
            Assert.Equal(2,provider.GetSkillCatalog().Count);
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
