using System.Collections;
using System.Reflection;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;

namespace DiskPilotX.Core.Tests;

public sealed class UsnReplaySafetyTests : IDisposable
{
    private readonly string _fixture=Directory.CreateTempSubdirectory("diskpilot-usn-safe-").FullName;
    private static readonly Type StateType=typeof(IncrementalFileScanner).GetNestedType("VolumeScanState",BindingFlags.NonPublic)!;
    private static readonly Type ChangeType=typeof(IncrementalFileScanner).Assembly.GetType("DiskPilotX.Core.Services.Scanning.UsnChange")!;
    private static readonly Type KindType=typeof(IncrementalFileScanner).Assembly.GetType("DiskPilotX.Core.Services.Scanning.UsnChangeKind")!;
    private static readonly MethodInfo ApplyMethod=typeof(IncrementalFileScanner).GetMethod("ApplyChanges",BindingFlags.Static|BindingFlags.NonPublic)!;
    private object Change(long frn,long parent,string name,string kind,bool dir=false,long oldParent=0,string? oldName=null)
        =>Activator.CreateInstance(ChangeType,[frn,parent,name,Enum.Parse(KindType,kind),dir,0u,oldParent,oldName])!;
    private object State(FileEntry root,params FileEntry[] directories)
    {
        var state=Activator.CreateInstance(StateType,true)!;
        StateType.GetProperty("ScannedPath")!.SetValue(state,root.Path);
        StateType.GetProperty("CachedSession")!.SetValue(state,new ScanSession {TargetPath=root.Path,RootEntry=root});
        StateType.GetProperty("FrnIndex")!.SetValue(state,directories.Append(root).ToDictionary(d=>d.FileReferenceNumber));
        return state;
    }
    private static FileEntry Root(object state)=>((ScanSession)StateType.GetProperty("CachedSession")!.GetValue(state)!).RootEntry!;
    private static void Apply(object state,params object[] changes)
    {
        var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ChangeType))!;
        foreach(var c in changes)list.Add(c);
        try {ApplyMethod.Invoke(null,[list,state,CancellationToken.None]);}
        catch(TargetInvocationException e)when(e.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();}
    }
    private FileEntry Dir(string name,long frn,FileEntry? parent=null)
    {
        string path=Path.Combine(parent?.Path??_fixture,name);Directory.CreateDirectory(path);
        var d=new FileEntry {Name=name,Path=path,IsDirectory=true,FileReferenceNumber=frn,Parent=parent};parent?.Children.Add(d);return d;
    }
    [Fact]
    public void 新建事件重复重放只保留一个真实文件且不更改旧快照()
    {
        var root=Dir("重放",1);File.WriteAllText(Path.Combine(root.Path,"新建.txt"),"abc");
        var state=State(root);var c=Change(2,1,"新建.txt","Create");Apply(state,c);Apply(state,c);
        Assert.Single(Root(state).Children);Assert.Equal(3,Root(state).Size);Assert.Empty(root.Children);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 移出目标范围从缓存树与目录索引移除(bool directory)
    {
        var root=Dir("范围",10);
        FileEntry child=directory?Dir("原目录",11,root):new FileEntry {Name="原.txt",Path=Path.Combine(root.Path,"原.txt"),Parent=root,Size=7,FileReferenceNumber=11};
        if(!directory)root.Children.Add(child);
        var state=directory?State(root,child):State(root);
        Apply(state,Change(11,999,"移出","Rename",directory,10,child.Name));
        Assert.Empty(Root(state).Children);
        var index=(Dictionary<long,FileEntry>)StateType.GetProperty("FrnIndex")!.GetValue(state)!;Assert.DoesNotContain(11,index.Keys);
        Assert.Single(root.Children);
    }
    [Fact]
    public void 目录改名所有后代路径深度与扩展名保持一致且重放幂等()
    {
        var root=Dir("改名",20);var d=Dir("旧目录",21,root);var nested=Dir("嵌套",22,d);
        nested.Children.Add(new FileEntry {Name="后代.bin",Path=Path.Combine(nested.Path,"后代.bin"),Parent=nested,FileReferenceNumber=23,Size=3});
        var state=State(root,d,nested);var c=Change(21,20,"新目录","Rename",true,20,"旧目录");Apply(state,c);Apply(state,c);
        var renamed=Assert.Single(Root(state).Children);var sub=Assert.Single(renamed.Children);var leaf=Assert.Single(sub.Children);
        Assert.Equal(Path.Combine(renamed.Path,"嵌套"),sub.Path);Assert.Equal(Path.Combine(sub.Path,"后代.bin"),leaf.Path);
        Assert.Equal(3,leaf.Depth);Assert.Equal(Path.Combine(root.Path,"旧目录"),d.Path);
    }
    [Fact]
    public void 范围外目录移入读取现有子树且重复事件不添加副本()
    {
        var root=Dir("移入",30);var moved=Path.Combine(root.Path,"已有资料");Directory.CreateDirectory(moved);File.WriteAllText(Path.Combine(moved,"数据.txt"),"abc");
        var state=State(root);var c=Change(31,30,"已有资料","Rename",true,999,"外部");Apply(state,c);Apply(state,c);
        var imported=Assert.Single(Root(state).Children);Assert.Single(imported.Children);Assert.Equal(3,Root(state).Size);
        Assert.Same(imported,imported.Children[0].Parent);Assert.Equal(31,imported.FileReferenceNumber);
    }
    [Fact]
    public void 缺失创建对象不能伪造零字节快照而必须失效()
    {
        var root=Dir("缺失",40);var state=State(root);
        Assert.Throws<InvalidOperationException>(()=>Apply(state,Change(41,40,"已消失.txt","Create")));
    }
    [Fact]
    public void 重放旧删除不能移除同名不同FRN的新文件()
    {
        var root=Dir("旧删除",50);var newer=new FileEntry {Name="同名.txt",Path=Path.Combine(root.Path,"同名.txt"),FileReferenceNumber=52,Parent=root,Size=3};root.Children.Add(newer);
        var state=State(root);Apply(state,Change(51,50,"同名.txt","Delete"));
        Assert.Single(Root(state).Children);Assert.Equal(52,Root(state).Children[0].FileReferenceNumber);
    }
    [Fact]
    public void 移入子树和重命名均继续遵守排除规则()
    {
        var root=Dir("排除",60);var incoming=Path.Combine(root.Path,"移入");Directory.CreateDirectory(incoming);
        File.WriteAllText(Path.Combine(incoming,"保留.txt"),"abc");File.WriteAllText(Path.Combine(incoming,"排除.txt"),"secret");
        var state=State(root);StateType.GetProperty("ScanWhitelist")!.SetValue(state,new List<PathWhitelistEntry>{new(){Path=Path.Combine(incoming,"排除.txt")},new(){Path=Path.Combine(root.Path,"禁止")}});
        Apply(state,Change(61,60,"移入","Rename",true,999,"旧目录"));
        var d=Assert.Single(Root(state).Children);Assert.Single(d.Children);Assert.Equal("保留.txt",d.Children[0].Name);
        Directory.Move(incoming,Path.Combine(root.Path,"禁止"));
        Apply(state,Change(61,60,"禁止","Rename",true,60,"移入"));Assert.Empty(Root(state).Children);
    }
    [Fact]
    public void 无法读取修改文件必须使缓存失效而不是继续显示旧大小()
    {
        var root=Dir("修改缺失",70);root.Children.Add(new FileEntry {Name="消失.txt",Path=Path.Combine(root.Path,"消失.txt"),FileReferenceNumber=71,Parent=root,Size=100});
        Assert.Throws<InvalidOperationException>(()=>Apply(State(root),Change(71,70,"消失.txt","Modify")));
    }
    [Fact]
    public void 普通遍历无FRN节点遇到旧删除时核验实物身份而不误删()
    {
        var root=Dir("无引用删除",80);var path=Path.Combine(root.Path,"仍存在.txt");File.WriteAllText(path,"abc");
        root.Children.Add(new FileEntry {Name="仍存在.txt",Path=path,Size=3,Parent=root,FileReferenceNumber=0});
        var state=State(root);Apply(state,Change(999999,80,"仍存在.txt","Delete"));
        Assert.Single(Root(state).Children);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 目录改名涉及排除规则时失效重扫而不是丢失或泄漏后代(bool previouslyExcluded)
    {
        var root=Dir("路径排除",90);var dir=Dir("旧目录",91,root);
        string oldLeaf=Path.Combine(dir.Path,"秘密.txt");File.WriteAllText(oldLeaf,"secret");
        if(!previouslyExcluded) dir.Children.Add(new FileEntry {Name="秘密.txt",Path=oldLeaf,Parent=dir,Size=6});
        dir.RecalculateSize();root.RecalculateSize();
        var state=State(root,dir);string target=Path.Combine(root.Path,"新目录");
        StateType.GetProperty("ScanWhitelist")!.SetValue(state,new List<PathWhitelistEntry>{new(){Path=previouslyExcluded?oldLeaf:Path.Combine(target,"秘密.txt")}});
        Directory.Move(dir.Path,target);
        var error=Assert.Throws<InvalidOperationException>(()=>Apply(state,Change(91,90,"新目录","Rename",true,90,"旧目录")));
        Assert.Contains("排除",error.Message);
        Assert.Equal(Path.Combine(root.Path,"旧目录"),dir.Path);
        Assert.Equal(previouslyExcluded?0:6,root.Size);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 重复目录创建必须核验当前实物类型与存在性(bool missing)
    {
        var root=Dir("目录重放",100);var dir=Dir("对象",101,root);var state=State(root,dir);
        Directory.Move(dir.Path,Path.Combine(root.Path,"留证"));
        if(!missing)File.WriteAllText(dir.Path,"replacement");
        Assert.Throws<InvalidOperationException>(()=>Apply(state,Change(101,100,"对象","Create",true)));
    }
    [Fact]
    public void 真实目录重复创建幂等且不改变旧快照()
    {
        var root=Dir("真实目录",110);var dir=Dir("对象",111,root);
        var native=typeof(IncrementalFileScanner).Assembly.GetType("DiskPilotX.Core.Services.Scanning.NtfsUsnNative")!;
        dir.FileReferenceNumber=(long)native.GetMethod("GetFileReferenceNumber",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,[dir.Path])!;
        Assert.NotEqual(0,dir.FileReferenceNumber);
        var state=State(root,dir);var create=Change(dir.FileReferenceNumber,110,"对象","Create",true);
        Apply(state,create);Apply(state,create);
        Assert.Single(Root(state).Children);Assert.Single(root.Children);
    }
    [Fact]
    public void 重复目录创建遇到同类型但身份已替换仍须失效()
    {
        var root=Dir("目录替换",120);var dir=Dir("对象",121,root);
        var native=typeof(IncrementalFileScanner).Assembly.GetType("DiskPilotX.Core.Services.Scanning.NtfsUsnNative")!;
        dir.FileReferenceNumber=(long)native.GetMethod("GetFileReferenceNumber",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,[dir.Path])!;
        var state=State(root,dir);Directory.Move(dir.Path,Path.Combine(root.Path,"旧实物留证"));Directory.CreateDirectory(dir.Path);
        Assert.Throws<InvalidOperationException>(()=>Apply(state,Change(dir.FileReferenceNumber,120,"对象","Create",true)));
    }
    public void Dispose()=>Directory.Delete(_fixture,true);
}
