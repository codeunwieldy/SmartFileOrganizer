using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using SmartFileOrganizer.App.Pages;
using SmartFileOrganizer.App.Platforms.Windows;
using SmartFileOrganizer.App.Services;
using SmartFileOrganizer.App.ViewModels;

namespace SmartFileOrganizer.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(_ => { /* add fonts here if needed */ });

        // ---------- Core services ----------
        builder.Services.AddSingleton<IFileScanner, FileScanner>();
        builder.Services.AddSingleton<IIndexStore, IndexStore>();
        builder.Services.AddSingleton<IExecutorService, ExecutorService>();
        builder.Services.AddSingleton<ISnapshotService, SnapshotService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IOverviewService, OverviewService>();
        builder.Services.AddSingleton<IHashingService, FileHasher>();
        builder.Services.AddSingleton<IDedupeService, DedupeService>();
        builder.Services.AddSingleton<IRuleStore, RuleStore>();
        builder.Services.AddSingleton<IRuleEngine, RuleEngine>();

        // Folder picker per platform
#if WINDOWS
        builder.Services.AddSingleton<IFolderPicker, FolderPickerWindows>();
#elif MACCATALYST
        builder.Services.AddSingleton<IFolderPicker, FolderPickerMac>();
#else
        builder.Services.AddSingleton<IFolderPicker>(_ => new NotSupportedFolderPicker());
#endif

        // ---------- HTTP + Planner ----------
        builder.Services.AddSingleton(new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5088")
        });
        builder.Services.AddSingleton<IPlanService, PlanService>();

        // ---------- ViewModels ----------
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<AdvancedPlannerViewModel>();

        // ---------- Pages ----------
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<AdvancedPlannerPage>();
        builder.Services.AddTransient<RulesPage>();
        builder.Services.AddTransient<OverviewPage>();
        builder.Services.AddTransient<DuplicatesPage>();
        // ConflictResolverPage is created with data via `new`, so no DI needed

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}