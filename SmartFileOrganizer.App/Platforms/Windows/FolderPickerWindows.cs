#if WINDOWS
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

using MauiApp = Microsoft.Maui.Controls.Application;                 // disambiguate
using MauiWindow = Microsoft.Maui.Controls.Window;                   // disambiguate
using UiWindow = Microsoft.UI.Xaml.Window;                           // WinUI window

using SmartFileOrganizer.App.Services;

namespace SmartFileOrganizer.App.Platforms.Windows;

public sealed class FolderPickerWindows : IFolderPicker
{
    public async Task<string?> PickFolderAsync(CancellationToken ct)
    {
        // Get the MAUI Window -> WinUI Window -> HWND
        var mauiWin = MauiApp.Current?.Windows.FirstOrDefault();
        var winuiWin = mauiWin?.Handler?.PlatformView as UiWindow;
        if (winuiWin is null) return null;

        var hwnd = WindowNative.GetWindowHandle(winuiWin);

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        // IMPORTANT: associate picker with our HWND in packaged desktop apps
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
#endif




