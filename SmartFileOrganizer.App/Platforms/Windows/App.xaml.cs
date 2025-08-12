namespace SmartFileOrganizer.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            // Ensure Windows App SDK is properly initialized
            base.OnLaunched(args);
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80040154))
        {
            // Handle "Class not registered" error gracefully
            System.Diagnostics.Debug.WriteLine($"Windows App SDK initialization failed: {ex.Message}");
            
            // Try to continue anyway - sometimes the app can still work
            try
            {
                base.OnLaunched(args);
            }
            catch
            {
                // If it still fails, exit gracefully
                System.Environment.Exit(ex.HResult);
            }
        }
    }
}