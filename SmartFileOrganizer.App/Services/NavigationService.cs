namespace SmartFileOrganizer.App.Services
{
    public sealed class NavigationService : INavigationService
    {
        public Task PushAsync(Page page)
        {
            return MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(page.GetType().Name));
        }

        public Task<Page?> PopAsync()
        {
            return MainThread.InvokeOnMainThreadAsync(() =>
            {
                Shell.Current.GoToAsync("..");
                return Task.FromResult<Page?>(null);
            });
        }
    }
}