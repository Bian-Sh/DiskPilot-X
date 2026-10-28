using DiskPilotX.Core.Services.Skills;
namespace DiskPilotX.Core.Tests;
public sealed class SkillLocalizationJsonFilesTests
{
    [Fact] public void 用户技能版本保存独立JSON且不接受目录文案被篡改()
    {
        var root=Path.Combine(Path.GetTempPath(),"skill-json-files-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new CleanupSkillStore(root,[]);
            var package=store.Save(new CleanupSkillPackage { Id="json-ui",Title="标题",Description="只读",SkillMarkdown="# 标题",Source="// 只读",
                Localizations=new() { ["en-US"]=new() { ["skill.name"]="Inventory" },["zh-CN"]=new() { ["skill.name"]="盘点" } } });
            var file=Path.Combine(root,package.Id,"versions",package.Digest,"i18n","en-US.json");
            Assert.True(File.Exists(file));
            Assert.Equal(package.Digest,store.Load(package.Id).Digest);
            File.WriteAllText(file,"""{"skill.name":"Changed outside host"}""");
            Assert.ThrowsAny<Exception>(()=>store.Load(package.Id));
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }

    [Fact] public void 文化名称大小写不同仍可保存重载()
    {
        var root=Path.Combine(Path.GetTempPath(),"skill-json-case-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new CleanupSkillStore(root,[]);
            var saved=store.Save(new CleanupSkillPackage { Id="json-ui",Title="标题",Description="只读",SkillMarkdown="# 标题",Source="// 只读",
                Localizations=new() { ["EN-us"]=new() { ["skill.name"]="Inventory" } } });
            Assert.Equal(saved.Digest,store.Load(saved.Id).Digest);
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
