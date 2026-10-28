using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DiskPilotX.Core.Services.Skills;

/// <summary>仅由宿主在 confirm_skill_code 通过后发放的能力对象，不可从模型参数或 JSON 创建。</summary>
public sealed class CleanupSkillCodeAuthorization
{
    public string Id { get; }
    public string Digest { get; }
    internal object Issuer { get; }
    internal CleanupSkillCodeAuthorization(string id, string digest, object issuer) { Id = id; Digest = digest; Issuer = issuer; }
}

/// <summary>可信源码编译与执行。Roslyn、固定引用随宿主发布，无须用户 SDK；此服务不是安全沙箱。</summary>
public sealed class CleanupSkillSourceService(CleanupSkillStore store)
{
    private readonly object issuer = new();
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    {
        // 不含 Registry / Process / 网络程序集：本机读取一律经 ISkillHostBridge 由宿主审计。
        string[] names = ["System.Private.CoreLib.dll", "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll", "System.Threading.dll", "System.Threading.Tasks.dll", "System.IO.dll", "System.IO.FileSystem.dll", "System.Console.dll", "System.Text.RegularExpressions.dll"];
        // 单文件发布时 Assembly.Location 为空串，不能直接拿来做路径。
        var directory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (string.IsNullOrEmpty(directory)) directory = AppContext.BaseDirectory;
        var paths = names.Select(name => Path.Combine(directory, name))
            .Append(ResolveContractAssemblyPath())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // 被裁剪或打进单文件 bundle 时引用程序集不在磁盘上，Roslyn 无法工作；给出可操作的说明而不是空路径异常。
        if (!paths.Any(path => path.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("找不到磁盘上的 .NET 引用程序集，无法在运行时编译源码。请使用文件夹方式发布（不要 PublishSingleFile）。");
        return [.. paths.Select(path => MetadataReference.CreateFromFile(path))];
    });

    /// <summary>契约接口所在程序集的磁盘位置；单文件发布下回退到 exe 同级目录。</summary>
    private static string ResolveContractAssemblyPath()
    {
        var location = typeof(ICleanupSkillDetector).Assembly.Location;
        return !string.IsNullOrWhiteSpace(location) && File.Exists(location)
            ? location
            : Path.Combine(AppContext.BaseDirectory, "DiskPilotX.Core.dll");
    }

    public Task<CleanupSkillSourceValidation> ValidateSourceAsync(string id, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var package = store.LoadReadOnly(id);
        CompilationResult result;
        // 运行时编译依赖磁盘上的引用程序集；打包方式不支持时给出可操作的诊断，而不是空路径异常。
        try { result = Compile(package, cancellationToken); }
        catch (InvalidOperationException ex) { return new CleanupSkillSourceValidation(false, package.Digest, [new("runtime_references", ex.Message, "error")], "none"); }
        return new CleanupSkillSourceValidation(result.Image is not null, package.Digest, result.Diagnostics, result.Contract);
    }, cancellationToken);

