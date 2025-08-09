using System.Text.Json;
using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Services;

public class RuleStore : IRuleStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SFO", "Config");
    private static string PathFile => Path.Combine(Dir, "rules.json");

    public async Task<RuleSet> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(PathFile)) return new RuleSet();
            var json = await File.ReadAllTextAsync(PathFile, ct);
            return JsonSerializer.Deserialize<RuleSet>(json) ?? new RuleSet();
        }
        catch { return new RuleSet(); }
    }

    public async Task SaveAsync(RuleSet set, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(set, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(PathFile, json, ct);
    }
}

