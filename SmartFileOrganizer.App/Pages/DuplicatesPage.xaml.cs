using CommunityToolkit.Mvvm.Input;
using SmartFileOrganizer.App.Models;
using SmartFileOrganizer.App.Services;
using System.Collections.ObjectModel;

namespace SmartFileOrganizer.App.Pages;

public partial class DuplicatesPage : ContentPage
{
    // === Result payload ===
    public enum DedupePolicy
    { MoveNextToKept, HardlinkToKept, MoveToArchive }

    public class ResultPayload
    {
        public bool Apply { get; init; }
        public DedupePolicy Policy { get; init; }
        public Dictionary<string, string> MoveMap { get; init; } = [];              // src -> dest
        public List<(string LinkPath, string Target)> Hardlinks { get; init; } = new();// hardlink policy
        public string? ArchiveFolder { get; init; }
    }

    // === Per-group VM for display ===
    public class GroupVM
    {
        public required string Header { get; init; }
        public required ObservableCollection<string> Items { get; init; }
        public string? Kept { get; set; }
    }

    public ObservableCollection<GroupVM> ViewGroups { get; } = new();

    // Await this from caller
    public TaskCompletionSource<ResultPayload>? Result { get; set; }

    public IAsyncRelayCommand<string>? MarkKeepHereCommand { get; set; }

    private readonly IFolderPicker? _folderPicker;

    public DuplicatesPage(IEnumerable<DuplicateGroup> groups)
    {
        InitializeComponent();
        BindingContext = this;

        _folderPicker = Application.Current?.Handler?.MauiContext?.Services?.GetService<IFolderPicker>();

        InitializeDuplicateGroups(groups);

        // "Keep here" command
        MarkKeepHereCommand = new AsyncRelayCommand<string>(async path =>
        {
            var group = ViewGroups.FirstOrDefault(v => v.Items.Contains(path!));
            if (group is not null)
            {
                group.Kept = path;
                await DisplayAlert("Marked", $"Keeping: {path}", "OK");
            }
        });
    }

    // Parameterless constructor for Shell navigation
    public DuplicatesPage()
    {
        InitializeComponent();
        BindingContext = this;

        _folderPicker = Application.Current?.Handler?.MauiContext?.Services?.GetService<IFolderPicker>();

        // "Keep here" command
        MarkKeepHereCommand = new AsyncRelayCommand<string>(async path =>
        {
            var group = ViewGroups.FirstOrDefault(v => v.Items.Contains(path!));
            if (group is not null)
            {
                group.Kept = path;
                await DisplayAlert("Marked", $"Keeping: {path}", "OK");
            }
        });
    }

    public void InitializeDuplicateGroups(IEnumerable<DuplicateGroup> groups)
    {
        // summary text
        var reclaim = groups.Sum(g => ((long)g.Paths.Count - 1) * g.SizeBytes);
        if (Summary != null)
        {
            Summary.Text = $"{groups.Count()} groups • Potential reclaim: ~{reclaim / (1024 * 1024)} MB";
        }

        // materialize groups
        ViewGroups.Clear();
        foreach (var g in groups)
        {
            ViewGroups.Add(new GroupVM
            {
                Header = $"{g.Paths.Count} × {g.SizeBytes / 1024} KB — {g.Hash[..8]}…",
                Items = new ObservableCollection<string>(g.Paths)
            });
        }
    }

    private async void OnPickArchive(object sender, EventArgs e)
    {
        if (_folderPicker is null)
        {
            await DisplayAlert("Not available", "Folder picker not supported on this platform.", "OK");
            return;
        }
        var picked = await _folderPicker.PickFolderAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(picked))
            ArchivePathBox.Text = picked;
    }

    private async void OnApply(object sender, EventArgs e)
    {
        var policy = PolicyPicker.SelectedIndex switch
        {
            1 => DedupePolicy.HardlinkToKept,
            2 => DedupePolicy.MoveToArchive,
            _ => DedupePolicy.MoveNextToKept
        };

        // Archive policy requires a folder
        if (policy == DedupePolicy.MoveToArchive && string.IsNullOrWhiteSpace(ArchivePathBox?.Text))
        {
            await DisplayAlert("Archive folder required", "Please pick an Archive folder.", "OK");
            return;
        }

        var payload = new ResultPayload
        {
            Apply = true,
            Policy = policy,
            ArchiveFolder = ArchivePathBox?.Text
        };

        // Track destinations we’re creating in this run to avoid duplicate target collisions
        var plannedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int keptCount = 0;

        foreach (var g in ViewGroups)
        {
            if (string.IsNullOrWhiteSpace(g.Kept)) continue;
            keptCount++;

            foreach (var p in g.Items)
            {
                if (string.Equals(p, g.Kept, StringComparison.OrdinalIgnoreCase))
                    continue;

                switch (policy)
                {
                    case DedupePolicy.HardlinkToKept:
                        // Replace p with a hardlink pointing to kept file g.Kept
                        payload.Hardlinks.Add((LinkPath: p, Target: g.Kept));
                        break;

                    case DedupePolicy.MoveToArchive:
                        {
                            var dest = Path.Combine(payload.ArchiveFolder!, Path.GetFileName(p));
                            dest = EnsureUniqueIn(plannedDestinations, dest);
                            payload.MoveMap[p] = dest;
                            break;
                        }

                    default: // MoveNextToKept
                        {
                            var destFolder = Path.GetDirectoryName(g.Kept)!;
                            var dest = Path.Combine(destFolder, Path.GetFileName(p));
                            dest = EnsureUniqueIn(plannedDestinations, dest);
                            payload.MoveMap[p] = dest;
                            break;
                        }
                }
            }
        }

        if (keptCount == 0)
        {
            await DisplayAlert("Nothing selected", "Mark at least one file as the one to keep.", "OK");
            return;
        }

        Result?.TrySetResult(payload);
        await Navigation.PopAsync();

        // Local helper: ensures uniqueness for this run (doesn't touch disk)
        static string EnsureUniqueIn(HashSet<string> seen, string desired)
        {
            if (seen.Add(desired)) return desired;

            var dir = Path.GetDirectoryName(desired)!;
            var name = Path.GetFileNameWithoutExtension(desired);
            var ext = Path.GetExtension(desired);
            int i = 1;
            string candidate;
            do candidate = Path.Combine(dir, $"{name} ({i++}){ext}");
            while (!seen.Add(candidate));
            return candidate;
        }
    }

    private async void OnSkip(object sender, EventArgs e)
    {
        Result?.TrySetResult(new ResultPayload { Apply = false });
        await Navigation.PopAsync();
    }
}