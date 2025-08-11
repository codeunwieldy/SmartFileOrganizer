using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Services;

public interface IDedupeService
{
    Task<List<DuplicateGroup>> FindDuplicatesAsync(IEnumerable<string> roots, IProgress<ScanProgress>? progress, CancellationToken ct);
}