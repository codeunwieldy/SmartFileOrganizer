using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using SmartFileOrganizer.App.Models;
using SmartFileOrganizer.App.Pages;
using SmartFileOrganizer.App.Services;
using System.Collections.ObjectModel;
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
    private readonly INavigationService _nav;
    private readonly IOverviewService _overview;
    private readonly IRuleEngine _rules;
    private readonly IRuleStore _ruleStore;

    private CancellationTokenSource? _cts;
    private FileNode? mapCache;

    public PlanPreferences Preferences { get; } = new();
    public ObservableCollection<string> ProgressLines { get; } = new();

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Ready";
    [ObservableProperty] public partial Plan? CurrentPlan { get; set; }
    [ObservableProperty] public partial Snapshot? LastSnapshot { get; set; }

    
    public IDrawable? PlanPreviewDrawable { get; private set; }

    public MainViewModel(
        IFileScanner scanner, IPlanService planner, IExecutorService executor,
        ISnapshotService snapshots, INavigationService nav, IIndexStore index,
        IFolderPicker folderPicker, IOverviewService overview, IDedupeService dedupe,
        IRuleEngine rules, IRuleStore ruleStore)
    {
        _scanner = scanner; _planner = planner; _executor = executor;
        _snapshots = snapshots; _nav = nav; _index = index;
        _folderPicker = folderPicker; _overview = overview; _dedupe = dedupe;
        _rules = rules; _ruleStore = ruleStore;

        LoadRoots();
        LoadPrefs();
        BuildPlanPreview(); 
    }

    private static string PrefsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SFO", "Config", "prefs.json");

    private static string RootsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SFO", "Config", "roots.json");

    public ObservableCollection<string> SelectedRoots { get; } = [];

   

    [RelayCommand]
    public async Task GeneratePlanAsync(string mode)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        ProgressLinesClear();
        AppendProgress("Generate plan started…");

        try
        {
            IsBusy = true;
            Status = "Scanning…";

            var roots = mode switch
            {
                "desktop" => new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
                "downloads" => new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") },
                _ => SelectedRoots.Any()
                        ? SelectedRoots.ToArray()
                        : new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }
            };

            AppendProgress($"Roots: {string.Join(", ", roots)}");

            var options = new ScanOptions
            {
                MaxDepth = 6,
                MaxItems = 40_000
            };
            options.Roots.AddRange(roots);

            var progress = new Progress<string>(s => { Status = s; AppendProgress(s); });
            var scan = await _scanner.ScanAsync(options, progress, _cts.Token);
            await _index.SaveAsync(scan, CancellationToken.None);

           
            Status = "Applying user rules…";
            AppendProgress("Applying user rules…");
            var ruleSet = await _ruleStore.LoadAsync(_cts.Token);
            var ruleEval = _rules.Evaluate(ruleSet, scan.RootTree);

            var mergedPlan = new Plan { ScopeDescription = mode };
            mergedPlan.Moves.AddRange(ruleEval.Moves);

            
            Status = "Planning (AI)…";
            AppendProgress("Planning (AI)…");
            var aiPlan = await _planner.GeneratePlanAsync(scan.RootTree, mode, _cts.Token);

            aiPlan.Moves = [.. aiPlan.Moves.Where(m => !ruleEval.ClaimedSources.Contains(m.Source))];

            mergedPlan.Moves.AddRange(aiPlan.Moves);
            mergedPlan.DeleteEmptyDirectories.AddRange(aiPlan.DeleteEmptyDirectories);

          
            Status = "Checking duplicates…";
            AppendProgress("Checking duplicates…");
            var dedupeGroups = await _dedupe.FindDuplicatesAsync(
                roots,
                new Progress<string>(s => { Status = s; AppendProgress(s); }),
                _cts.Token
            );

            if (dedupeGroups.Count > 0)
            {
                AppendProgress($"Duplicates: {dedupeGroups.Count} groups found.");
                var dupPage = new DuplicatesPage(dedupeGroups)
                {
                    Result = new TaskCompletionSource<DuplicatesPage.ResultPayload>()
                };

                await _nav.PushAsync(dupPage);
                var result = await dupPage.Result.Task;

                if (result.Apply)
                {
                    foreach (var kv in result.MoveMap)
                        mergedPlan.Moves.Add(new MoveOp(kv.Key, kv.Value));

                    foreach (var hl in result.Hardlinks)
                        mergedPlan.Hardlinks.Add(new HardlinkOp(hl.LinkPath, hl.Target));

                    Status = $"Duplicates resolved: {result.MoveMap.Count} moves, {result.Hardlinks.Count} links";
                    AppendProgress(Status);
                }
                else
                {
                    AppendProgress("Duplicates: skipped by user.");
                }
            }
            else
            {
                AppendProgress("Duplicates: none.");
            }

            CurrentPlan = mergedPlan;
            mapCache = scan.RootTree;
            BuildPlanPreview(); 

            Status = $"Plan ready: {CurrentPlan.Moves.Count} moves" + (scan.Truncated ? " (truncated)" : "");
            AppendProgress(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
            AppendProgress("Cancelled.");
        }
        catch (Exception ex)
        {
            Status = "Failed";
            AppendProgress(" Error: " + ex.Message);
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

        AppendProgress(" Execute plan…");

        var conflicts = await _executor.DryRunAsync(CurrentPlan, CancellationToken.None);
        IEnumerable<ConflictResolution> resolutions = [];

        if (conflicts.Count > 0)
        {
            AppendProgress($"Conflicts: {conflicts.Count}");
            var page = new ConflictResolverPage(conflicts)
            {
                ResolveTask = new TaskCompletionSource<List<ConflictResolution>>()
            };
            await _nav.PushAsync(page);
            resolutions = await page.ResolveTask.Task;
        }

        IsBusy = true;
        var progress = new Progress<string>(s => { Status = s; AppendProgress(s); });
        LastSnapshot = await _executor.ExecuteAsync(CurrentPlan, resolutions, progress, CancellationToken.None);
        await _snapshots.SaveAsync(LastSnapshot);

        Status = "Completed";
        AppendProgress(" Completed.");
        IsBusy = false;

        
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
        await _nav.PushAsync(
            new AdvancedPlannerPage(
                new AdvancedPlannerViewModel(
                    CurrentPlan,
                    mapCache ?? new FileNode { Name = "Root", Path = root, IsDirectory = true },
                    root)));
    }

    [RelayCommand]
    public async Task RevertAsync()
    {
        if (LastSnapshot is null) return;
        AppendProgress(" Revert…");
        IsBusy = true;
        var progress = new Progress<string>(s => { Status = s; AppendProgress(s); });
        await _executor.RevertAsync(LastSnapshot, progress, CancellationToken.None);
        Status = "Reverted";
        AppendProgress(" Reverted.");
        IsBusy = false;
    }

    [RelayCommand]
    public async Task AddFolderAsync()
    {
        AppendProgress("Pick folder…");
        var path = await _folderPicker.PickFolderAsync(CancellationToken.None);

        if (!string.IsNullOrWhiteSpace(path) &&
            !SelectedRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            SelectedRoots.Add(path);
            SaveRoots();
            Status = $"Added: {path}";
            AppendProgress(Status);
        }
        else
        {
            AppendProgress("No folder selected or already added.");
        }
    }

    [RelayCommand]
    public void RemoveRoot(string path)
    {
        if (SelectedRoots.Remove(path))
        {
            SaveRoots();
            Status = $"Removed: {path}";
            AppendProgress(Status);
        }
    }

    [RelayCommand]
    public void CancelOps() => _cts?.Cancel();

   

    private void AppendProgress(string line)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressLines.Add(line);
                const int max = 200;
                if (ProgressLines.Count > max)
                    ProgressLines.RemoveAt(0);
            });
        }
        catch {  }
    }

    private void ProgressLinesClear()
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(ProgressLines.Clear);
        }
        catch { }
    }

    private void LoadRoots()
    {
        try
        {
            if (File.Exists(RootsPath))
            {
                var arr = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RootsPath)) ?? [];
                foreach (var r in arr) SelectedRoots.Add(r);
            }
            if (SelectedRoots.Count == 0)
                SelectedRoots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        catch {  }
    }

    private void SaveRoots()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RootsPath)!);
            File.WriteAllText(RootsPath, JsonSerializer.Serialize(SelectedRoots.ToList()));
        }
        catch {  }
    }

    private void LoadPrefs()
    {
        try
        {
            if (File.Exists(PrefsPath))
            {
                var json = File.ReadAllText(PrefsPath);
                var p = JsonSerializer.Deserialize<PlanPreferences>(json);
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
    }

    private void SavePrefs()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!);
            var json = JsonSerializer.Serialize(Preferences);
            File.WriteAllText(PrefsPath, json);
        }
        catch { }
    }

    partial void OnCurrentPlanChanged(Plan? value)
    {
        BuildPlanPreview();
        OnPropertyChanged(nameof(PlanPreviewDrawable));
    }

    private void BuildPlanPreview()
    {
        if (CurrentPlan?.Moves is null || CurrentPlan.Moves.Count == 0)
        {
            PlanPreviewDrawable = new EmptyPreviewDrawable();
            return;
        }

        
        var top = CurrentPlan.Moves
            .Select(m => Path.GetDirectoryName(m.Destination) ?? "")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Bar(g.Key, g.Count()))
            .OrderByDescending(b => b.Value)
            .Take(20)
            .ToList();

        PlanPreviewDrawable = new BarsDrawable(top);
    }

    
    private readonly record struct Bar(string Label, int Value);

    private sealed class EmptyPreviewDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirty)
        {
            canvas.SaveState();
            canvas.FontSize = 12;
            canvas.FontColor = Colors.Gray;
            canvas.DrawString("(Updates after you Generate Plan.)",
                dirty, HorizontalAlignment.Left, VerticalAlignment.Bottom);
            canvas.RestoreState();
        }
    }

    private sealed class BarsDrawable : IDrawable
    {
        private readonly IReadOnlyList<Bar> _bars;
        public BarsDrawable(IReadOnlyList<Bar> bars) => _bars = bars;

        public void Draw(ICanvas canvas, RectF rect)
        {
            canvas.Antialias = true;
            canvas.SaveState();

            var left = rect.Left + 12;
            var top = rect.Top + 12;
            var right = rect.Right - 12;
            var bottom = rect.Bottom - 12;

            if (_bars.Count == 0)
            {
                canvas.FontSize = 12;
                canvas.FontColor = Colors.Gray;
                canvas.DrawString("(No destinations yet.)",
                    rect, HorizontalAlignment.Left, VerticalAlignment.Center);
                canvas.RestoreState();
                return;
            }

            float rowH = MathF.Min(26f, (bottom - top) / _bars.Count);
            int max = Math.Max(1, _bars.Max(b => b.Value));

            int i = 0;
            foreach (var b in _bars)
            {
                var y = top + i * rowH;
                var pct = (float)b.Value / max;
                var barW = (right - left) * pct;

                var barRect = new RectF(left, y + 4, barW, rowH - 8);
                canvas.FillColor = Color.FromRgba(107, 87, 182, 255); 
                canvas.FillRectangle(barRect);

                canvas.FontSize = 12;
                canvas.FontColor = Colors.White;
                var label = $"{Path.GetFileName(b.Label)}  ({b.Value})";
                canvas.DrawString(label, new RectF(left + 6, y, right - left - 12, rowH),
                    HorizontalAlignment.Left, VerticalAlignment.Center);

                i++;
            }

            canvas.RestoreState();
        }
    }
}