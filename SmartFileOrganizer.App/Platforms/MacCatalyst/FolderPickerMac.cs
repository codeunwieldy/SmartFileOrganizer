#if MACCATALYST
using UIKit;
using UniformTypeIdentifiers;
using Foundation; // Ensure this is resolved by adding the appropriate NuGet package reference.

namespace SmartFileOrganizer.App.Services;
public sealed class FolderPickerMac : IFolderPicker
{
    public Task<string?> PickFolderAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>();
        var picker = new UIDocumentPickerViewController(new string[] { UTTypes.Folder.Identifier }, UIDocumentPickerMode.Open);
        picker.DirectoryURL = NSUrl.FromFilename(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        picker.DidPickDocumentAtUrls += (_, e) => tcs.TrySetResult(e.Urls?.FirstOrDefault()?.Path);
        picker.WasCancelled += (_, __) => tcs.TrySetResult(null);

        var vc = UIApplication.SharedApplication.KeyWindow.RootViewController!;
        while (vc.PresentedViewController != null) vc = vc.PresentedViewController;
        vc.PresentViewController(picker, true, null);
        return tcs.Task;
    }
}
#endif

