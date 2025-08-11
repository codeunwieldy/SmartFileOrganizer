using SmartFileOrganizer.App.Pages;

namespace SmartFileOrganizer.App;

public partial class App : Application
{
    private readonly MainPage _mainPage;

    public App(MainPage mainPage)
    {
        InitializeComponent();
        _mainPage = mainPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new NavigationPage(_mainPage))
        {
#if WINDOWS
            Title = "Smart File Organizer"
#endif
        };
        return window;
    }
}