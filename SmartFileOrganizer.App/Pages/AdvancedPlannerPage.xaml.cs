using SmartFileOrganizer.App.Services;
using SmartFileOrganizer.App.ViewModels;

namespace SmartFileOrganizer.App.Pages;

public partial class AdvancedPlannerPage : ContentPage
{
    private readonly AdvancedPlannerViewModel _vm;
    private readonly MainViewModel _mainViewModel;

    public AdvancedPlannerPage(AdvancedPlannerViewModel vm, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _vm = vm;
        _mainViewModel = mainViewModel;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var roots = _mainViewModel.SelectedRoots?.ToList() ?? new List<string>();

        // If nothing was selected, default to the user profile so the tree renders
        if (roots.Count == 0)
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        await _vm.InitializeAsync(roots, _mainViewModel.CurrentPlan);
        UpdatePlanSummary();
    }

    private async void OnSave(object sender, EventArgs e)
    {
        try
        {
            StatusLabel.Text = "Saving...";
            
            var planService = Application.Current?.Handler?.MauiContext?.Services?.GetService<IPlanService>();
            if (planService is not null)
                await planService.CommitAsync(_vm.GetEditedPlan(), CancellationToken.None);

            await DisplayAlert("? Saved", "Plan updated successfully! The AI will follow this structure when organizing files.", "OK");
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Save failed";
            await DisplayAlert("? Error", $"Failed to save plan: {ex.Message}", "OK");
        }
    }

    private async void OnCancel(object sender, EventArgs e)
    {
        var confirm = await DisplayAlert("? Confirm", "Discard all changes and return to main page?", "Yes", "No");
        if (confirm)
        {
            await Navigation.PopAsync();
        }
    }

    private async void OnPreviewChanges(object sender, EventArgs e)
    {
        var plan = _vm.GetEditedPlan();
        var moves = plan.Moves.Count;
        var hardlinks = plan.Hardlinks.Count;
        
        var message = $"Preview of planned changes:\n\n" +
                     $"?? File Moves: {moves}\n" +
                     $"?? Hardlinks: {hardlinks}\n" +
                     $"?? New Folders: {_vm.DestinationRoot.Count(f => f.IsFolder)}\n\n";

        if (moves > 0)
        {
            message += "First 5 moves:\n";
            foreach (var move in plan.Moves.Take(5))
            {
                message += $"• {Path.GetFileName(move.Source)} ? {Path.GetDirectoryName(move.Destination)}\n";
            }
        }

        await DisplayAlert("??? Plan Preview", message, "OK");
    }

    private void OnCreateDocumentsFolder(object sender, EventArgs e)
    {
        CreateQuickFolder("Documents");
    }

    private void OnCreateImagesFolder(object sender, EventArgs e)
    {
        CreateQuickFolder("Images");
    }

    private void OnCreateMediaFolder(object sender, EventArgs e)
    {
        CreateQuickFolder("Media");
    }

    private void OnCreateArchiveFolder(object sender, EventArgs e)
    {
        CreateQuickFolder("Archive");
    }

    private void OnCreateNewFolder(object sender, EventArgs e)
    {
        var folderName = NewFolderEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folderName))
        {
            DisplayAlert("? Error", "Please enter a folder name", "OK");
            return;
        }

        CreateQuickFolder(folderName);
        NewFolderEntry.Text = "";
    }

    private void CreateQuickFolder(string folderName)
    {
        try
        {
            // Find the root path from the current roots
            var rootPath = _mainViewModel.SelectedRoots?.FirstOrDefault() ?? 
                          Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            
            var destinationPath = Path.Combine(rootPath, "Organized", folderName);
            
            // Add to destination tree if not already exists
            var existingFolder = _vm.DestinationRoot.FirstOrDefault(f => 
                string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase));
            
            if (existingFolder == null)
            {
                var newFolder = new SmartFileOrganizer.App.Models.PlanTreeNode
                {
                    Name = folderName,
                    FullPath = destinationPath,
                    IsFolder = true
                };
                
                _vm.DestinationRoot.Add(newFolder);
                UpdatePlanSummary();
                
                StatusLabel.Text = $"Created folder: {folderName}";
            }
            else
            {
                StatusLabel.Text = $"Folder '{folderName}' already exists";
            }
        }
        catch (Exception ex)
        {
            DisplayAlert("? Error", $"Failed to create folder: {ex.Message}", "OK");
        }
    }

    private void OnClearAllMoves(object sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var confirm = await DisplayAlert("? Confirm", "Clear all planned moves and folder creations?", "Yes", "No");
            if (confirm)
            {
                _vm.DestinationRoot.Clear();
                UpdatePlanSummary();
                StatusLabel.Text = "All moves cleared";
            }
        });
    }

    private void UpdatePlanSummary()
    {
        try
        {
            // Handle null plan gracefully
            var plan = _vm?.GetEditedPlan();
            var moves = plan?.Moves?.Count ?? 0;
            var folders = _vm?.DestinationRoot?.Count(f => f.IsFolder) ?? 0;
            
            if (MovesCountLabel != null)
                MovesCountLabel.Text = $"{moves} planned moves";
            
            if (FoldersCountLabel != null)
                FoldersCountLabel.Text = $"{folders} destination folders";
            
            if (StatusLabel != null)
            {
                if (moves > 0 || folders > 0)
                {
                    StatusLabel.Text = "Plan ready to save";
                    StatusLabel.TextColor = Colors.Green;
                }
                else
                {
                    StatusLabel.Text = "No changes planned";
                    StatusLabel.TextColor = Colors.Gray;
                }
            }
        }
        catch (Exception ex)
        {
            if (StatusLabel != null)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
                StatusLabel.TextColor = Colors.Red;
            }
        }
    }

    private async Task DisplayAlert(string title, string message, string cancel)
    {
        await Application.Current?.MainPage?.DisplayAlert(title, message, cancel)!;
    }
}
