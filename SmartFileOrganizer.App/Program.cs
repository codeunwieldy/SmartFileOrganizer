#if WINDOWS
using System;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace SmartFileOrganizer.App
{
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Create the WinUI Application (Maui host). Do NOT call OnLaunched yourself.
            Microsoft.UI.Xaml.Application.Start(_ => new WinHost());
        }
    }

    // Windows host that bridges to your MauiProgram
    internal sealed class WinHost : Microsoft.Maui.MauiWinUIApplication
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        // You generally don't need this; the base class wires everything.
        // If you MUST hook launch, override the correct signature:
        // protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        // {
        //     base.OnLaunched(args);
        //     // custom logic here...
        // }
    }
}
#endif


