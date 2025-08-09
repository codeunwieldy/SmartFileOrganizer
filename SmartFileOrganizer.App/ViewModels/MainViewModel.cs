using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartFileOrganizer.App.Models;
using SmartFileOrganizer.App.Pages;
using SmartFileOrganizer.App.Services;
using System.Collections.ObjectModel;
using System.Data;
using System.Text.Json;
using static SmartFileOrganizer.App.Services.IExecutorService;

namespace SmartFileOrganizer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileScanner _scanner;
    private readonly IPlanService _planner;
    private readonly IFolderPicker _folderPicker;
    private readonly IIndexStore _index;
    private readonly IExecutorService _executor;
    private readonly ISnapshotService _snapshots;
    private readonly IDedupeService _dedupe;
    private FileNode? mapCache;
    private readonly INavigationService _nav;
    private readonly IOverviewService _overview;
    private readonly IRuleEngine _rules;
    private readonly IRuleStore _ruleStore;

    private CancellationTokenSource? _cts;
    public PlanPreferences Preferences { get; } = new();


    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string status = "Ready";
    [ObservableProperty] private Plan? currentPlan;
    [ObservableProperty] private Snapshot? lastSnapshot;

    public MainViewModel(IFileScanner scanner, IPlanService planner, IExecutorService executor,
                     ISnapshotService snapshots, INavigationService nav, IIndexStore index,
                     IFolderPicker folderPicker, IOverviewService overview, IDedupeService dedupe, IRuleEngine rules, IRuleStore ruleStore)
    {
        _scanner = scanner; _planner = planner; _executor = executor;
        _snapshots = snapshots; _nav = nav; _index = index; _folderPicker = folderPicker;
        _overview = overview;
        _dedupe = dedupe;
        _rules = rules; _ruleStore = ruleStore;

        LoadRoots();
        LoadPrefs();
    }
    private static string PrefsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SFO", "Config", "prefs.json");

    [RelayCommand]
    public async Task GeneratePlanAsync(string mode)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            Status = "Scanning...";

            var roots = mode switch
            {
                "desktop" => new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
                "downloads" => new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") },
                _ => SelectedRoots.Any()
                                ? SelectedRoots.ToArray()
                                : new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }
            };

            var options = new ScanOptions
            {
                MaxDepth = 6,
                MaxItems = 40_000
            };
            options.Roots.AddRange(roots);

            var progress = new Progress<string>(s => Status = s);
            var scan = await _scanner.ScanAsync(options, progress, _cts.Token);
            await _index.SaveAsync(scan, CancellationToken.None);

            // ---- RULES FIRST (User > AI) ----
            Status = "Applying user rules...";
            var ruleSet = await _ruleStore.LoadAsync(_cts.Token);
            var ruleEval = _rules.Evaluate(ruleSet, scan.RootTree);

            // Seed plan with rule-based moves
            var mergedPlan = new Plan { ScopeDescription = mode };
            mergedPlan.Moves.AddRange(ruleEval.Moves);

            // ---- AI PLAN (filter out files already claimed by rules) ----
            Status = "Planning (AI)...";
            var aiPlan = await _planner.GeneratePlanAsync(scan.RootTree, mode, _cts.Token);

            // Keep only AI moves whose Source wasn't claimed by rules
            aiPlan.Moves = aiPlan.Moves
                .Where(m => !ruleEval.ClaimedSources.Contains(m.Source))
                .ToList();

            // Merge AI results
            mergedPlan.Moves.AddRange(aiPlan.Moves);
            mergedPlan.DeleteEmptyDirectories.AddRange(aiPlan.DeleteEmptyDirectories);

            // ---- DUPLICATES (Move / Hardlink / Archive) ----
            Status = "Checking duplicates...";
            var dedupeGroups = await _dedupe.FindDuplicatesAsync(
                roots,
                new Progress<string>(s => Status = s),
                _cts.Token
            );

            if (dedupeGroups.Count > 0)
            {
                var dupPage = new DuplicatesPage(dedupeGroups)
                {
                    Result = new TaskCompletionSource<DuplicatesPage.ResultPayload>()
                };

                await _nav.PushAsync(dupPage);
                var result = await dupPage.Result.Task;

                if (result.Apply)
                {
                    // Archive or MoveNextToKept -> add MoveOps
                    foreach (var kv in result.MoveMap)
                        mergedPlan.Moves.Add(new MoveOp(kv.Key, kv.Value));

                    // Hardlink policy -> add HardlinkOps
                    foreach (var hl in result.Hardlinks)
                        mergedPlan.Hardlinks.Add(new HardlinkOp(hl.LinkPath, hl.Target));

                    Status = $"Duplicates resolved: {result.MoveMap.Count} moves, {result.Hardlinks.Count} links";
                }
            }

            CurrentPlan = mergedPlan;
            mapCache = scan.RootTree;

            Status = $"Plan ready: {CurrentPlan.Moves.Count} moves" + (scan.Truncated ? " (truncated)" : "");
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
        }
        finally
        {
            IsBusy = false;
        }
    }


    [RelayCommand]
    public async Task ExecuteAsync()
    {
        if (CurrentPlan is null) return;

        // Dry-run
        var conflicts = await _executor.DryRunAsync(CurrentPlan, CancellationToken.None);
        IEnumerable<ConflictResolution> resolutions = Array.Empty<ConflictResolution>();

        if (conflicts.Count > 0)
        {
            var page = new ConflictResolverPage(conflicts);
            page.ResolveTask = new TaskCompletionSource<List<ConflictResolution>>();
            await _nav.PushAsync(page);
            resolutions = await page.ResolveTask.Task;
        }

        IsBusy = true;
        var progress = new Progress<string>(s => Status = s);
        LastSnapshot = await _executor.ExecuteAsync(CurrentPlan, resolutions, progress, CancellationToken.None);
        await _snapshots.SaveAsync(LastSnapshot);
        Status = "Completed";
        IsBusy = false;
        // Build overview data and navigate
        var overview = _overview.Build(CurrentPlan!);
        var firstDest = CurrentPlan!.Moves.Select(m => Path.GetDirectoryName(m.Destination))
                                          .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        await _nav.PushAsync(new OverviewPage(overview, firstDest));

    }

    [RelayCommand]
    public async Task OpenAdvancedAsync()
    {
        if (CurrentPlan is null) return;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await _nav.PushAsync(new AdvancedPlannerPage(new AdvancedPlannerViewModel(CurrentPlan, mapCache ?? new FileNode { Name = "Root", Path = root }, root)));
    }

    [RelayCommand]
    public async Task RevertAsync()
    {
        if (LastSnapshot is null) return;
        IsBusy = true;
        var progress = new Progress<string>(s => Status = s);
        await _executor.RevertAsync(LastSnapshot, progress, CancellationToken.None);
        Status = "Reverted";
        IsBusy = false;
    }

    [RelayCommand]
    public async Task AddFolderAsync()
    {
        var path = await _folderPicker.PickFolderAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(path) && !SelectedRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            SelectedRoots.Add(path);
            SaveRoots();
            Status = $"Added: {path}";
        }
    }

    [RelayCommand]
    public void RemoveRoot(string path)
    {
        if (SelectedRoots.Remove(path))
        {
            SaveRoots();
            Status = $"Removed: {path}";
        }
    }

    [RelayCommand]
    public void CancelOps()
    {
        _cts?.Cancel();
    }
    public ObservableCollection<string> SelectedRoots { get; } = new();

    private void LoadRoots()
    {
        try
        {
            var file = GetRootsPath();
            if (File.Exists(file))
            {
                var arr = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) ?? new();
                foreach (var r in arr) SelectedRoots.Add(r);
            }
            if (SelectedRoots.Count == 0)
                SelectedRoots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        catch { /* ignore */ }
    }

    private void SaveRoots()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GetRootsPath())!);
            File.WriteAllText(GetRootsPath(), JsonSerializer.Serialize(SelectedRoots.ToList()));
        }
        catch { /* ignore */ }
    }

    private static string GetRootsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SFO", "Config", "roots.json");

    private void LoadPrefs()
    {
        try
        {
            if (File.Exists(PrefsPath))
            {
                var json = File.ReadAllText(PrefsPath);
                var p = System.Text.Json.JsonSerializer.Deserialize<PlanPreferences>(json);
                if (p is not null)
                {
                    Preferences.GroupByType = p.GroupByType;
                    Preferences.GroupByDate = p.GroupByDate;
                    Preferences.GroupByProject = p.GroupByProject;
                    Preferences.KeepFolderNames = p.KeepFolderNames;
                    Preferences.FlattenSmallFolders = p.FlattenSmallFolders;
                }
            }
        }
        catch { }

        // call in ctor end:
        // LoadRoots();
        // LoadPrefs();
    }

    private void SavePrefs()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!);
            var json = System.Text.Json.JsonSerializer.Serialize(Preferences);
            File.WriteAllText(PrefsPath, json);
        }
        catch { }
    }
}