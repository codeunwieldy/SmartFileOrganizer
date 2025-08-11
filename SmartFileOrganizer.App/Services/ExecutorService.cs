using SmartFileOrganizer.App.Models;
using System.Runtime.InteropServices;

namespace SmartFileOrganizer.App.Services;

public class ExecutorService : IExecutorService
{
    public async Task<Snapshot> ExecuteAsync(
        Plan plan,
        IEnumerable<IExecutorService.ConflictResolution> resolutions,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var resDict = resolutions.ToDictionary(r => r.Destination, StringComparer.OrdinalIgnoreCase);
        var snap = new Snapshot();

        // 1) Moves
        foreach (var op in plan.Moves)
        {
            ct.ThrowIfCancellationRequested();
            var dest = op.Destination;

            if (File.Exists(dest) && resDict.TryGetValue(dest, out var r))
            {
                switch (r.Choice)
                {
                    case IExecutorService.ConflictChoice.Skip:
                        progress?.Report($"Skipped: {op.Source}");
                        continue;
                    case IExecutorService.ConflictChoice.Rename:
                        dest = !string.IsNullOrWhiteSpace(r.NewDestinationIfRename)
                            ? r.NewDestinationIfRename
                            : EnsureUnique(dest);
                        break;

                    case IExecutorService.ConflictChoice.Overwrite:
                        try { File.Delete(dest); } catch { dest = EnsureUnique(dest); }
                        break;
                }
            }
            else if (File.Exists(dest))
            {
                dest = EnsureUnique(dest);
            }

            var destDir = Path.GetDirectoryName(dest)!;
            Directory.CreateDirectory(destDir);

            File.Move(op.Source, dest, overwrite: false);
            snap.ReverseMoves.Add((dest, op.Source));
            progress?.Report($"Moved: {op.Source} -> {dest}");
            await Task.Yield();
        }

        // 2) Hardlinks
        foreach (var hl in plan.Hardlinks)
        {
            ct.ThrowIfCancellationRequested();

            var link = hl.LinkPath;
            var target = hl.TargetExistingPath;

            if (File.Exists(link))
            {
                try { File.Delete(link); }
                catch
                {
                    progress?.Report($"Skip link (cannot delete): {link}");
                    continue;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(link)!);

            if (TryCreateHardLink(link, target))
            {
                snap.CreatedHardlinks.Add(link);
                progress?.Report($"Linked: {link} → {target}");
            }
            else
            {
                try
                {
                    File.Copy(target, link, overwrite: false);
                    progress?.Report($"Copied (fallback): {link} ← {target}");
                }
                catch (Exception ex)
                {
                    progress?.Report($"Hardlink failed and copy failed: {link}: {ex.Message}");
                }
            }

            await Task.Yield();
        }

        return snap;
    }

    public async Task RevertAsync(Snapshot snapshot, IProgress<string>? progress, CancellationToken ct)
    {
        // Revert moves
        foreach (var (from, to) in snapshot.ReverseMoves.AsEnumerable().Reverse())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var destDir = Path.GetDirectoryName(to)!;
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                if (File.Exists(from)) File.Move(from, to, overwrite: true);
                progress?.Report($"Reverted: {from} -> {to}");
            }
            catch (Exception ex) { progress?.Report($"Revert failed: {from}: {ex.Message}"); }
            await Task.Yield();
        }

        // Remove created hardlinks
        foreach (var link in snapshot.CreatedHardlinks)
        {
            try { if (File.Exists(link)) File.Delete(link); }
            catch (Exception ex) { progress?.Report($"Failed to remove link: {link} ({ex.Message})"); }
        }

        // Optionally remove created directories if now empty
        foreach (var dir in snapshot.CreatedDirectories.Distinct())
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch { /* ignore */ }
        }
    }

    public Task<IReadOnlyList<IExecutorService.Conflict>> DryRunAsync(Plan plan, CancellationToken ct)
    {
        var list = new List<IExecutorService.Conflict>();

        // Moves
        foreach (var m in plan.Moves)
        {
            ct.ThrowIfCancellationRequested();
            var destDir = Path.GetDirectoryName(m.Destination)!;
            if (string.IsNullOrWhiteSpace(destDir))
                list.Add(new IExecutorService.Conflict(m.Destination, "Invalid destination", null));
            else if (File.Exists(m.Destination))
                list.Add(new IExecutorService.Conflict(m.Destination, "Destination exists", m.Destination));
            else
            {
                try
                {
                    Directory.CreateDirectory(destDir);
                    var probe = Path.Combine(destDir, $".sfo_probe_{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(probe, "x");
                    File.Delete(probe);
                }
                catch
                {
                    list.Add(new IExecutorService.Conflict(m.Destination, "No write access", null));
                }
            }
        }

        // Hardlinks
        foreach (var h in plan.Hardlinks)
        {
            ct.ThrowIfCancellationRequested();
            var destDir = Path.GetDirectoryName(h.LinkPath)!;
            if (!File.Exists(h.TargetExistingPath))
                list.Add(new IExecutorService.Conflict(h.LinkPath, "Hardlink target no longer exists", h.TargetExistingPath));
            else if (File.Exists(h.LinkPath))
                list.Add(new IExecutorService.Conflict(h.LinkPath, "Link path already exists", h.LinkPath));
            else
            {
                try { Directory.CreateDirectory(destDir); }
                catch
                {
                    list.Add(new IExecutorService.Conflict(h.LinkPath, "Cannot create destination directory", null));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<IExecutorService.Conflict>>(list);
    }

    private static bool TryCreateHardLink(string linkPath, string target)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(linkPath) || string.IsNullOrWhiteSpace(target)) return false;
            if (!File.Exists(target)) return false;

            var rootA = Path.GetPathRoot(linkPath);
            var rootB = Path.GetPathRoot(target);
            if (!string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase))
                return false; // cross-volume not possible
        }
        catch { return false; }

#if WINDOWS
        return CreateHardLink(linkPath, target, IntPtr.Zero);
#else
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("ln", $"-f", target, linkPath)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            var p = System.Diagnostics.Process.Start(psi);
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
#endif
    }

#if WINDOWS

    [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

#endif

    private static string EnsureUnique(string desired)
    {
        if (!File.Exists(desired)) return desired;
        var dir = Path.GetDirectoryName(desired)!;
        var name = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);
        int i = 1;
        string candidate;
        do candidate = Path.Combine(dir, $"{name} ({i++}){ext}");
        while (File.Exists(candidate));
        return candidate;
    }
}