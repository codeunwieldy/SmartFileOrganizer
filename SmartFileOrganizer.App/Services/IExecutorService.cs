using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Services
{
    public interface IExecutorService
    {
        public record Conflict(string Destination, string Reason, string? ExistingPath);
        public enum ConflictChoice { Skip, Rename, Overwrite }
        public record ConflictResolution(string Destination, ConflictChoice Choice, string? NewDestinationIfRename);
        Task<Snapshot> ExecuteAsync(Plan plan, IEnumerable<ConflictResolution> resolutions, IProgress<string>? progress, CancellationToken ct);
        Task RevertAsync(Snapshot snapshot, IProgress<string>? progress, CancellationToken ct);
        Task<IReadOnlyList<Conflict>> DryRunAsync(Plan plan, CancellationToken ct);
    }
}