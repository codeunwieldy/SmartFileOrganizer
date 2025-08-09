using SmartFileOrganizer.App.Pages;

namespace SmartFileOrganizer.App;

public partial class App : Application
{
    public App(MainPage page)
    {
        InitializeComponent();
        MainPage = new NavigationPage(page);
        UserAppTheme = AppTheme.Light; // or AppTheme.Dark / Unspecified
    }
}