    /// <summary>宿主 wrapper 须先将当前完整包摘要展示给用户并完成 confirm_skill_code，再调用本方法；绝不能注册为模型工具。</summary>
    public CleanupSkillCodeAuthorization Authorize(string id, string confirmedDigest)
    {
        var package = store.LoadReadOnly(id);
        if (string.IsNullOrEmpty(package.Source) || !string.Equals(package.Digest, confirmedDigest, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("源码包未确认或包内容已变化，必须重新确认。");
        return new(id, package.Digest, issuer);
    }

    public async Task<CleanupSkillPreview> RunAsync(CleanupSkillCodeAuthorization authorization, string scopePath, CancellationToken cancellationToken = default, IReadOnlyDictionary<string,string>? parameters = null, SkillLocalizationState? localization = null)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if(parameters is { Count: > 32 } || (parameters?.Any(p=>p.Key.Length>64 || p.Value.Length>4000) ?? false)) throw new ArgumentException("检测参数超过限制。");
        var inputs=parameters?.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal) ?? new Dictionary<string,string>();
        if (!ReferenceEquals(authorization.Issuer, issuer)) throw new UnauthorizedAccessException("授权不属于当前宿主服务。");
        var package = store.LoadReadOnly(authorization.Id);
        if (!string.Equals(package.Digest, authorization.Digest, StringComparison.Ordinal)) throw new UnauthorizedAccessException("包已变化，旧授权失效。");
        var scope = CleanupSkillPreviewer.ValidateScope(scopePath);
        var compilation = await Task.Run(() => Compile(package, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (compilation.Image is null) throw new InvalidDataException("源码编译失败：" + string.Join("；", compilation.Diagnostics.Select(d => d.Message)));
        // 独立加载上下文仅方便回收程序集，不能隔离文件、网络或进程访问。取消需要可信检测器配合。
        var loadContext = new AssemblyLoadContext("cleanup-" + package.Id + "-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            using var image = new MemoryStream(compilation.Image);
            var assembly = loadContext.LoadFromStream(image);
            var type = assembly.GetTypes().Single(t => t.IsClass && !t.IsAbstract && t.IsPublic && typeof(ICleanupSkillDetector).IsAssignableFrom(t));
            cancellationToken.ThrowIfCancellationRequested();
            // 编译使用已验证的内存快照；执行前再次校验，避免编译期间的修改复用旧授权。
            if (store.LoadReadOnly(package.Id).Digest != authorization.Digest) throw new UnauthorizedAccessException("编译期间包已变化，请重新确认。");
            var detector = (ICleanupSkillDetector)Activator.CreateInstance(type)!;
            var output = await Task.Run(async () => await detector.DetectAsync(new(package.Id, scope, package.Rules, inputs, localization ?? SkillLocalizationState.FromCulture(System.Globalization.CultureInfo.CurrentUICulture)), cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (output is null || output.Count > 20_000) throw new InvalidDataException("源码候选为空或超出数量限制。");
            var candidates = output.Select(candidate => CleanupSkillPreviewer.VerifySourceCandidate(candidate, scope, package))
                .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            return new(package.Id, package.Digest, scope, candidates, ["这是可信源码执行，不是安全沙箱；返回结果仍须经宿主清单与最终确认。"]);
        }
        finally { loadContext.Unload(); }
    }

    /// <summary>宿主 wrapper 须先完成 confirm_skill_code，再调用本方法执行发现类（IAppDataProvider）skill；绝不能注册为模型工具。</summary>
    public async Task<AppDataDiscoveryResult> RunDiscoveryAsync(CleanupSkillCodeAuthorization authorization, ISkillHostBridge host, CancellationToken cancellationToken = default, IReadOnlyDictionary<string,string>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (parameters is { Count: > 32 } || (parameters?.Any(p => p.Key.Length > 64 || p.Value.Length > 4000) ?? false)) throw new ArgumentException("发现参数超过限制。");
        var inputs = parameters?.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) ?? new Dictionary<string,string>();
        if (!ReferenceEquals(authorization.Issuer, issuer)) throw new UnauthorizedAccessException("授权不属于当前宿主服务。");
        var package = store.LoadReadOnly(authorization.Id);
        if (string.IsNullOrWhiteSpace(package.Source) || !string.Equals(package.Digest, authorization.Digest, StringComparison.Ordinal)) throw new UnauthorizedAccessException("包已变化，旧授权失效。");
        var compilation = await Task.Run(() => Compile(package, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (compilation.Image is null) throw new InvalidDataException("源码编译失败：" + string.Join("；", compilation.Diagnostics.Select(d => d.Message)));
        var loadContext = new AssemblyLoadContext("appdata-" + package.Id + "-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            using var image = new MemoryStream(compilation.Image);
            var assembly = loadContext.LoadFromStream(image);
            var type = assembly.GetTypes().Single(t => t.IsClass && !t.IsAbstract && t.IsPublic && typeof(IAppDataProvider).IsAssignableFrom(t));
            cancellationToken.ThrowIfCancellationRequested();
            if (store.LoadReadOnly(package.Id).Digest != authorization.Digest) throw new UnauthorizedAccessException("编译期间包已变化，请重新确认。");
            var provider = (IAppDataProvider)Activator.CreateInstance(type)!;
            var output = await Task.Run(async () => await provider.DiscoverAsync(new(package.Id, host, inputs, package.Localizations), cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // 发现类允许「什么都没找到」——源码可返回解释性视图；只有数量越界才是错误。
            if (output?.Entries is null || output.Entries.Count > 20_000) throw new InvalidDataException("发现结果无效或超出数量限制。");
            var entries = output.Entries
                .Select(e => e with { AppId = TrimTo(e.AppId, 128) ?? "", Name = TrimTo(e.Name, 256) ?? "", Path = TrimTo(e.Path, 1024) ?? "", Detail = TrimTo(e.Detail, 512) })
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .DistinctBy(e => e.AppId + "|" + e.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new AppDataDiscoveryResult(entries, output.View);
        }
        finally { loadContext.Unload(); }
    }

    private static string? TrimTo(string? value, int maxLength) => string.IsNullOrEmpty(value) ? value : value.Length <= maxLength ? value : value[..maxLength];

    private static CompilationResult Compile(CleanupSkillPackage package, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(package.Source)) return new(null, [new("missing_source", "技能包没有检测源码。", "error")]);
        var tree = CSharpSyntaxTree.ParseText(package.Source, new CSharpParseOptions(LanguageVersion.CSharp14), path: "src/Detector.cs", cancellationToken: token);
        var compilation = CSharpCompilation.Create("Cleanup_" + package.Digest, [tree], References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: false, nullableContextOptions: NullableContextOptions.Enable, deterministic: true));
        var diagnostics = new List<CleanupSkillSourceDiagnostic>();
        // 在编译符号阶段检查契约，避免仅校验源码就加载并触发模块初始化器。
        var detectorContract = compilation.GetTypeByMetadataName(typeof(ICleanupSkillDetector).FullName!);
        var providerContract = compilation.GetTypeByMetadataName(typeof(IAppDataProvider).FullName!);
        var implementations = AllTypes(compilation.Assembly.GlobalNamespace).Where(t => t.TypeKind == TypeKind.Class && !t.IsAbstract && t.DeclaredAccessibility == Accessibility.Public &&
            t.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, detectorContract) || SymbolEqualityComparer.Default.Equals(i, providerContract))).ToArray();
        var implementsDetector = implementations.Length == 1 && implementations[0].AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, detectorContract));
        var implementsProvider = implementations.Length == 1 && implementations[0].AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, providerContract));
        if (implementations.Length != 1 || (implementsDetector && implementsProvider) || implementations[0].IsGenericType || implementations[0].ContainingType is not null || !implementations[0].InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
            diagnostics.Add(new("detector_contract", "源码必须包含且仅包含一个顶层公开、非泛型、具有公开无参构造器的 ICleanupSkillDetector 或 IAppDataProvider 实现，且不能同时实现两者。", "error"));
        using var output = new MemoryStream();
        var result = compilation.Emit(output, cancellationToken: token);
        diagnostics.AddRange(result.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(d =>
        {
            var position = d.Location.GetLineSpan().StartLinePosition;
            return new CleanupSkillSourceDiagnostic(d.Id, d.GetMessage(System.Globalization.CultureInfo.GetCultureInfo("zh-CN")), d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.Location.IsInSource ? position.Line + 1 : null, d.Location.IsInSource ? position.Character + 1 : null);
        }));
        return new(result.Success && diagnostics.All(d => d.Severity != "error") ? output.ToArray() : null, diagnostics,
            implementsDetector ? "detector" : implementsProvider ? "provider" : "none");
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns) => ns.GetNamespaceMembers().SelectMany(AllTypes).Concat(ns.GetTypeMembers().SelectMany(NestedTypes));
    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol type) => new[] { type }.Concat(type.GetTypeMembers().SelectMany(NestedTypes));
    private sealed record CompilationResult(byte[]? Image, IReadOnlyList<CleanupSkillSourceDiagnostic> Diagnostics, string Contract = "none");
}
