namespace SmartFileOrganizer.App.Services;

public class NavigationService : INavigationService
{
    public Task PushAsync(Page page) => Application.Current!.MainPage!.Navigation.PushAsync(page);

    public Task<Page?> PopAsync() => Application.Current!.MainPage!.Navigation.PopAsync();
}