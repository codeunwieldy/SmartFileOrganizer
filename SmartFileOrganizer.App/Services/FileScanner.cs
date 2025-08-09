using SmartFileOrganizer.App.Models;
using System.Runtime.InteropServices;

namespace SmartFileOrganizer.App.Services;

public class FileScanner : IFileScanner
{
    private static readonly string[] SystemHintsWindows = new[] {
        "C:\\Windows", "C:\\Program Files", "C:\\Program Files (x86)", "C:\\Users\\All Users"
    };

    private static readonly string[] SystemHintsUnix = new[] {
        "/System/", "/Library/", "/bin/", "/sbin/", "/usr/", "/etc/", "/private/"
    };

    public async Task<ScanResult> ScanAsync(ScanOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        if (options.Roots.Count == 0)
            options.Roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        return await Task.Run(() =>
        {
            int files = 0, dirs = 0, totalItems = 0;
            bool truncated = false;

            var rootNode = new FileNode
            {
                Name = "Root",
                Path = "",
                IsDirectory = true,
                CreatedUtc = DateTime.UtcNow,
            };
            var digestChildren = new List<FileNodeDigest>();

            foreach (var r in options.Roots)
            {
                ct.ThrowIfCancellationRequested();
                if (IsSystemPath(r)) continue;

                progress?.Report($"Scanning: {r}");
                var (uiNode, dgNode) = Walk(r, 0);
                rootNode.Children.Add(uiNode);
                digestChildren.Add(dgNode);
            }

            var digestRoot = new FileNodeDigest("", "Root", true, 0, DateTime.UtcNow, digestChildren);

            return new ScanResult
            {
                RootTree = rootNode,
                Digest = digestRoot,
                TotalFiles = files,
                TotalDirs = dirs,
                Truncated = truncated
            };

            // Local functions
            (FileNode ui, FileNodeDigest dg) Walk(string path, int depth)
            {
                ct.ThrowIfCancellationRequested();

                if (depth > options.MaxDepth) { truncated = true; return (EmptyDirNode(path), EmptyDigest(path)); }
                if (IsSystemPath(path)) return (EmptyDirNode(path), EmptyDigest(path));

                var attr = File.GetAttributes(path);
                bool isDir = attr.HasFlag(FileAttributes.Directory);
                bool isHidden = attr.HasFlag(FileAttributes.Hidden);
                if (isHidden && !options.IncludeHidden) return (EmptyDirNode(path), EmptyDigest(path));

                if (!isDir)
                {
                    var fi = new FileInfo(path);
                    if (fi.Length > options.MaxFileSizeBytes) return (EmptyDirNode(path), EmptyDigest(path));
                    files++; totalItems++;
                    if (totalItems >= options.MaxItems) { truncated = true; }
                    return (new FileNode
                    {
                        Path = fi.FullName,
                        Name = fi.Name,
                        IsDirectory = false,
                        SizeBytes = fi.Length,
                        CreatedUtc = fi.CreationTimeUtc
                    },
                    new FileNodeDigest(fi.FullName, fi.Name, false, fi.Length, fi.CreationTimeUtc, null));
                }
                else
                {
                    dirs++; totalItems++;
                    var di = new DirectoryInfo(path);
                    var ui = new FileNode
                    {
                        Path = di.FullName,
                        Name = di.Name,
                        IsDirectory = true,
                        CreatedUtc = di.CreationTimeUtc
                    };
                    var dgKids = new List<FileNodeDigest>();

                    try
                    {
                        foreach (var dir in Directory.EnumerateDirectories(path))
                        {
                            if (totalItems >= options.MaxItems) { truncated = true; break; }
                            var (u, d) = Walk(dir, depth + 1);
                            if (!string.IsNullOrEmpty(u.Path)) ui.Children.Add(u);
                            if (d.Children is not null || d.IsDir == false || d.Path != "")
                                dgKids.Add(d);
                        }
                        foreach (var file in Directory.EnumerateFiles(path))
                        {
                            if (totalItems >= options.MaxItems) { truncated = true; break; }
                            var (u, d) = Walk(file, depth + 1);
                            if (!string.IsNullOrEmpty(u.Path)) ui.Children.Add(u);
                            if (!d.IsDir) dgKids.Add(d);
                        }
                    }
                    catch { /* swallow protected dirs */ }

                    return (ui, new FileNodeDigest(di.FullName, di.Name, true, 0, di.CreationTimeUtc, dgKids));
                }
            }

            static FileNode EmptyDirNode(string path)
            {
                var name = Path.GetFileName(path);
                return new FileNode { Path = path, Name = string.IsNullOrEmpty(name) ? path : name, IsDirectory = true, CreatedUtc = DateTime.UtcNow };
            }
            static FileNodeDigest EmptyDigest(string path)
            {
                var name = Path.GetFileName(path);
                return new FileNodeDigest(path, string.IsNullOrEmpty(name) ? path : name, true, 0, DateTime.UtcNow, new List<FileNodeDigest>());
            }
        }, ct);
    }

    private static bool IsSystemPath(string p)
    {
        var path = p.Replace('\\', '/');
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return SystemHintsWindows.Any(h => p.StartsWith(h, StringComparison.OrdinalIgnoreCase));
        return SystemHintsUnix.Any(h => path.StartsWith(h, StringComparison.OrdinalIgnoreCase));
    }
}