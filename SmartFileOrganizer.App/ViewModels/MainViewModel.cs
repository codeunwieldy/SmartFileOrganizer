using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
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

    // live progress console
    public ObservableCollection<string> ProgressLines { get; } = new();

    // SCAN TREE shown by CurrentTreeView
    public ObservableCollection<CurrentTreeNode> CurrentRoot { get; } = new();

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Ready";
    [ObservableProperty] public partial Plan? CurrentPlan { get; set; }
    [ObservableProperty] public partial Snapshot? LastSnapshot { get; set; }

    [ObservableProperty] public partial string CurrentBreadcrumb { get; set; } = "";

    // Progress bar properties
    [ObservableProperty] public partial bool IsProgressVisible { get; set; }
    [ObservableProperty] public partial bool IsIndeterminate { get; set; }
    [ObservableProperty] public partial double ProgressValue { get; set; } // Overall progress 0-1
    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";
    [ObservableProperty] public partial string SubStatusText { get; set; } = "";
    [ObservableProperty] public partial JobStage Stage { get; set; } = JobStage.Idle;
    [ObservableProperty] public partial bool CanPause { get; set; } = false;
    [ObservableProperty] public partial bool IsPaused { get; set; } = false;
    [ObservableProperty] public partial bool CanCancel { get; set; } = false;

    IDispatcherTimer? _barTimer;

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

        IsProgressVisible = true;
        CanCancel = true;
        CanPause = true; // Enable pause when operation starts

        try
        {
            var roots = mode switch
            {
                "desktop" => new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
                "downloads" => new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") },
                _ => SelectedRoots.Any()
                        ? SelectedRoots.ToArray()
                        : new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }
            };

            AppendProgress($"Roots: {string.Join(", ", roots)}");

            var options = new ScanOptions { MaxDepth = 6, MaxItems = 40_000 };
            options.Roots.AddRange(roots);

            // Scan stage
            var scanProgress = new Progress<ScanProgress>(ReportProgress);
            var scan = await _scanner.ScanAsync(options, scanProgress, _cts.Token);
            await _index.SaveAsync(scan, CancellationToken.None);

            // Scan summary
            AppendProgress($"Scan done: {scan.TotalFiles:n0} files in {scan.TotalDirs:n0} folders{(scan.Truncated ? " (truncated)" : "")}.");

            // ==== show scan immediately in the tree ====
            BuildCurrentTreeFromScan(scan.RootTree);

            // ---- RULES FIRST (User > AI) ----
            AppendProgress("Applying user rules…");
            var ruleSet = await _ruleStore.LoadAsync(_cts.Token);
            var ruleEval = _rules.Evaluate(ruleSet, scan.RootTree);

            var mergedPlan = new Plan { ScopeDescription = mode };
            mergedPlan.Moves.AddRange(ruleEval.Moves);

            // ---- AI PLAN ----
            AppendProgress("Planning (AI)…");
            var aiPlan = await _planner.GeneratePlanApiCallAsync(scan.RootTree, mode, _cts.Token);

            aiPlan.Moves = [.. aiPlan.Moves.Where(m => !ruleEval.ClaimedSources.Contains(m.Source))];
            mergedPlan.Moves.AddRange(aiPlan.Moves);
            mergedPlan.DeleteEmptyDirectories.AddRange(aiPlan.DeleteEmptyDirectories);

            // ---- DUPLICATES ----
            AppendProgress("Checking duplicates…");
            var dedupeProgress = new Progress<ScanProgress>(ReportProgress);
            var dedupeGroups = await _dedupe.FindDuplicatesAsync(
                roots,
                dedupeProgress,
                _cts.Token);

            if (dedupeGroups.Count > 0)
            {
                AppendProgress($"Duplicates: {dedupeGroups.Count} groups found.");
                var dupPage = new DuplicatesPage(dedupeGroups)
                {
                    Result = new TaskCompletionSource<DuplicatesPage.ResultPayload>()
                };

                await Shell.Current.GoToAsync("//DuplicatesPage");
                var result = await dupPage.Result.Task;

                if (result.Apply)
                {
                    foreach (var kv in result.MoveMap)
                        mergedPlan.Moves.Add(new MoveOp(kv.Key, kv.Value));

                    foreach (var hl in result.Hardlinks)
                        mergedPlan.Hardlinks.Add(new HardlinkOp(hl.LinkPath, hl.Target));

                    StatusText = $"Duplicates resolved: {result.MoveMap.Count} moves, {result.Hardlinks.Count} links";
                    AppendProgress(StatusText);
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

            // Final completed state
            ReportProgress(new ScanProgress { Stage = JobStage.Completed, Message = "Done" });
            await Task.Delay(1000); // Show "Done" for 1 second
        }
        catch (OperationCanceledException)
        {
            ReportProgress(new ScanProgress { Stage = JobStage.Cancelling, Message = "Cancelling…" });
            await Task.Delay(2000); // Show cancelling for 2 seconds
        }
        catch (Exception ex)
        {
            ReportProgress(new ScanProgress { Stage = JobStage.Error, Message = $"Stopped with errors ({ex.Message}). See details." });
            AppendProgress("Error: " + ex.Message);
        }
        finally
        {
            IsProgressVisible = false;
            IsIndeterminate = false;
            CanCancel = false;
            CanPause = false;
            IsPaused = false;
            ProgressValue = 0;
            StatusText = "Ready";
            SubStatusText = "";
        }
    }

    [RelayCommand]
    public void SelectCurrent(string path)
    {
        CurrentBreadcrumb = path;
        AppendProgress($"Selected: {path}");
    }

    [RelayCommand]
    public async Task ExecuteAsync()
    {
        if (CurrentPlan is null) return;

        AppendProgress("Execute plan…");

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

        IsProgressVisible = true;
        CanCancel = true;
        CanPause = true; // Enable pause when operation starts

        var applyProgress = new Progress<ScanProgress>(ReportProgress); // Changed type here
        LastSnapshot = await _executor.ExecuteAsync(CurrentPlan, resolutions, applyProgress, _cts.Token);
        await _snapshots.SaveAsync(LastSnapshot);

        // Final completed state
        ReportProgress(new ScanProgress { Stage = JobStage.Completed, Message = "Completed." });
        await Task.Delay(1000);

        var overview = _overview.Build(CurrentPlan!);
        var firstDest = CurrentPlan!.Moves
            .Select(m => Path.GetDirectoryName(m.Destination))
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        await Shell.Current.GoToAsync("//OverviewPage");

        IsProgressVisible = false;
        IsIndeterminate = false;
        CanCancel = false;
        CanPause = false;
        IsPaused = false;
        ProgressValue = 0;
        StatusText = "Ready";
        SubStatusText = "";
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
            StatusText = $"Added: {path}";
            AppendProgress(StatusText);
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
            StatusText = $"Removed: {path}";
            AppendProgress(StatusText);
        }
    }

    [RelayCommand] public void CancelOps() => _cts?.Cancel();

    [RelayCommand]
    public void PauseOps()
    {
        IsPaused = true;
        CanPause = false;
        CanCancel = false; // Disable cancel while paused, or allow based on UX
        // No actual pause mechanism for _cts, just UI indication
        StatusText = $"Paused at {ProgressValue:P0}";
    }

    [RelayCommand]
    public void ResumeOps()
    {
        IsPaused = false;
        CanPause = true;
        CanCancel = true;
        // Re-report last progress to update status text
        // This assumes ReportProgress is idempotent or handles re-reporting gracefully
        ReportProgress(new ScanProgress
        {
            Stage = Stage,
            StageProgress = ProgressValue, // Use current ProgressValue as StageProgress for re-calculation
            OverallProgress = ProgressValue, // Use current ProgressValue as OverallProgress for re-calculation
            Message = GetStatusText(new ScanProgress { Stage = Stage, StageProgress = ProgressValue }), // Re-evaluate message
            // Other properties might be stale, but for UI update, this is sufficient
        });
    }

    // --- Progress Reporting Helper ---
    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private void ReportProgress(ScanProgress progress)
    {
        // Throttle UI updates to ~10 fps
        if ((DateTime.Now - _lastProgressUpdate).TotalMilliseconds < 100 && progress.OverallProgress < 1.0)
        {
            return;
        }
        _lastProgressUpdate = DateTime.Now;

        MainThread.BeginInvokeOnMainThread(async () => // Use async for Task.Delay
        {
            IsProgressVisible = true;
            IsIndeterminate = progress.Stage == JobStage.Estimating || progress.Stage == JobStage.Cancelling;

            // Calculate OverallProgress based on stage weights
            double overallProgress = progress.Stage switch
            {
                JobStage.Estimating => 0.05 * progress.StageProgress,
                JobStage.Scanning => 0.05 + (0.65 * progress.StageProgress),
                JobStage.Planning => 0.05 + 0.65 + (0.20 * progress.StageProgress),
                JobStage.Applying => 0.05 + 0.65 + 0.20 + (0.10 * progress.StageProgress),
                JobStage.Completed => 1.0,
                _ => 0.0 // Default for Idle, Cancelling, Error
            };

            ProgressValue = overallProgress;
            Stage = progress.Stage;

            // Handle "Done" and "No files to process" messages
            if (progress.Stage == JobStage.Completed)
            {
                StatusText = progress.Message ?? GetStatusText(progress);
                SubStatusText = GetSubStatusText(progress);
                await Task.Delay(1000); // Show "Done" for 1 second
                IsProgressVisible = false;
            }
            else if (progress.Stage == JobStage.Error)
            {
                StatusText = progress.Message ?? GetStatusText(progress);
                SubStatusText = GetSubStatusText(progress);
                // Keep visible to show error message
            }
            else if (IsPaused)
            {
                StatusText = $"Paused at {ProgressValue:P0}";
            }
            else
            {
                StatusText = progress.Message ?? GetStatusText(progress);
                SubStatusText = GetSubStatusText(progress);
            }
        });
    }

    private string GetStatusText(ScanProgress progress)
    {
        return progress.Stage switch
        {
            JobStage.Estimating => "Estimating files…",
            JobStage.Scanning => $"Scanning {progress.FilesProcessed}/{progress.FilesTotal} files ({progress.StageProgress:P0})",
            JobStage.Planning => $"Building plan… ({progress.StageProgress:P0})",
            JobStage.Applying => $"Applying changes… ({progress.StageProgress:P0})",
            JobStage.Cancelling => "Cancelling…",
            JobStage.Completed => "Done",
            JobStage.Error => $"Stopped with errors ({progress.Errors}). See details.",
            _ => "Ready"
        };
    }

    private string GetSubStatusText(ScanProgress progress)
    {
        var parts = new List<string>();
        if (progress.Throughput > 0) parts.Add($"Throughput {progress.Throughput:N0} items/s");
        if (progress.Skipped > 0) parts.Add($"{progress.Skipped} skipped");
        if (progress.Errors > 0) parts.Add($"{progress.Errors} errors");
        if (progress.Eta.HasValue) parts.Add($"ETA {progress.Eta.Value:mm\\:ss}");
        return string.Join(" · ", parts);
    }


    // --- helpers ---

    private void BuildCurrentTreeFromScan(FileNode scanRoot)
    {
        CurrentRoot.Clear();

        // put each selected root as a top-level node in the tree
        foreach (var child in scanRoot.Children)
            CurrentRoot.Add(ToCurrentNode(child));
    }

    private static CurrentTreeNode ToCurrentNode(FileNode n)
    {
        var isFolder = n.IsDirectory || (n.Children?.Count > 0);
        var node = new CurrentTreeNode
        {
            Name = n.Name,
            FullPath = n.Path,
            IsFolder = isFolder
        };

        foreach (var c in n.Children)
            node.Children.Add(ToCurrentNode(c));

        node.FileCount = node.Children.Count(x => !x.IsFolder);
        node.FolderCount = node.Children.Count(x => x.IsFolder);
        return node;
    }

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
        catch { }
    }

    private void ProgressLinesClear()
    {
        try { MainThread.BeginInvokeOnMainThread(ProgressLines.Clear); }
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
        catch { }
    }

    private void SaveRoots()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RootsPath)!);
            File.WriteAllText(RootsPath, JsonSerializer.Serialize(SelectedRoots.ToList()));
        }
        catch { }
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