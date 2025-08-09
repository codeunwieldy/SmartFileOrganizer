using SmartFileOrganizer.App.Services;
using SmartFileOrganizer.App.ViewModels;

namespace SmartFileOrganizer.App.Pages;

public partial class AdvancedPlannerPage : ContentPage
{
    private readonly AdvancedPlannerViewModel _vm;
    private readonly IPlanService? _planService;

    public AdvancedPlannerPage(AdvancedPlannerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;

        // Resolve from MAUI’s service provider (safe to do in a Page)
        _planService = Microsoft.Maui.Controls.Application.Current?
            .Handler?
            .MauiContext?
            .Services?
            .GetService<IPlanService>();

    }

    private async void OnSave(object sender, EventArgs e)
    {
        if (_planService is not null)
        {
            var edited = _vm.GetEditedPlan();
            await _planService.CommitAsync(edited, CancellationToken.None);
        }

        await DisplayAlert("Saved", "Plan updated. The AI will follow this structure.", "OK");
        await Navigation.PopAsync();
    }
}