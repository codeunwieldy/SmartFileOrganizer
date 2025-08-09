using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Services;

public interface IIndexStore
{
    Task SaveAsync(ScanResult result, CancellationToken ct);

    Task<ScanResult?> LoadLastAsync(CancellationToken ct);
}