using Microsoft.Extensions.DependencyInjection;
using SmartFileOrganizer.App.Services;
using SmartFileOrganizer.App.ViewModels;

namespace SmartFileOrganizer.App.Pages;

public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private void OnToggleTheme(object sender, EventArgs e)
    {
        var app = Application.Current!;
        app.UserAppTheme = app.UserAppTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
    }

    private void OnPrefChanged(object sender, ToggledEventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            var mi = typeof(MainViewModel)
                .GetMethod("SavePrefs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            mi?.Invoke(vm, null);
        }
    }

    private async void OnOpenRules(object sender, EventArgs e)
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services is null) return;

        // Prefer DI for the page; otherwise construct with all required deps
        var page = services.GetService<RulesPage>()
                   ?? new RulesPage(
                        services.GetRequiredService<IRuleStore>(),
                        services.GetRequiredService<IFileScanner>(),
                        services.GetRequiredService<IRuleEngine>());

        await Navigation.PushAsync(page);
    }
}

