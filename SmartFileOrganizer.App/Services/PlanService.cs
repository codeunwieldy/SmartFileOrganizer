using SmartFileOrganizer.App.Models;
using System.Net.Http.Json;

namespace SmartFileOrganizer.App.Services;

public class PlanService : IPlanService
{
    private readonly HttpClient _http;
    private readonly Func<PlanPreferences>? _prefsProvider;

    public PlanService(HttpClient http) => _http = http;

    public PlanService(HttpClient http, Func<PlanPreferences> prefsProvider)
    {
        _http = http; _prefsProvider = prefsProvider;
    }

    // ----- Request/Response DTOs (local to service) -----
    private record PlanRequest(List<FileNodeDigest> roots, string scope, PrefsDto preferences);

    private record FileNodeDigest(
        string path,
        string name,
        bool isDir,
        long size,
        DateTime createdUtc,
        List<FileNodeDigest>? children
    );

    private record PrefsDto
    {
        public bool GroupByType { get; set; }
        public bool GroupByDate { get; set; }
        public bool GroupByProject { get; set; }
        public bool KeepFolderNames { get; set; }
        public bool FlattenSmallFolders { get; set; }
    }

    private record CleanPlanDto(
        string? PlanId,
        string Summary,
        List<MoveOpDto> Moves,
        List<string> DeleteEmpty,
        Dictionary<string, string>? RationaleByPath,
        List<HardlinkOpDto>? Hardlinks
    );

    private record MoveOpDto(string Source, string Destination);
    private record HardlinkOpDto(string LinkPath, string TargetExistingPath);

    // ----- Public API -----
    public async Task<Plan> GeneratePlanApiCallAsync(FileNode map, string mode, CancellationToken ct)
    {
        var digest = ToDigest(map);

        var prefs = _prefsProvider?.Invoke() ?? new PlanPreferences();
        var req = new PlanRequest(
            new List<FileNodeDigest> { digest },
            mode,
            new PrefsDto
            {
                GroupByType = prefs.GroupByType,
                GroupByDate = prefs.GroupByDate,
                GroupByProject = prefs.GroupByProject,
                KeepFolderNames = prefs.KeepFolderNames,
                FlattenSmallFolders = prefs.FlattenSmallFolders
            }
        );

        var resp = await _http.PostAsJsonAsync("/plan", req, ct);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<CleanPlanDto>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Empty plan response from /plan.");

        // Map DTO -> domain Plan
        return new Plan
        {
            Id = dto.PlanId ?? Guid.NewGuid().ToString("N"),
            ScopeDescription = string.IsNullOrWhiteSpace(dto.Summary) ? mode : dto.Summary,
            Moves = dto.Moves?.Select(m => new MoveOp(m.Source, m.Destination)).ToList() ?? new(),
            DeleteEmptyDirectories = dto.DeleteEmpty ?? new(),
            Hardlinks = dto.Hardlinks?.Select(h => new HardlinkOp(h.LinkPath, h.TargetExistingPath)).ToList() ?? new()
            // If you later surface rationale, add it to Plan and map dto.RationaleByPath here.
        };
    }

    public async Task CommitAsync(Plan plan, CancellationToken ct)
    {
        var dto = new
        {
            planId = plan.Id,
            summary = plan.ScopeDescription,
            moves = plan.Moves.Select(m => new { source = m.Source, destination = m.Destination }).ToList(),
            deleteEmpty = plan.DeleteEmptyDirectories,
            rationaleByPath = new Dictionary<string, string>()
            // Add hardlinks if your /commit endpoint supports them:
            // hardlinks = plan.Hardlinks.Select(h => new { linkPath = h.LinkPath, targetExistingPath = h.TargetExistingPath }).ToList()
        };

        var resp = await _http.PostAsJsonAsync("/commit", dto, ct);
        resp.EnsureSuccessStatusCode();
    }

    // ----- Helpers -----
    private static FileNodeDigest ToDigest(FileNode n) => new(
        path: n.Path,
        name: n.Name,
        isDir: n.Children.Any(),
        size: n.SizeBytes,
        createdUtc: n.CreatedUtc,
        children: n.Children.Select(ToDigest).ToList()
    );
}