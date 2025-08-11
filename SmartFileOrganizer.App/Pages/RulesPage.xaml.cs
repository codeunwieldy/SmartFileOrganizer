using CommunityToolkit.Mvvm.Input;
using SmartFileOrganizer.App.Models;
using SmartFileOrganizer.App.Services;
using System.Collections.ObjectModel;

namespace SmartFileOrganizer.App.Pages;

public partial class RulesPage : ContentPage
{
    public class RuleVM : Rule
    {
        public int ActionIndex
        {
            get => Action == RuleActionKind.MoveToFolder ? 0 : 1;
            set => Action = (value == 0 ? RuleActionKind.MoveToFolder : RuleActionKind.Ignore);
        }

        public bool IsMove => Action == RuleActionKind.MoveToFolder;
    }

    private readonly IRuleStore _store;
    private readonly IFolderPicker? _picker;
    private readonly IFileScanner _scanner;
    private readonly IRuleEngine _engine;

    public ObservableCollection<RuleVM> Rules { get; } = new();
    public IAsyncRelayCommand<RuleVM>? DeleteRuleCommand { get; set; }
    public IAsyncRelayCommand<string>? RemoveScopeCommand { get; set; }

    public RulesPage(IRuleStore store, IFileScanner scanner, IRuleEngine engine)
    {
        InitializeComponent();
        BindingContext = this;

        _store = store;
        _scanner = scanner;
        _engine = engine;
        _picker = Application.Current?.Handler?.MauiContext?.Services?.GetService<IFolderPicker>();

        DeleteRuleCommand = new AsyncRelayCommand<RuleVM>(async r =>
        {
            if (r is null) return;
            Rules.Remove(r);
            await Task.CompletedTask;
        });

        RemoveScopeCommand = new AsyncRelayCommand<string>(async scope =>
        {
            var selected = RulesList?.SelectedItem as RuleVM; // RulesList should exist in XAML with x:Name="RulesList"
            if (selected is null || string.IsNullOrWhiteSpace(scope)) return;
            selected.Scopes.Remove(scope);
            await Task.CompletedTask;
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var set = await _store.LoadAsync();
        Rules.Clear();

        foreach (var r in set.Rules.OrderBy(r => r.Priority))
        {
            Rules.Add(new RuleVM
            {
                Name = r.Name,
                Pattern = r.Pattern,
                MatchFullPath = r.MatchFullPath,
                Action = r.Action,
                DestinationFolder = r.DestinationFolder,
                GroupByYear = r.GroupByYear,
                GroupByYearMonth = r.GroupByYearMonth,
                Enabled = r.Enabled,
                Priority = r.Priority,
                Scopes = r.Scopes ?? new List<string>() // ensure not null
            });
        }
    }

    private void OnAdd(object? sender, EventArgs e)
        => Rules.Add(new RuleVM { Scopes = new List<string>() }); // ensure not null

    private async void OnSave(object? sender, EventArgs e)
    {
        var set = new RuleSet { Rules = Rules.Select(r => (Rule)r).ToList() };
        await _store.SaveAsync(set);
        await DisplayAlert("Saved", "Rules saved.", "OK");
    }

    private async void OnClose(object? sender, EventArgs e) => await Navigation.PopAsync();

    private async void OnBrowseDestination(object? sender, EventArgs e)
    {
        if (_picker is null)
        {
            await DisplayAlert("Not available", "Folder picker not supported on this platform.", "OK");
            return;
        }
        if ((sender as Button)?.CommandParameter is not RuleVM vm) return;

        var picked = await _picker.PickFolderAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(picked))
            vm.DestinationFolder = picked;
    }

    private async void OnAddScope(object? sender, EventArgs e)
    {
        if (_picker is null)
        {
            await DisplayAlert("Not available", "Folder picker not supported on this platform.", "OK");
            return;
        }
        if ((sender as Button)?.CommandParameter is not RuleVM vm) return;

        var picked = await _picker.PickFolderAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(picked) &&
            !vm.Scopes.Contains(picked, StringComparer.OrdinalIgnoreCase))
        {
            vm.Scopes.Add(picked);
        }
    }

    private async void OnTest(object? sender, EventArgs e)
    {
        var roots = new List<string>();
        var scoped = Rules.SelectMany(r => r.Scopes ?? new()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (scoped.Count > 0)
        {
            roots.AddRange(scoped);
        }
        else if (_picker is not null)
        {
            var picked = await _picker.PickFolderAsync(CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(picked)) roots.Add(picked);
        }

        if (roots.Count == 0)
        {
            await DisplayAlert("Test rules", "No scope selected. Add a scope to a rule or pick a folder.", "OK");
            return;
        }

        var options = new ScanOptions { MaxDepth = 3, MaxItems = 2000 };
        options.Roots.AddRange(roots);
        var scan = await _scanner.ScanAsync(options, progress: null, CancellationToken.None);

        var set = new RuleSet { Rules = Rules.Select(r => (Rule)r).ToList() };
        var eval = _engine.Evaluate(set, scan.RootTree);

        var moved = eval.Moves.Count;
        var ignored = eval.IgnoredSources.Count;

        var preview = string.Join("\n",
            eval.Moves.Take(5).Select(m =>
            {
                var srcName = Path.GetFileName(m.Source);
                var destDir = Path.GetDirectoryName(m.Destination);
                var destTail = destDir is null ? "" : Path.GetFileName(destDir);
                return $"{srcName} → …{destTail}";
            })
        );
        if (string.IsNullOrEmpty(preview)) preview = "(no examples)";

        await DisplayAlert("Rules test",
            $"Roots: {roots.Count}\nMoves: {moved}\nIgnored: {ignored}\n\nExamples:\n{preview}",
            "OK");
    }
}