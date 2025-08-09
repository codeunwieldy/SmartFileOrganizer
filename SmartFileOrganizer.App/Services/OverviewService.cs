using SmartFileOrganizer.App.Models;
using System.Text.RegularExpressions;

namespace SmartFileOrganizer.App.Services;

public class OverviewService : IOverviewService
{
    private static readonly Dictionary<string, string> _extToCat = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "Images",
        [".jpg"] = "Images",
        [".jpeg"] = "Images",
        [".gif"] = "Images",
        [".webp"] = "Images",
        [".heic"] = "Images",
        [".bmp"] = "Images",
        [".mp4"] = "Videos",
        [".mov"] = "Videos",
        [".mkv"] = "Videos",
        [".mp3"] = "Audio",
        [".wav"] = "Audio",
        [".flac"] = "Audio",
        [".pdf"] = "Docs",
        [".doc"] = "Docs",
        [".docx"] = "Docs",
        [".txt"] = "Docs",
        [".md"] = "Docs",
        [".rtf"] = "Docs",
        [".xls"] = "Spreadsheets",
        [".xlsx"] = "Spreadsheets",
        [".csv"] = "Spreadsheets",
        [".ppt"] = "Presentations",
        [".pptx"] = "Presentations",
        [".zip"] = "Archives",
        [".7z"] = "Archives",
        [".rar"] = "Archives",
        [".tar"] = "Archives",
        [".gz"] = "Archives"
    };

    public OverviewResult Build(Plan executedPlan)
    {
        // Categories
        var byCat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in executedPlan.Moves)
        {
            var cat = CatFor(m.Destination);
            byCat[cat] = byCat.TryGetValue(cat, out var c) ? c + 1 : 1;
        }
        var cats = byCat.OrderByDescending(kv => kv.Value)
                        .Select(kv => new OverviewCategory(kv.Key, kv.Value))
                        .ToList();

        // Destination tree (counts only)
        var root = new OverviewNode("Organized", new())
        {
            Files = 0
        };
        foreach (var m in executedPlan.Moves)
        {
            var dir = Path.GetDirectoryName(m.Destination) ?? "";
            AddToTree(root, dir);
        }
        // now add counts per directory by counting files under exact dir path
        foreach (var group in executedPlan.Moves.GroupBy(m => Path.GetDirectoryName(m.Destination) ?? ""))
            Bump(root, group.Key, group.Count());

        return new OverviewResult(cats, root);
    }

    private static string CatFor(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return "Misc";
        return _extToCat.TryGetValue(ext, out var cat) ? cat : "Misc";
    }

    private static void AddToTree(OverviewNode root, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        var parts = Split(folderPath);
        var cur = root;
        var accum = "";
        for (int i = 0; i < parts.Length; i++)
        {
            accum = string.IsNullOrEmpty(accum) ? parts[i] : Path.Combine(accum, parts[i]);
            var next = cur.Children.FirstOrDefault(c => c.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                next = new OverviewNode(parts[i], new())
                {
                    Files = 0
                };

                cur.Children.Add(next);
            }
            cur = next;
        }
    }

    private static void Bump(OverviewNode node, string folderPath, int count)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        var parts = Split(folderPath);
        var cur = node;
        foreach (var p in parts)
        {
            cur = cur.Children.First(c => c.Name.Equals(p, StringComparison.OrdinalIgnoreCase));
        }
        cur.Files += count;
    }

    private static string[] Split(string path)
    {
        path = path.Replace('\\', '/');
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Normalize root like "C:" or "/" → ignore
        return parts.Where(p => !Regex.IsMatch(p, @"^[A-Za-z]:$")).ToArray();
    }
}