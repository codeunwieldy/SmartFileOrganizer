using SmartFileOrganizer.App.Services;
using SmartFileOrganizer.App.ViewModels;

namespace SmartFileOrganizer.App.Pages;

public partial class AdvancedPlannerPage : ContentPage
{
    private readonly AdvancedPlannerViewModel _vm;

    public AdvancedPlannerPage(AdvancedPlannerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Get roots from the MainViewModel (the list you show as chips on MainPage)
        var services = Application.Current?.Handler?.MauiContext?.Services;
        var main = services?.GetService<MainViewModel>();
        var roots = main?.SelectedRoots?.ToList() ?? new List<string>();

        // If nothing was selected, default to the user profile so the tree renders
        if (roots.Count == 0)
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        await _vm.InitializeAsync(roots);
    }

    private async void OnSave(object sender, EventArgs e)
    {
        var planService = Application.Current?.Handler?.MauiContext?.Services?.GetService<IPlanService>();
        if (planService is not null)
            await planService.CommitAsync(_vm.GetEditedPlan(), CancellationToken.None);

        await DisplayAlert("Saved", "Plan updated. The AI will follow this structure.", "OK");
        await Navigation.PopAsync();
    }
}
