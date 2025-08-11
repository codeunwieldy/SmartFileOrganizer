#if MACCATALYST
using System.Threading;
using System.Threading.Tasks;

namespace SmartFileOrganizer.App.Services;

public sealed class FolderPickerMac : IFolderPicker
{
    public Task<string?> PickFolderAsync(CancellationToken ct)
    {
        // Keep it simple for now—wire up a real NSOpenPanel later if you like
        return Task.FromResult<string?>(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }
}
#endif


