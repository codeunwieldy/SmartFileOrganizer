using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Services;

public enum JobStage
{
    Idle,
    Estimating,
    Scanning,
    Planning,
    Applying,
    Cancelling,
    Completed,
    Error
}

public record ScanProgress
{
    public JobStage Stage { get; init; }
    public double StageProgress { get; init; }   // 0..1 within current stage
    public double OverallProgress { get; init; }  // 0..1 after weights
    public long FilesProcessed { get; init; }
    public long FilesTotal { get; init; }
    public long BytesProcessed { get; init; }
    public long BytesTotal { get; init; }
    public int ActionsDone { get; init; }
    public int ActionsTotal { get; init; }
    public double Throughput { get; init; }   // bytes/sec or actions/sec
    public TimeSpan? Eta { get; init; }
    public string? Message { get; init; }  // optional, current file/path
    public int Errors { get; init; }
    public int Skipped { get; init; }
}

public interface IFileScanner
{
    Task<ScanResult> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken ct);
}
public interface IPlanService
{
    Task<Plan> GeneratePlanApiCallAsync(FileNode map, string mode, CancellationToken ct);

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