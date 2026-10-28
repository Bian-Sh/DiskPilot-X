using System.IO;
using System.Text.Json.Nodes;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Services;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Services.Skills;
using Microsoft.Extensions.DependencyInjection;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private JsonObject? csharpSkillProbe;

    internal object AcceptanceCSharpSkill(string? kind)
    {
        if (!AcceptanceAutomationServer.IsEnabled() || kind is not ("steam" or "unity" or "steam-empty" or "unity-empty"))
            throw new InvalidOperationException("仅支持验收模式下的两个已授权只读技能夹具。");
        var settings=Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS");
        if(string.IsNullOrWhiteSpace(settings)) throw new InvalidOperationException("需要独立验收设置目录。");
        csharpSkillProbe=new JsonObject { ["running"]=true };
        _=Run(); return csharpSkillProbe;
        async Task Run()
        {
            try
            {
                var fixture=Path.Combine(Path.GetDirectoryName(settings)!,"只读技能夹具",kind);
                Directory.CreateDirectory(fixture);
                var steam=kind.StartsWith("steam",StringComparison.Ordinal);
                if(steam && kind=="steam")
                {
                    var apps=Path.Combine(fixture,"steamapps");Directory.CreateDirectory(apps);
                    var game=Path.Combine(apps,"common","FixtureGame");Directory.CreateDirectory(game);
                    File.WriteAllText(Path.Combine(game,"sample.dat"),"只读盘点夹具");
                    File.WriteAllText(Path.Combine(apps,"libraryfolders.vdf"),"\"libraryfolders\" { \"0\" { \"path\" \""+fixture.Replace("\\","\\\\")+"\" } }");
                    File.WriteAllText(Path.Combine(apps,"appmanifest_998877.acf"),"\"AppState\" { \"appid\" \"998877\" \"name\" \"Fixture Game\" \"installdir\" \"FixtureGame\" \"LastPlayed\" \"0\" }");
                }
                if(!steam && kind=="unity")
                {
                    var project=Path.Combine(fixture,"FixtureUnity");
                    foreach(var folder in new[]{"Assets","ProjectSettings","Library"})Directory.CreateDirectory(Path.Combine(project,folder));
                    File.WriteAllText(Path.Combine(project,"ProjectSettings","ProjectVersion.txt"),"m_EditorVersion: 6000.0.23f1");
                    File.WriteAllText(Path.Combine(project,"Library","sample.dat"),"只读Unity缓存夹具");
                }
                var id=steam?"steam-library-organizer":"unity-cache-check";
                var store=App.Services!.GetRequiredService<CleanupSkillStore>();var package=store.LoadReadOnly(id);
                var source=App.Services!.GetRequiredService<CleanupSkillSourceService>();
                var validation=await source.ValidateSourceAsync(id);
                if(!validation.Success)throw new InvalidDataException(System.Text.Json.JsonSerializer.Serialize(validation));
                var parameters=new Dictionary<string,string>();
                if(!steam) { parameters["roots"]=kind=="unity-empty"?"":fixture;parameters["depth"]="3"; }
                var logs=new System.Collections.Concurrent.ConcurrentQueue<string>();
                var host=new DefaultSkillHostBridge(id,parameters:parameters,logger:logs.Enqueue);
                ISkillHostBridge bridge=steam?new SteamFixtureBridge(host,fixture):host;
                var authorization=source.Authorize(id,package.Digest);
                var result=await source.RunDiscoveryAsync(authorization,bridge,parameters:parameters);
                if(result.View is {} view)
                {
                    view=view with { Localizations=view.Localizations??package.Localizations };
                    localizationFixture?.Close();
                    localizationFixture=new SkillWindow(view) { Owner=this };
                    localizationFixture.Show();
                }
                csharpSkillProbe=new JsonObject { ["running"]=false,["ok"]=true,["id"]=id,["digest"]=package.Digest,["kind"]=kind,
                    ["entries"]=result.Entries.Count,["sections"]=result.View?.Sections.Count??0,["culture"]=L.CurrentLanguageName,
                    ["validation"]=System.Text.Json.JsonSerializer.SerializeToNode(validation),["logs"]=System.Text.Json.JsonSerializer.SerializeToNode(logs.ToArray()),
                    ["view"]=System.Text.Json.JsonSerializer.SerializeToNode(result.View),["base_directory"]=AppContext.BaseDirectory };
            }
            catch(Exception error){csharpSkillProbe=new JsonObject { ["running"]=false,["ok"]=false,["error"]=error.ToString() };}
        }
    }
    internal object? AcceptanceCSharpSkillState()=>csharpSkillProbe;

    /// <summary>仅把夹具目录作为 Steam 注册表位置，其余读取沿用宿主真实只读桥。</summary>
    private sealed class SteamFixtureBridge(DefaultSkillHostBridge host,string root) : ISkillHostBridge
    {
        public string SkillId=>host.SkillId;
        public IReadOnlyDictionary<string,string> Parameters=>host.Parameters;
        public SkillLocalizationState GetLocalizationState()=>host.GetLocalizationState();
        public IDisposable SubscribeLocalizationChanged(Action<SkillLocalizationState> observer)=>host.SubscribeLocalizationChanged(observer);
        public void Log(string message)=>host.Log(message);
        public string? ReadRegistry(string keyPath,string valueName)=>root;
        public bool DirectoryExists(string path)=>host.DirectoryExists(path);
        public bool FileExists(string path)=>host.FileExists(path);
        public IReadOnlyList<string> EnumerateDirectories(string path)=>host.EnumerateDirectories(path);
        public IReadOnlyList<string> EnumerateFiles(string path,string searchPattern="*")=>host.EnumerateFiles(path,searchPattern);
        public string? ReadTextFile(string path)=>host.ReadTextFile(path);
        public long GetDirectorySize(string path)=>host.GetDirectorySize(path);
        public DateTime? GetDirectoryLastWriteTimeUtc(string path)=>host.GetDirectoryLastWriteTimeUtc(path);
        public IReadOnlyList<SkillHostEntry> QueryScan(string? rootPath=null,int maxCount=20000)=>host.QueryScan(rootPath,maxCount);
    }
}
