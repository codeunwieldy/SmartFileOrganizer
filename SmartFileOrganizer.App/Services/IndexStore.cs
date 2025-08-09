using SmartFileOrganizer.App.Models;
using System.Text.Json;

namespace SmartFileOrganizer.App.Services;

public class IndexStore : IIndexStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SFO", "Index");

    private static string FilePath => Path.Combine(Dir, "last-scan.json");

    public async Task SaveAsync(ScanResult result, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        var payload = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
        await File.WriteAllTextAsync(FilePath, payload, ct);
    }

    public async Task<ScanResult?> LoadLastAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return null;
        var json = await File.ReadAllTextAsync(FilePath, ct);
        return JsonSerializer.Deserialize<ScanResult>(json);
    }
}