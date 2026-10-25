using System.Net.Http;
﻿using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SpaceMonger.App.Logging;
using SpaceMonger.App.Diagnostics;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Services;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Services.Analysis;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.FileTree;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Scanning;
using SpaceMonger.Core.Services.Settings;
using SpaceMonger.Core.Services.Cleanup;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Treemap;
using SpaceMonger.Core.Services.Whitelist;

namespace SpaceMonger.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Gets the application-wide service provider for dependency injection.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppLog.Configure();
        // 模型流协议故障时把最近 SSE 原始帧转储到 logs/llm-streams/，用于事后定位流未完整结束类问题。
        SpaceMonger.Core.Services.Llm.LlmStreamDiagnostics.Configure(AppLog.LogDirectory);
        Log.Information("Application startup begin");
        base.OnStartup(e);
        CrashDiagnostics.Register(this);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ClearProviders().AddSerilog(dispose: true));

        // Theme manager (singleton, initialized early)
        services.AddSingleton<ThemeManager>();

        services.AddSingleton<IPathWhitelistMatcher, PathWhitelistMatcher>();
        services.AddSingleton<ISkillPromptProvider>(provider => AcceptanceAutomationServer.IsEnabled()
            ? new FileSkillPromptProvider([System.IO.Path.Combine(AppContext.BaseDirectory,"skills")],provider.GetRequiredService<SpaceMonger.Core.Services.Skills.CleanupSkillStore>().UserRoot)
            : new FileSkillPromptProvider());
        services.AddSingleton<IAiSkillRouter, AiSkillRouter>();

        // US1 services
        services.AddSingleton<FileScanner>();
        services.AddSingleton<IFileScanner, IncrementalFileScanner>();
        services.AddSingleton<ITreemapLayoutEngine, SquarifiedTreemapLayout>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<TreemapViewModel>();

        // US3 services
        services.AddSingleton<ICleanupService, CleanupService>();

        // US2 services
        services.AddSingleton<ISettingsService>(_ => new SettingsService(AcceptanceAutomationServer.IsEnabled()
            ? Environment.GetEnvironmentVariable("SPACEMONGER_ACCEPTANCE_SETTINGS") : null));
        services.AddSingleton<ModelHttpClientFactory>();
        services.AddSingleton<ModelClientFactory>();
        services.AddSingleton<ModelCatalogService>();
        services.AddSingleton<ModelConnectionTester>();
        services.AddSingleton<IDuplicateDetector, DuplicateDetector>();
        services.AddSingleton<RecommendationEngine>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<RecommendationsViewModel>();

        // Agent and chat services
        services.AddSingleton<IFileTreeQueryService, FileTreeQueryService>();
        services.AddSingleton<IAgentTool, FindByNameTool>();
        services.AddSingleton<IAgentTool, FindByPathTool>();
        services.AddSingleton<IAgentTool, ListChildrenTool>();
        services.AddSingleton<IAgentTool, SummarizeSubtreeTool>();
        services.AddSingleton<IAgentTool, FindLargeFilesTool>();
        services.AddSingleton<IAgentTool, ResolvePathTool>();
        services.AddSingleton<IAgentTool, ReadUnityRegistryContextTool>();
        services.AddSingleton<IAgentTool, ManageDiskSkillsTool>();
        services.AddSingleton(_ => new SpaceMonger.Core.Services.Skills.CleanupSkillStore(AcceptanceAutomationServer.IsEnabled() && Environment.GetEnvironmentVariable("SPACEMONGER_ACCEPTANCE_SETTINGS") is { Length: > 0 } acceptanceSettings
            ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(acceptanceSettings))!,"skills") : null));
        services.AddSingleton<SpaceMonger.Core.Services.Skills.CleanupSkillSourceService>();
        services.AddSingleton<IAgentTool, CleanupSkillTool>();
        services.AddSingleton<AssistantWorkspace>();
        services.AddSingleton(_ =>
        {
            var acceptanceSettings = AcceptanceAutomationServer.IsEnabled()
                ? Environment.GetEnvironmentVariable("SPACEMONGER_ACCEPTANCE_SETTINGS") : null;
            var directory = !string.IsNullOrWhiteSpace(acceptanceSettings)
                ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(acceptanceSettings))!
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceMonger");
            return new ConversationStore(System.IO.Path.Combine(directory, "Conversations"));
        });
        services.AddSingleton<DiskAssistantService>(provider => new DiskAssistantService(
            options => provider.GetRequiredService<ModelClientFactory>().Create(options.Endpoint),
            provider.GetServices<IAgentTool>(), provider.GetRequiredService<AssistantWorkspace>(),
            provider.GetRequiredService<ISettingsService>(), provider.GetRequiredService<RecommendationEngine>(),
            provider.GetRequiredService<ILoggerFactory>(), provider.GetRequiredService<ConversationStore>()));
        services.AddTransient<ChatViewModel>();

        // TreeView services
        services.AddTransient<TreeViewModel>();

        // Update services
        services.AddSingleton<UpdateService>();
        services.AddTransient<UpdateViewModel>();

        Services = services.BuildServiceProvider();

        // Initialize theme manager early
        Log.Information("Service provider built");

        var themeManager = Services.GetRequiredService<ThemeManager>();
        themeManager.Initialize();
        themeManager.Refresh();

        var settingsService = Services.GetRequiredService<ISettingsService>();
        L.SetLanguage(settingsService.LoadSettings().Language);

        var mainViewModel = Services.GetRequiredService<MainViewModel>();
        var treemapViewModel = Services.GetRequiredService<TreemapViewModel>();
        var recommendationsViewModel = Services.GetRequiredService<RecommendationsViewModel>();
        var settingsViewModel = Services.GetRequiredService<SettingsViewModel>();
        var chatViewModel = Services.GetRequiredService<ChatViewModel>();
        var treeViewModel = Services.GetRequiredService<TreeViewModel>();
        var updateViewModel = Services.GetRequiredService<UpdateViewModel>();

        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel
        };

        // Register the main window for backdrop effects
        themeManager.RegisterMainWindow(mainWindow);

        mainWindow.TreemapView.SetViewModel(treemapViewModel);
        mainWindow.SetTreemapViewModel(treemapViewModel);
        mainWindow.SetViewModels(recommendationsViewModel, settingsViewModel);
        mainWindow.SetChatViewModel(chatViewModel);
        mainWindow.SetTreeViewModel(treeViewModel);
        mainWindow.SetUpdateViewModel(updateViewModel);

        mainViewModel.ScanCompleted += session =>
        {
            Log.Information("Scan completed event received for {TargetPath}", session.TargetPath);
            treemapViewModel.SetRoot(session.RootEntry!, session);
            chatViewModel.SetContext(session, session.RootEntry!);
            treeViewModel.SetRoot(session.RootEntry!, session);
        };

        if (AcceptanceAutomationServer.IsEnabled()) mainWindow.ShowActivated = false;
        mainWindow.Show();
        Log.Information("Main window shown");


        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => updateViewModel.CheckForUpdateCommand.Execute(null));
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application exit begin with code {ExitCode}", e.ApplicationExitCode);
        AppLog.CloseAndFlush();
        base.OnExit(e);
    }
}

