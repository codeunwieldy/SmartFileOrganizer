#if WINDOWS
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SmartFileOrganizer.App.Services;
public sealed class FolderPickerWindows : IFolderPicker
{
    public async Task<string?> PickFolderAsync(CancellationToken ct)
    {
        var hwnd = WindowNative.GetWindowHandle(Application.Current!.Windows[0].Handler.PlatformView as Microsoft.UI.Xaml.Window);
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
#endif
