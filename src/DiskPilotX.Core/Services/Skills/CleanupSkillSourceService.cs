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
        var directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] names = ["System.Private.CoreLib.dll", "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll", "System.Threading.dll", "System.Threading.Tasks.dll", "System.IO.dll", "System.IO.FileSystem.dll", "System.Console.dll"];
        return names.Select(name => Path.Combine(directory, name)).Append(typeof(ICleanupSkillDetector).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    });

    public Task<CleanupSkillSourceValidation> ValidateSourceAsync(string id, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var package = store.Load(id);
        var result = Compile(package, cancellationToken);
        return new CleanupSkillSourceValidation(result.Image is not null, package.Digest, result.Diagnostics);
    }, cancellationToken);

    /// <summary>宿主 wrapper 须先将当前完整包摘要展示给用户并完成 confirm_skill_code，再调用本方法；绝不能注册为模型工具。</summary>
    public CleanupSkillCodeAuthorization Authorize(string id, string confirmedDigest)
    {
        var package = store.Load(id);
        if (string.IsNullOrEmpty(package.Source) || !string.Equals(package.Digest, confirmedDigest, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("源码包未确认或包内容已变化，必须重新确认。");
        return new(id, package.Digest, issuer);
    }

    public async Task<CleanupSkillPreview> RunAsync(CleanupSkillCodeAuthorization authorization, string scopePath, CancellationToken cancellationToken = default, IReadOnlyDictionary<string,string>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if(parameters is { Count: > 32 } || (parameters?.Any(p=>p.Key.Length>64 || p.Value.Length>4000) ?? false)) throw new ArgumentException("检测参数超过限制。");
        var inputs=parameters?.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal) ?? new Dictionary<string,string>();
        if (!ReferenceEquals(authorization.Issuer, issuer)) throw new UnauthorizedAccessException("授权不属于当前宿主服务。");
        var package = store.Load(authorization.Id);
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
            if (store.Load(package.Id).Digest != authorization.Digest) throw new UnauthorizedAccessException("编译期间包已变化，请重新确认。");
            var detector = (ICleanupSkillDetector)Activator.CreateInstance(type)!;
            var output = await Task.Run(async () => await detector.DetectAsync(new(package.Id, scope, package.Rules, inputs), cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (output is null || output.Count > 20_000) throw new InvalidDataException("源码候选为空或超出数量限制。");
            var candidates = output.Select(candidate => CleanupSkillPreviewer.VerifySourceCandidate(candidate, scope, package))
                .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            return new(package.Id, package.Digest, scope, candidates, ["这是可信源码执行，不是安全沙箱；返回结果仍须经宿主清单与最终确认。"]);
        }
        finally { loadContext.Unload(); }
    }

    private static CompilationResult Compile(CleanupSkillPackage package, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(package.Source)) return new(null, [new("missing_source", "技能包没有检测源码。", "error")]);
        var tree = CSharpSyntaxTree.ParseText(package.Source, new CSharpParseOptions(LanguageVersion.CSharp14), path: "src/Detector.cs", cancellationToken: token);
        var compilation = CSharpCompilation.Create("Cleanup_" + package.Digest, [tree], References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: false, nullableContextOptions: NullableContextOptions.Enable, deterministic: true));
        var diagnostics = new List<CleanupSkillSourceDiagnostic>();
        // 在编译符号阶段检查契约，避免仅校验源码就加载并触发模块初始化器。
        var contract = compilation.GetTypeByMetadataName(typeof(ICleanupSkillDetector).FullName!);
        var implementations = AllTypes(compilation.Assembly.GlobalNamespace).Where(t => t.TypeKind == TypeKind.Class && !t.IsAbstract && t.DeclaredAccessibility == Accessibility.Public &&
            t.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, contract))).ToArray();
        if (implementations.Length != 1 || implementations[0].IsGenericType || implementations[0].ContainingType is not null || !implementations[0].InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
            diagnostics.Add(new("detector_contract", "源码必须包含且仅包含一个顶层公开、非泛型、具有公开无参构造器的 ICleanupSkillDetector 实现。", "error"));
        using var output = new MemoryStream();
        var result = compilation.Emit(output, cancellationToken: token);
        diagnostics.AddRange(result.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(d =>
        {
            var position = d.Location.GetLineSpan().StartLinePosition;
            return new CleanupSkillSourceDiagnostic(d.Id, d.GetMessage(System.Globalization.CultureInfo.GetCultureInfo("zh-CN")), d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.Location.IsInSource ? position.Line + 1 : null, d.Location.IsInSource ? position.Character + 1 : null);
        }));
        return new(result.Success && diagnostics.All(d => d.Severity != "error") ? output.ToArray() : null, diagnostics);
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns) => ns.GetNamespaceMembers().SelectMany(AllTypes).Concat(ns.GetTypeMembers().SelectMany(NestedTypes));
    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol type) => new[] { type }.Concat(type.GetTypeMembers().SelectMany(NestedTypes));
    private sealed record CompilationResult(byte[]? Image, IReadOnlyList<CleanupSkillSourceDiagnostic> Diagnostics);
}
