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
    [ObservableProperty] public partial string CurrentMode { get; set; } = "";
    [ObservableProperty] public partial bool IsModeVisible { get; set; } = false;

    // Interactive tree properties
    [ObservableProperty] public partial IDrawable? TreeDrawable { get; set; }
    [ObservableProperty] public partial double TreeZoom { get; set; } = 1.0;
    [ObservableProperty] public partial double TreePanX { get; set; } = 0.0;
    [ObservableProperty] public partial double TreePanY { get; set; } = 0.0;

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

        // Set current mode for UI display
        CurrentMode = mode switch
        {
            "desktop" => "Desktop",
            "downloads" => "Downloads",
            _ => "Everything"
        };
        IsModeVisible = true;

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
            try
            {
                var aiPlan = await _planner.GeneratePlanApiCallAsync(scan.RootTree, mode, _cts.Token);
                aiPlan.Moves = [.. aiPlan.Moves.Where(m => !ruleEval.ClaimedSources.Contains(m.Source))];
                mergedPlan.Moves.AddRange(aiPlan.Moves);
                mergedPlan.DeleteEmptyDirectories.AddRange(aiPlan.DeleteEmptyDirectories);
            }
            catch (Exception aiEx)
            {
                AppendProgress($"AI planning failed ({aiEx.Message}), using default organization...");

                // Fallback: Create a simple default plan based on file types
                var defaultPlan = CreateDefaultPlan(scan.RootTree, mode, roots.FirstOrDefault() ?? "");
                defaultPlan.Moves = [.. defaultPlan.Moves.Where(m => !ruleEval.ClaimedSources.Contains(m.Source))];
                mergedPlan.Moves.AddRange(defaultPlan.Moves);
                mergedPlan.DeleteEmptyDirectories.AddRange(defaultPlan.DeleteEmptyDirectories);
            }

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
                
                // Create and configure the duplicates page
                var dupPage = new DuplicatesPage(dedupeGroups);
                var result = new TaskCompletionSource<DuplicatesPage.ResultPayload>();
                dupPage.Result = result;

                // Use Shell navigation instead of NavigationService for shell content
                try
                {
                    await Shell.Current.GoToAsync("//DuplicatesPage");
                    
                    // Find the actual duplicates page instance in the shell
                    var currentPage = Shell.Current.CurrentPage;
                    if (currentPage is DuplicatesPage actualDupPage)
                    {
                        actualDupPage.ViewGroups.Clear();
                        foreach (var g in dedupeGroups)
                        {
                            actualDupPage.ViewGroups.Add(new DuplicatesPage.GroupVM
                            {
                                Header = $"{g.Paths.Count} × {g.SizeBytes / 1024} KB — {g.Hash[..8]}…",
                                Items = new System.Collections.ObjectModel.ObservableCollection<string>(g.Paths)
                            });
                        }
                        actualDupPage.Result = result;
                    }
                    
                    var duplicateResult = await result.Task;

                    if (duplicateResult.Apply)
                    {
                        foreach (var kv in duplicateResult.MoveMap)
                            mergedPlan.Moves.Add(new MoveOp(kv.Key, kv.Value));

                        foreach (var hl in duplicateResult.Hardlinks)
                            mergedPlan.Hardlinks.Add(new HardlinkOp(hl.LinkPath, hl.Target));

                        StatusText = $"Duplicates resolved: {duplicateResult.MoveMap.Count} moves, {duplicateResult.Hardlinks.Count} links";
                        AppendProgress(StatusText);
                    }
                    else
                    {
                        AppendProgress("Duplicates: skipped by user.");
                    }
                    
                    // Navigate back to main page
                    await Shell.Current.GoToAsync("//MainPage");
                }
                catch (Exception navEx)
                {
                    AppendProgress($"Navigation error: {navEx.Message}");
                    // Skip duplicates processing if navigation fails
                    AppendProgress("Duplicates: skipped due to navigation error.");
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
            CurrentMode = "";
            IsModeVisible = false;
        }
    }

    [RelayCommand]
    public void SelectCurrent(string path)
    {
        CurrentBreadcrumb = path;
        AppendProgress($"Selected: {path}");
    }

    [RelayCommand]
    public void SelectTreeNode(object nodeData)
    {
        if (nodeData is CurrentTreeNode node)
        {
            CurrentBreadcrumb = node.FullPath;
            AppendProgress($"Selected: {node.Name} ({node.FullPath})");

            // If it's a folder, we could expand/collapse it
            if (node.IsFolder)
            {
                node.IsExpanded = !node.IsExpanded;
                RefreshTreeView();
            }
        }
    }

    [RelayCommand]
    public void ZoomTree(double zoomDelta)
    {
        TreeZoom = Math.Max(0.1, Math.Min(5.0, TreeZoom + zoomDelta));
        RefreshTreeView();
    }

    [RelayCommand]
    public void PanTree(Microsoft.Maui.Graphics.Point delta)
    {
        TreePanX += delta.X;
        TreePanY += delta.Y;
        RefreshTreeView();
    }

    [RelayCommand]
    public void ResetTreeView()
    {
        TreeZoom = 1.0;
        TreePanX = 0.0;
        TreePanY = 0.0;
        RefreshTreeView();
    }

    public void RefreshTreeView()
    {
        if (CurrentRoot.Any())
        {
            TreeDrawable = new InteractiveTreeDrawable(CurrentRoot, TreeZoom, TreePanX, TreePanY, this);
        }
    }

    [RelayCommand]
    public async Task ExecuteAsync()
    {
        // Check if we have a plan first
        if (CurrentPlan is null)
        {
            AppendProgress("No plan available. Please generate a plan first by clicking 'Desktop', 'Downloads', or 'Everything'.");
            StatusText = "No plan available";
            
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Application.Current?.MainPage?.DisplayAlert("No Plan", "Please generate a plan first by scanning files.", "OK")!;
            });
            return;
        }

        // Check if plan has any moves
        if (CurrentPlan.Moves.Count == 0 && CurrentPlan.Hardlinks.Count == 0)
        {
            AppendProgress("Plan has no moves. Nothing to execute.");
            StatusText = "No moves in plan";
            
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Application.Current?.MainPage?.DisplayAlert("Empty Plan", "The current plan has no file moves to execute.", "OK")!;
            });
            return;
        }

        AppendProgress("Execute plan…");

        try
        {
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

            var applyProgress = new Progress<ScanProgress>(ReportProgress);
            LastSnapshot = await _executor.ExecuteAsync(CurrentPlan, resolutions, applyProgress, _cts?.Token ?? CancellationToken.None);
            await _snapshots.SaveAsync(LastSnapshot);

            // Final completed state
            ReportProgress(new ScanProgress { Stage = JobStage.Completed, Message = "Completed." });
            await Task.Delay(1000);

            // Navigate to overview
            await Shell.Current.GoToAsync("//OverviewPage");
        }
        catch (Exception ex)
        {
            AppendProgress($"Execute failed: {ex.Message}");
            StatusText = "Execute failed";
            
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Application.Current?.MainPage?.DisplayAlert("Execution Error", $"Failed to execute plan: {ex.Message}", "OK")!;
            });
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
        AppendProgress($"Attempting to remove root: {path}");
        
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendProgress("Cannot remove empty path");
            return;
        }

        var removed = SelectedRoots.Remove(path);
        AppendProgress($"Remove operation result: {removed}");
        
        if (removed)
        {
            SaveRoots();
            StatusText = $"Removed: {path}";
            AppendProgress($"Successfully removed and saved: {path}");
            AppendProgress($"Remaining roots: {SelectedRoots.Count}");
        }
        else
        {
            AppendProgress($"Failed to remove: {path} (not found in collection)");
            AppendProgress($"Current roots: {string.Join(", ", SelectedRoots)}");
        }
    }

    [RelayCommand] public void CancelOps() => _cts?.Cancel();

    [RelayCommand]
    public async Task RevertAsync()
    {
        if (LastSnapshot is null) return;

        AppendProgress("Reverting changes…");
        IsProgressVisible = true;
        StatusText = "Reverting…";

        try
        {
            var progress = new Progress<string>(msg => AppendProgress(msg));
            await _executor.RevertAsync(LastSnapshot, progress, CancellationToken.None);
            StatusText = "Reverted successfully";
            AppendProgress("Revert completed.");
        }
        catch (Exception ex)
        {
            StatusText = $"Revert failed: {ex.Message}";
            AppendProgress($"Revert error: {ex.Message}");
        }
        finally
        {
            IsProgressVisible = false;
        }
    }

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

            // Only show indeterminate spinner during cancelling - always show progress bar for other stages
            IsIndeterminate = progress.Stage == JobStage.Cancelling;

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
            JobStage.Scanning => progress.FilesTotal > 0 && progress.FilesTotal < 40_000
                ? $"Scanning {progress.FilesProcessed}/{progress.FilesTotal} files ({progress.StageProgress:P0})"
                : $"Scanning {progress.FilesProcessed} files ({progress.StageProgress:P0})",
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

        // Refresh the interactive tree view
        RefreshTreeView();
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

    private Plan CreateDefaultPlan(FileNode rootTree, string mode, string baseRoot)
    {
        var plan = new Plan { ScopeDescription = $"Default {mode} organization" };
        var baseDir = Path.GetDirectoryName(baseRoot) ?? baseRoot;

        // Define default organization folders
        var organizationFolders = new Dictionary<string, string[]>
        {
            ["Images"] = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".svg", ".webp"],
            ["Documents"] = [".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt"],
            ["Spreadsheets"] = [".xls", ".xlsx", ".csv", ".ods"],
            ["Presentations"] = [".ppt", ".pptx", ".odp"],
            ["Archives"] = [".zip", ".rar", ".7z", ".tar", ".gz"],
            ["Videos"] = [".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm"],
            ["Audio"] = [".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma"],
            ["Code"] = [".cs", ".js", ".ts", ".html", ".css", ".cpp", ".py", ".java"],
            ["Executables"] = [".exe", ".msi", ".dmg", ".pkg", ".deb", ".rpm"]
        };

        // Organize files by type
        OrganizeFilesByType(rootTree, plan.Moves, baseDir, organizationFolders);

        return plan;
    }

    private void OrganizeFilesByType(FileNode node, List<MoveOp> moves, string baseDir, Dictionary<string, string[]> organizationFolders)
    {
        foreach (var child in node.Children)
        {
            if (child.IsDirectory)
            {
                OrganizeFilesByType(child, moves, baseDir, organizationFolders);
            }
            else
            {
                var extension = Path.GetExtension(child.Name).ToLowerInvariant();
                var targetFolder = GetTargetFolderForExtension(extension, organizationFolders);

                if (!string.IsNullOrEmpty(targetFolder))
                {
                    var destinationDir = Path.Combine(baseDir, "Organized", targetFolder);
                    var destination = Path.Combine(destinationDir, child.Name);

                    // Avoid moving files that are already in organized folders
                    if (!child.Path.Contains("Organized", StringComparison.OrdinalIgnoreCase))
                    {
                        moves.Add(new MoveOp(child.Path, destination));
                    }
                }
            }
        }
    }

    private string GetTargetFolderForExtension(string extension, Dictionary<string, string[]> organizationFolders)
    {
        foreach (var folder in organizationFolders)
        {
            if (folder.Value.Contains(extension))
                return folder.Key;
        }
        return "Other"; // Default folder for unrecognized file types
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

    private sealed class InteractiveTreeDrawable : IDrawable
    {
        private readonly IReadOnlyList<CurrentTreeNode> _rootNodes;
        private readonly double _zoom;
        private readonly double _panX;
        private readonly double _panY;
        private readonly MainViewModel _viewModel;
        private const float NodeHeight = 24f;
        private const float IndentWidth = 20f;
        private const float IconSize = 16f;

        public InteractiveTreeDrawable(IReadOnlyList<CurrentTreeNode> rootNodes, double zoom, double panX, double panY, MainViewModel viewModel)
        {
            _rootNodes = rootNodes;
            _zoom = zoom;
            _panX = panX;
            _panY = panY;
            _viewModel = viewModel;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.Antialias = true;
            canvas.SaveState();

            // Apply zoom and pan transformations
            canvas.Scale((float)_zoom, (float)_zoom);
            canvas.Translate((float)_panX, (float)_panY);

            if (_rootNodes?.Any() != true)
            {
                canvas.FontSize = 14;
                canvas.FontColor = Colors.Gray;
                canvas.DrawString("No files scanned yet. Click 'Desktop', 'Downloads', or 'Everything' to scan files.",
                    new RectF(10, 10, dirtyRect.Width - 20, 100), HorizontalAlignment.Left, VerticalAlignment.Top);
                canvas.RestoreState();
                return;
            }

            float currentY = 10f;
            foreach (var rootNode in _rootNodes)
            {
                currentY = DrawNode(canvas, rootNode, 0, currentY, dirtyRect.Width);
            }

            canvas.RestoreState();
        }

        private float DrawNode(ICanvas canvas, CurrentTreeNode node, int depth, float y, float maxWidth)
        {
            float x = 10f + (depth * IndentWidth);
            var bounds = new RectF(x, y, maxWidth - x - 10f, NodeHeight);

            // Draw background for hover/selection (simplified)
            canvas.FillColor = Colors.Transparent;
            canvas.FillRectangle(bounds);

            // Draw expand/collapse icon for folders
            if (node.IsFolder && node.Children.Any())
            {
                canvas.FontSize = 12;
                canvas.FontColor = Colors.Gray;
                var expandIcon = node.IsExpanded ? "▼" : "▶";
                canvas.DrawString(expandIcon, new RectF(x, y + 4, IconSize, IconSize), HorizontalAlignment.Center, VerticalAlignment.Center);
            }

            // Draw file/folder icon
            float iconX = x + (node.IsFolder && node.Children.Any() ? IconSize + 2 : 2);
            canvas.FontSize = 14;
            canvas.FontColor = Colors.Black;
            var icon = node.IsFolder ? "📁" : "📄";
            canvas.DrawString(icon, new RectF(iconX, y + 4, IconSize, IconSize), HorizontalAlignment.Center, VerticalAlignment.Center);

            // Draw name
            float textX = iconX + IconSize + 4;
            canvas.FontSize = 13;
            canvas.FontColor = node.IsFolder ? Color.FromRgb(46, 125, 50) : Color.FromRgb(21, 101, 192);
            canvas.DrawString(node.Name, new RectF(textX, y + 4, maxWidth - textX - 50, NodeHeight - 8),
                HorizontalAlignment.Left, VerticalAlignment.Center);

            // Draw file count for folders
            if (node.IsFolder && node.FileCount > 0)
            {
                canvas.FontSize = 11;
                canvas.FontColor = Colors.Gray;
                var countText = $"({node.FileCount} files)";
                canvas.DrawString(countText, new RectF(maxWidth - 100, y + 4, 90, NodeHeight - 8),
                    HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            float nextY = y + NodeHeight;

            // Draw children if expanded
            if (node.IsFolder && node.IsExpanded && node.Children.Any())
            {
                foreach (var child in node.Children.Take(50)) // Limit visible children for performance
                {
                    nextY = DrawNode(canvas, child, depth + 1, nextY, maxWidth);
                }
            }

            return nextY;
        }
    }
}