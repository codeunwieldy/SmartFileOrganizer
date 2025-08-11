using SmartFileOrganizer.App.Pages;

namespace SmartFileOrganizer.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell())
        {
#if WINDOWS
            Title = "Smart File Organizer"
#endif
        };
        return window;
    }
}