using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SmartFileOrganizer.App.Services;
using SmartFileOrganizer.App.ViewModels;
using System.Collections.Specialized;

namespace SmartFileOrganizer.App.Pages;

public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        // Auto-scroll the progress list as lines are added
        vm.ProgressLines.CollectionChanged += ProgressLines_CollectionChanged;
    }

    private void ProgressLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (BindingContext is not MainViewModel vm) return;
        if (vm.ProgressLines.Count == 0) return;

        // Scroll to last line (index-based overload; no ScrollToAsync in CollectionView)
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                ProgressList.ScrollTo(vm.ProgressLines.Count - 1,
                                      position: ScrollToPosition.End,
                                      animate: true);
            }
            catch { /* ignore transient layout issues */ }
        });
    }

    private void OnToggleTheme(object sender, EventArgs e)
    {
        var app = Application.Current!;
        app.UserAppTheme = app.UserAppTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
    }

    // This is the event the XAML is pointing to
    private void OnPrefChanged(object sender, ToggledEventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            var mi = typeof(MainViewModel).GetMethod("SavePrefs",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            mi?.Invoke(vm, null);
        }
    }

    private async void OnOpenRules(object sender, EventArgs e)
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services is null) return;

        var page = services.GetService<RulesPage>()
                   ?? new RulesPage(
                        services.GetRequiredService<IRuleStore>(),
                        services.GetRequiredService<IFileScanner>(),
                        services.GetRequiredService<IRuleEngine>());

        await Navigation.PushAsync(page);
    }
}
