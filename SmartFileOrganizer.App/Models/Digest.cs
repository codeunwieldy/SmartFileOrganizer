namespace SmartFileOrganizer.App.Models;

public record FileNodeDigest(
    string Path,
    string Name,
    bool IsDir,
    long Size,
    DateTime CreatedUtc,
    List<FileNodeDigest>? Children);

public class ScanOptions
{
    public List<string> Roots { get; } = new();
    public int MaxDepth { get; set; } = 6;             // safety default
    public int MaxItems { get; set; } = 50_000;        // cap payload/work
    public bool IncludeHidden { get; set; } = false;
    public bool FollowSymlinks { get; set; } = false;
    public long MaxFileSizeBytes { get; set; } = 2L * 1024 * 1024 * 1024; // 2GB cap
}

public class ScanResult
{
    public FileNode RootTree { get; set; } = new();          // rich tree for UI
    public FileNodeDigest Digest { get; set; } = default!;   // compact tree for proxy
    public int TotalFiles { get; set; }
    public int TotalDirs { get; set; }
    public bool Truncated { get; set; }                      // hit MaxItems/MaxDepth

    public List<DuplicateGroup> Duplicates { get; set; } = new();
}