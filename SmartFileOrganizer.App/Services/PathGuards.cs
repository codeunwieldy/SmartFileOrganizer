namespace SmartFileOrganizer.App.Services;

public static class PathGuards
{
    private static readonly string[] SystemHints =
    [
        "/System/", "/Library/", "/bin/", "/sbin/", "/usr/", "/etc/", "/private/",
        "C:/Windows", "C:/Program Files", "C:/Program Files (x86)", "C:/Users/All Users",
    ];

    public static bool IsSystemPath(string path)
    {
        var p = path.Replace('\\', '/');
        return SystemHints.Any(h => p.StartsWith(h, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsUnderRoot(string path, string allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(allowedRoot)) return true;
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(allowedRoot);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}