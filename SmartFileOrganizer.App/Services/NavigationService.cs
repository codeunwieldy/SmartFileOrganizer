namespace SmartFileOrganizer.App.Services
{
    public sealed class NavigationService : INavigationService
    {
        private static INavigation? GetNavigation()
        {
            // .NET 9: use Window.Page instead of Application.MainPage
            var window = Application.Current?.Windows.FirstOrDefault();
            var root = window?.Page;

            // Prefer Shell’s navigation if app uses Shell
            if (root is Shell shell)
                return shell.Navigation;

            // Otherwise use the NavigationPage stack, or the page’s own INavigation
            if (root is NavigationPage navPage)
                return navPage.Navigation;

            return root?.Navigation;
        }

        public Task PushAsync(Page page)
        {
            var nav = GetNavigation();
            if (nav is null)
                return Task.CompletedTask;

            // Ensure we run on the UI thread
            return MainThread.InvokeOnMainThreadAsync(() => nav.PushAsync(page));
        }

        public Task<Page?> PopAsync()
        {
            var nav = GetNavigation();
            if (nav is null)
                return Task.FromResult<Page?>(null);

            // Ensure we run on the UI thread
            return MainThread.InvokeOnMainThreadAsync(() => nav.PopAsync());
        }
    }
}