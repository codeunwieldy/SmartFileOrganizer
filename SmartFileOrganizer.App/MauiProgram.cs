using CommunityToolkit.Maui;
using Microsoft.Extensions.DependencyInjection;
using SmartFileOrganizer.App;
using SmartFileOrganizer.App.Pages;
using SmartFileOrganizer.App.Services;
using SmartFileOrganizer.App.ViewModels;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

        // Core services
        builder.Services.AddSingleton<IFileScanner, FileScanner>();
        builder.Services.AddSingleton<IIndexStore, IndexStore>();
        builder.Services.AddSingleton<IExecutorService, ExecutorService>();   // Ensure class implements IExecutorService
        builder.Services.AddSingleton<ISnapshotService, SnapshotService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IOverviewService, OverviewService>();
        builder.Services.AddSingleton<IHashingService, FileHasher>();
        builder.Services.AddSingleton<IDedupeService, DedupeService>();
        builder.Services.AddSingleton<IRuleStore, RuleStore>();
        builder.Services.AddSingleton<IRuleEngine, RuleEngine>();

        // Http client + planner (no AddHttpClient)
        builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri("http://localhost:5088") });
        builder.Services.AddSingleton<IPlanService, PlanService>();

        // VMs + Pages
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<AdvancedPlannerViewModel>();
        builder.Services.AddTransient<AdvancedPlannerPage>();
        builder.Services.AddTransient<OverviewPage>();
        builder.Services.AddTransient<RulesPage>();

#if WINDOWS
        builder.Services.AddSingleton<IFolderPicker, FolderPickerWindows>();
#elif MACCATALYST
        builder.Services.AddSingleton<IFolderPicker, FolderPickerMac>();
#else
        builder.Services.AddSingleton<IFolderPicker>(_ => new NotSupportedFolderPicker());
#endif

        return builder.Build();
    }
}
