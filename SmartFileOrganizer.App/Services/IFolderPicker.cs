namespace SmartFileOrganizer.App.Services;

public interface IFolderPicker
{
    Task<string?> PickFolderAsync(CancellationToken ct);
}