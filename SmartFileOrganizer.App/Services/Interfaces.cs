using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Services;

public interface IFileScanner
{
    Task<ScanResult> ScanAsync(ScanOptions options, IProgress<string>? progress, CancellationToken ct);
}

public interface IPlanService
{
    Task<Plan> GeneratePlanAsync(FileNode map, string mode, CancellationToken ct);
    Task CommitAsync(Plan plan, CancellationToken ct);
}

public interface ISnapshotService
{
    Task SaveAsync(Snapshot s);
    Task<Snapshot?> GetAsync(string id);
}

public interface INavigationService
{
    Task PushAsync(Page page);
    Task<Page?> PopAsync();
}
