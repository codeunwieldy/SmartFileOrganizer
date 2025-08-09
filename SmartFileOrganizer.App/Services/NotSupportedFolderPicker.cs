namespace SmartFileOrganizer.App.Services;
public sealed class NotSupportedFolderPicker : IFolderPicker
{
    public Task<string?> PickFolderAsync(CancellationToken ct)
        => Task.FromResult<string?>(null);
}