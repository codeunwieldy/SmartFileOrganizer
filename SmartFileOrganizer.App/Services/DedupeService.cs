using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Services;

public class DedupeService : IDedupeService
{
    private readonly IHashingService _hasher;

    public DedupeService(IHashingService hasher) => _hasher = hasher;

    public async Task<List<DuplicateGroup>> FindDuplicatesAsync(IEnumerable<string> roots, IProgress<string>? progress, CancellationToken ct)
    {
        // 1) collect files -> bucket by size
        var files = new List<FileInfo>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var path in EnumerateFilesSafe(root))
                files.Add(new FileInfo(path));
        }

        var bySize = files.GroupBy(f => f.Length).Where(g => g.Count() > 1);

        var groups = new List<DuplicateGroup>();
        foreach (var sizeBucket in bySize)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Dedup pass (partial hash): {sizeBucket.Key:n0} bytes");

            // 2) partial hash (first 256KB) to prune
            var byPartial = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in sizeBucket)
            {
                try
                {
                    var ph = await _hasher.HashFileAsync(f.FullName, partialBytes: 256 * 1024, ct);
                    (byPartial.TryGetValue(ph, out var list) ? list : byPartial[ph] = new()).Add(f);
                }
                catch { /* ignore locked/inaccessible */ }
            }

            // 3) full hash within partial-collisions
            foreach (var partial in byPartial.Values.Where(v => v.Count > 1))
            {
                var byFull = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in partial)
                {
                    try
                    {
                        var full = await _hasher.HashFileAsync(f.FullName, partialBytes: 0, ct);
                        (byFull.TryGetValue(full, out var list) ? list : byFull[full] = new()).Add(f);
                    }
                    catch { /* ignore */ }
                }

                foreach (var dup in byFull.Where(kv => kv.Value.Count > 1))
                {
                    var group = new DuplicateGroup
                    {
                        Hash = dup.Key,
                        SizeBytes = sizeBucket.Key
                    };
                    foreach (var path in dup.Value.Select(v => v.FullName))
                        group.Paths.Add(path);

                    groups.Add(group);
                }
            }
        }
        return groups;
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var q = new Queue<string>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var dir = q.Dequeue();
            string[] subs = Array.Empty<string>(), files = Array.Empty<string>();
            try { subs = Directory.GetDirectories(dir); } catch { }
            try { files = Directory.GetFiles(dir); } catch { }
            foreach (var s in subs) q.Enqueue(s);
            foreach (var f in files) yield return f;
        }
    }
}