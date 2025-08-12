using SmartFileOrganizer.App.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace SmartFileOrganizer.App.Services;

public class FileScanner : IFileScanner
{
    private static readonly string[] SystemHintsWindows = new[] {
    @"C:\Windows",
    @"C:\Program Files",
    @"C:\Program Files (x86)",
    @"C:\Users\All Users", // legacy junction; see note below
    @"C:\ProgramData",
    @"C:\System Volume Information",
    @"C:\$Recycle.Bin",
    @"C:\Config.Msi",
    @"C:\Documents and Settings", // legacy junction
    @"C:\Recovery",
    @"C:\Intel",
    @"C:\PerfLogs",
    @"C:\inetpub", // IIS web server directory
    @"C:\Windows.old", // Old Windows installation
    @"C:\Boot", // Boot files
    @"C:\bootmgr", // Boot manager
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
};


    private static readonly string[] SystemHintsUnix = new[] {
        "/System/", "/Library/", "/bin/", "/sbin/", "/usr/", "/etc/", "/private/"
    };

    public async Task<ScanResult> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (options.Roots.Count == 0)
            options.Roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        return await Task.Run(() =>
        {
            int files = 0, dirs = 0, totalItems = 0;
            long bytesProcessed = 0;
            int errors = 0;
            int skipped = 0;
            bool truncated = false;
            var stopwatch = Stopwatch.StartNew();
            var lastReportTime = DateTime.MinValue;
            
            // Estimate total items for better progress reporting
            int estimatedTotal = 0;
            bool hasEstimate = false;

            // Local function for reporting progress
            void ReportProgress(JobStage stage, string? message = null)
            {
                if (progress == null) return;

                // Throttle updates to avoid excessive reporting
                if ((DateTime.Now - lastReportTime).TotalMilliseconds < 100 && stage == JobStage.Scanning)
                {
                    return;
                }
                lastReportTime = DateTime.Now;

                double stageProgress = 0.0;
                long filesTotal = estimatedTotal > 0 ? estimatedTotal : options.MaxItems;
                
                if (stage == JobStage.Estimating)
                {
                    // For estimation, provide a meaningful progress even if we don't have exact numbers
                    stageProgress = Math.Min(1.0, (double)totalItems / Math.Max(100, options.MaxItems / 10));
                }
                else if (stage == JobStage.Scanning)
                {
                    if (hasEstimate && estimatedTotal > 0)
                    {
                        stageProgress = Math.Min(1.0, (double)totalItems / estimatedTotal);
                        filesTotal = estimatedTotal;
                    }
                    else
                    {
                        stageProgress = Math.Min(1.0, (double)totalItems / options.MaxItems);
                        filesTotal = options.MaxItems;
                    }
                }
                else if (stage == JobStage.Completed)
                {
                    stageProgress = 1.0;
                    filesTotal = totalItems; // Use actual count for final report
                }

                var throughput = stopwatch.Elapsed.TotalSeconds > 0 ? totalItems / stopwatch.Elapsed.TotalSeconds : 0;
                TimeSpan? eta = null;
                if (throughput > 0 && stageProgress < 1.0 && hasEstimate)
                {
                    var remaining = estimatedTotal - totalItems;
                    eta = TimeSpan.FromSeconds(remaining / throughput);
                }

                progress.Report(new ScanProgress
                {
                    Stage = stage,
                    StageProgress = stageProgress,
                    FilesProcessed = files,
                    FilesTotal = filesTotal,
                    BytesProcessed = bytesProcessed,
                    BytesTotal = 0, // Cannot reliably determine total bytes upfront
                    Throughput = throughput,
                    Eta = eta,
                    Message = message,
                    Errors = errors,
                    Skipped = skipped
                });
            }

            // Initial report: Estimating stage
            ReportProgress(JobStage.Estimating, "Estimating…");

            // Quick estimation phase for better progress reporting
            try
            {
                int rootsProcessed = 0;
                foreach (var r in options.Roots)
                {
                    if (Directory.Exists(r) && !IsSystemPath(r))
                    {
                        // Report progress during estimation with proper stage progress
                        var estimateStageProgress = (double)rootsProcessed / options.Roots.Count;
                        
                        // Temporarily update totalItems for progress calculation during estimation
                        var oldTotalItems = totalItems;
                        totalItems = (int)(estimateStageProgress * 100); // Scale for meaningful progress during estimation
                        
                        ReportProgress(JobStage.Estimating, $"Estimating files in {Path.GetFileName(r)}…");
                        
                        // Restore totalItems for actual scanning
                        totalItems = oldTotalItems;
                        
                        var estimate = EstimateItems(r, options.MaxDepth, 0);
                        estimatedTotal += Math.Min(estimate, options.MaxItems / options.Roots.Count);
                    }
                    rootsProcessed++;
                }
                hasEstimate = estimatedTotal > 0;
                if (hasEstimate)
                {
                    estimatedTotal = Math.Min(estimatedTotal, options.MaxItems);
                }
            }
            catch
            {
                // If estimation fails, fall back to MaxItems
                estimatedTotal = options.MaxItems;
                hasEstimate = false;
            }

            // Reset totalItems for actual scanning
            totalItems = 0;

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
                if (IsSystemPath(r)) { skipped++; continue; }

                ReportProgress(JobStage.Scanning, $"Scanning: {r}");
                var (uiNode, dgNode) = Walk(r, 0);
                rootNode.Children.Add(uiNode);
                digestChildren.Add(dgNode);
            }

            var digestRoot = new FileNodeDigest("", "Root", true, 0, DateTime.UtcNow, digestChildren);

            stopwatch.Stop();

            // Final report: Completed stage or No files to process.
            if (totalItems == 0)
            {
                ReportProgress(JobStage.Completed, "No files to process.");
            }
            else
            {
                ReportProgress(JobStage.Completed, "Scan complete.");
            }

            return new ScanResult
            {
                RootTree = rootNode,
                Digest = digestRoot,
                TotalFiles = files,
                TotalDirs = dirs,
                Truncated = truncated
            };

            // Local functions
            int EstimateItems(string path, int maxDepth, int currentDepth)
            {
                if (currentDepth >= maxDepth || IsSystemPath(path)) return 0;
                
                try
                {
                    // Check if we can access the directory first
                    if (!Directory.Exists(path))
                        return 0;

                    var count = 0;
                    
                    // Use a more defensive approach to enumerate entries
                    var entries = new List<string>();
                    try
                    {
                        entries.AddRange(Directory.EnumerateFileSystemEntries(path).Take(1000));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // If we can't enumerate, return a small default estimate
                        return 10;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        return 0;
                    }
                    catch (IOException)
                    {
                        return 10;
                    }
                    
                    count += entries.Count;
                    
                    // For deeper estimation, sample a few subdirectories
                    if (currentDepth < 2)
                    {
                        try
                        {
                            var subDirs = Directory.EnumerateDirectories(path)
                                .Where(dir => !IsSystemPath(dir))
                                .Take(5);
                            
                            foreach (var subDir in subDirs)
                            {
                                try
                                {
                                    count += EstimateItems(subDir, maxDepth, currentDepth + 1) / 5; // Average estimate
                                }
                                catch
                                {
                                    // If subdirectory fails, add small default
                                    count += 20;
                                }
                            }
                        }
                        catch
                        {
                            // If we can't enumerate subdirectories, just use current count
                        }
                    }
                    
                    return count;
                }
                catch (UnauthorizedAccessException)
                {
                    return 10; // Default estimate for inaccessible directories
                }
                catch (DirectoryNotFoundException)
                {
                    return 0;
                }
                catch (IOException)
                {
                    return 10; // Default estimate for I/O issues
                }
                catch (SecurityException)
                {
                    return 10; // Default estimate for security issues
                }
                catch
                {
                    return 20; // Default estimate for any other exception
                }
            }

            (FileNode ui, FileNodeDigest dg) Walk(string path, int depth)
            {
                ct.ThrowIfCancellationRequested();

                if (depth > options.MaxDepth) { truncated = true; skipped++; return (EmptyDirNode(path), EmptyDigest(path)); }
                if (IsSystemPath(path)) { skipped++; return (EmptyDirNode(path), EmptyDigest(path)); }

                var attr = File.GetAttributes(path);
                bool isDir = attr.HasFlag(FileAttributes.Directory);
                bool isHidden = attr.HasFlag(FileAttributes.Hidden);
                if (isHidden && !options.IncludeHidden) { skipped++; return (EmptyDirNode(path), EmptyDigest(path)); }

                if (!isDir)
                {
                    var fi = new FileInfo(path);
                    if (fi.Length > options.MaxFileSizeBytes) { skipped++; return (EmptyDirNode(path), EmptyDigest(path)); }
                    files++; totalItems++; bytesProcessed += fi.Length;

                    // Report progress periodically during scanning
                    if (totalItems % 10 == 0) // Report every 10 files
                    {
                        ReportProgress(JobStage.Scanning, $"Scanned {files} files");
                    }

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

                    // Report progress periodically during scanning
                    if (totalItems % 5 == 0) // Report every 5 directories
                    {
                        ReportProgress(JobStage.Scanning, $"Scanned {dirs} directories");
                    }

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
                    catch (UnauthorizedAccessException)
                    {
                        errors++;
                        ReportProgress(JobStage.Scanning, $"Skipped (Access Denied): {path}");
                    }
                    catch { /* swallow other errors */ errors++; }

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
        if (string.IsNullOrWhiteSpace(p)) return true;
        
        var path = p.Replace('\\', '/');
        var normalizedPath = p;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Check against known system paths
            if (SystemHintsWindows.Any(h => normalizedPath.StartsWith(h, StringComparison.OrdinalIgnoreCase)))
                return true;
                
            // Additional Windows-specific system path patterns
            if (normalizedPath.Contains("\\$Recycle.Bin") || 
                normalizedPath.Contains("\\System Volume Information") ||
                normalizedPath.Contains("\\Config.Msi") ||
                normalizedPath.Contains("\\inetpub\\") || // IIS directories and subdirs
                normalizedPath.Contains("\\Windows.old\\") || // Old Windows installation
                normalizedPath.Contains("\\Boot\\") || // Boot directories
                normalizedPath.EndsWith("\\DumpStack.log.tmp", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Contains("\\hiberfil.sys") ||
                normalizedPath.Contains("\\pagefile.sys") ||
                normalizedPath.Contains("\\swapfile.sys") ||
                normalizedPath.Contains("\\bootmgr"))
                return true;
                
            // Check for common protected file/folder patterns
            var fileName = Path.GetFileName(normalizedPath);
            if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase) || // Temp Office files
                fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            {
                // Only exclude these if they're in system directories
                var parentDir = Path.GetDirectoryName(normalizedPath);
                if (!string.IsNullOrEmpty(parentDir) && 
                    (parentDir.Contains("\\Windows\\") || parentDir.Contains("\\System32\\")))
                    return true;
            }
        }
        else
        {
            return SystemHintsUnix.Any(h => path.StartsWith(h, StringComparison.OrdinalIgnoreCase));
        }
        
        return false;
    }
}
