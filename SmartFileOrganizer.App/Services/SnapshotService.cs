using SmartFileOrganizer.App.Models;
using System.Text.Json;

namespace SmartFileOrganizer.App.Services;

public class SnapshotService : ISnapshotService
{
    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SFO", "Snapshots");

    public async Task SaveAsync(Snapshot s)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, s.Id + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(s));
    }

    public async Task<Snapshot?> GetAsync(string id)
    {
        var path = Path.Combine(Dir, id + ".json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Snapshot>(json);
    }
}