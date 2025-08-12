using SmartFileOrganizer.App.Models;
using System.Diagnostics;

namespace SmartFileOrganizer.App.Services;

public class DedupeService : IDedupeService
{
    private readonly IHashingService _hasher;

    public DedupeService(IHashingService hasher) => _hasher = hasher;

    public async Task<List<DuplicateGroup>> FindDuplicatesAsync(IEnumerable<string> roots, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var lastReportTime = DateTime.MinValue;
        int errors = 0;
        int skipped = 0;

        // 1) collect files -> bucket by size
        var files = new List<FileInfo>();
        long totalBytes = 0;
        int enumeratedFiles = 0;

        void ReportProgress(JobStage stage, string? message = null)
        {
            if (progress == null) return;

            // Throttle updates to avoid excessive reporting
            if ((DateTime.Now - lastReportTime).TotalMilliseconds < 100 && enumeratedFiles < files.Count)
            {
                return;
            }
            lastReportTime = DateTime.Now;

            var stageProgress = files.Count > 0 ? (double)enumeratedFiles / files.Count : 1.0;
            var throughput = stopwatch.Elapsed.TotalSeconds > 0 ? enumeratedFiles / stopwatch.Elapsed.TotalSeconds : 0;
            TimeSpan? eta = null;
            if (throughput > 0 && enumeratedFiles < files.Count)
            {
                eta = TimeSpan.FromSeconds((files.Count - enumeratedFiles) / throughput);
            }

            progress.Report(new ScanProgress
            {
                Stage = stage,
                StageProgress = stageProgress,
                FilesProcessed = enumeratedFiles,
                FilesTotal = files.Count,
                BytesProcessed = totalBytes, // This will be total bytes of files enumerated so far
                BytesTotal = totalBytes, // This will be total bytes of files enumerated so far
                ActionsDone = enumeratedFiles, // Using enumeratedFiles as ActionsDone for this stage
                ActionsTotal = files.Count, // Using files.Count as ActionsTotal for this stage
                Throughput = throughput,
                Eta = eta,
                Message = message,
                Errors = errors,
                Skipped = skipped
            });
        }

        ReportProgress(JobStage.Estimating, "Enumerating files for deduplication...");

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) { skipped++; continue; }
            
            // Pre-check root access before processing
            try
            {
                // Test basic access by trying to enumerate just the top level
                _ = Directory.GetDirectories(root);
                _ = Directory.GetFiles(root);
            }
            catch (UnauthorizedAccessException)
            {
                skipped++;
                ReportProgress(JobStage.Estimating, $"Skipping root (access denied): {root}");
                continue;
            }
            catch (Exception ex)
            {
                errors++;
                ReportProgress(JobStage.Estimating, $"Skipping root (error): {root} - {ex.Message}");
                continue;
            }

            foreach (var path in EnumerateFilesSafe(root))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(path);
                    
                    // Additional safety checks before adding to list
                    if (fi.Exists && fi.Length > 0 && fi.Length < 1_000_000_000) // Skip files over 1GB
                    {
                        files.Add(fi);
                        totalBytes += fi.Length;
                        enumeratedFiles++;
                        
                        // Only report progress every 100 files to reduce overhead
                        if (enumeratedFiles % 100 == 0)
                        {
                            ReportProgress(JobStage.Estimating, $"Found {enumeratedFiles} files...");
                        }
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    skipped++;
                    // Don't report individual file access issues to avoid spam
                }
                catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))
                {
                    skipped++;
                    // Don't report file-in-use issues to avoid spam
                }
                catch (Exception ex)
                {
                    errors++;
                    // Only report every 10th error to avoid spam
                    if (errors % 10 == 0)
                    {
                        ReportProgress(JobStage.Estimating, $"Errors encountered: {errors}");
                    }
                }
            }
        }

        var totalFilesToProcess = files.Count;
        if (totalFilesToProcess == 0)
        {
            ReportProgress(JobStage.Completed, "No files to deduplicate.");
            return new List<DuplicateGroup>();
        }

        var bySize = files.GroupBy(f => f.Length).Where(g => g.Count() > 1);

        var groups = new List<DuplicateGroup>();
        long processedBytes = 0;
        int processedFiles = 0;

        // Reset stopwatch for actual hashing progress
        stopwatch.Restart();
        enumeratedFiles = 0; // Reset for hashing progress

        foreach (var sizeBucket in bySize)
        {
            ct.ThrowIfCancellationRequested();

            // 2) partial hash (first 256KB) to prune
            var byPartial = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in sizeBucket)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var ph = await _hasher.HashFileAsync(f.FullName, partialBytes: 256 * 1024, ct);
                    (byPartial.TryGetValue(ph, out var list) ? list : byPartial[ph] = new()).Add(f);
                    processedBytes += f.Length; // Accumulate processed bytes
                    processedFiles++;
                    enumeratedFiles++; // Increment for overall progress of hashing
                    ReportProgress(JobStage.Planning, $"Dedup pass (partial hash): {f.Name}");
                }
                catch (Exception ex)
                {
                    errors++;
                    ReportProgress(JobStage.Planning, $"Error partial hashing {f.Name}: {ex.Message}");
                }
            }

            // 3) full hash within partial-collisions
            foreach (var partial in byPartial.Values.Where(v => v.Count > 1))
            {
                ct.ThrowIfCancellationRequested();
                var byFull = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in partial)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var full = await _hasher.HashFileAsync(f.FullName, partialBytes: 0, ct);
                        (byFull.TryGetValue(full, out var list) ? list : byFull[full] = new()).Add(f);
                        // processedFiles and processedBytes are already incremented by partial hash
                        // enumeratedFiles is also incremented by partial hash
                        ReportProgress(JobStage.Planning, $"Dedup pass (full hash): {f.Name}");
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        ReportProgress(JobStage.Planning, $"Error full hashing {f.Name}: {ex.Message}");
                    }
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
        stopwatch.Stop();
        ReportProgress(JobStage.Completed, "Deduplication complete.");

        return groups;
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        // Pre-filter: Skip obviously problematic root paths immediately
        if (IsSystemPath(root))
        {
            yield break;
        }

        var q = new Queue<string>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var dir = q.Dequeue();
            
            // Double-check each directory before processing
            if (IsSystemPath(dir))
            {
                continue;
            }

            string[] subs = Array.Empty<string>(), files = Array.Empty<string>();
            
            // Handle subdirectories enumeration
            try 
            { 
                subs = Directory.GetDirectories(dir); 
            } 
            catch (UnauthorizedAccessException)
            {
                // Silently skip directories we don't have access to
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                // Directory was deleted while we were scanning
                continue;
            }
            catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020)) // File in use
            {
                // Handle "file in use" specifically
                continue;
            }
            catch (IOException)
            {
                // Network issues, device not ready, etc.
                continue;
            }
            catch (System.Security.SecurityException)
            {
                // Security restrictions
                continue;
            }
            catch (NotSupportedException)
            {
                // Invalid path format
                continue;
            }
            catch (Exception)
            {
                // Any other exception, stop processing this branch
                continue;
            }
            
            // Handle files enumeration
            try 
            { 
                files = Directory.GetFiles(dir); 
            } 
            catch (UnauthorizedAccessException)
            {
                // Skip files we don't have access to, but still process subdirectories
                files = Array.Empty<string>();
            }
            catch (DirectoryNotFoundException)
            {
                // Directory was deleted while we were scanning
                continue;
            }
            catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020)) // File in use
            {
                // Handle "file in use" specifically
                files = Array.Empty<string>();
            }
            catch (IOException)
            {
                // Network issues, device not ready, etc.
                files = Array.Empty<string>();
            }
            catch (System.Security.SecurityException)
            {
                // Security restrictions
                files = Array.Empty<string>();
            }
            catch (NotSupportedException)
            {
                // Invalid path format
                files = Array.Empty<string>();
            }
            catch (Exception)
            {
                // Any other exception for files, continue with subdirectories
                files = Array.Empty<string>();
            }
            
            // Queue subdirectories for processing with additional filtering
            foreach (var s in subs) 
            {
                // Multiple layers of filtering to avoid problematic paths
                if (!IsSystemPath(s) && !IsHiddenOrSystem(s))
                {
                    q.Enqueue(s);
                }
            }
            
            // Yield files with additional safety checks
            foreach (var f in files) 
            {
                if (!string.IsNullOrEmpty(f) && !IsSystemPath(f) && !IsHiddenOrSystem(f))
                {
                    // Additional safety check before yielding file
                    bool canYield = false;
                    try
                    {
                        // Quick existence and access check
                        var fileInfo = new FileInfo(f);
                        if (fileInfo.Exists && 
                            !fileInfo.Attributes.HasFlag(FileAttributes.System) && 
                            !fileInfo.Attributes.HasFlag(FileAttributes.Device) &&
                            fileInfo.Length > 0) // Skip 0-byte files which are often problematic
                        {
                            canYield = true;
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip files we can't access
                        canYield = false;
                    }
                    catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))
                    {
                        // Skip files that are in use
                        canYield = false;
                    }
                    catch (Exception)
                    {
                        // Skip any other problematic files
                        canYield = false;
                    }
                    
                    if (canYield)
                    {
                        yield return f;
                    }
                }
            }
        }
    }

    private static bool IsSystemPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        
        var normalizedPath = path.ToLowerInvariant();
        
        // Expanded list of Windows system paths that commonly cause access issues
        var systemPaths = new[]
        {
            @"c:\windows",
            @"c:\program files",
            @"c:\program files (x86)",
            @"c:\programdata",
            @"c:\system volume information",
            @"c:\$recycle.bin",
            @"c:\config.msi",
            @"c:\recovery",
            @"c:\intel",
            @"c:\perflogs",
            @"c:\inetpub",
            @"c:\windows.old",
            @"c:\boot",
            @"c:\users\all users",
            @"c:\documents and settings",
            @"c:\msocache", // Microsoft Office cache
            @"c:\swapfile.sys", // Page file
            @"c:\hiberfil.sys", // Hibernation file
            @"c:\pagefile.sys", // Virtual memory
            @"c:\$windows.~bt", // Windows Update temp
            @"c:\$windows.~ws", // Windows Update temp
            @"c:\windows10upgrade", // Windows 10 upgrade temp
            @"c:\efi", // EFI system partition
            @"c:\amd", // AMD driver temp files
            @"c:\nvidia", // NVIDIA temp files
            @"c:\temp", // System temp (sometimes protected)
            @"c:\tmp" // System temp (sometimes protected)
        };
        
        foreach (var sysPath in systemPaths)
        {
            if (normalizedPath.StartsWith(sysPath))
                return true;
        }

        // Check for common problematic patterns
        if (normalizedPath.Contains(@"\$recycle.bin") ||
            normalizedPath.Contains(@"\system volume information") ||
            normalizedPath.Contains(@"\config.msi") ||
            normalizedPath.Contains(@"\inetpub\") ||
            normalizedPath.Contains(@"\windows.old\") ||
            normalizedPath.Contains(@"\$windows.~bt") ||
            normalizedPath.Contains(@"\$windows.~ws") ||
            normalizedPath.Contains(@"\windows10upgrade") ||
            normalizedPath.Contains(@"\msocache\") ||
            normalizedPath.Contains(@"\winsxs\") ||
            normalizedPath.Contains(@"\assembly\") ||
            normalizedPath.Contains(@"\microsoft.net\") ||
            normalizedPath.Contains(@"\windows defender\") ||
            normalizedPath.Contains(@"\windowsapps\") ||
            normalizedPath.Contains(@"\application data\") ||
            normalizedPath.EndsWith(@"\dumpstak.log.tmp") ||
            normalizedPath.Contains(@"\hiberfil.sys") ||
            normalizedPath.Contains(@"\pagefile.sys") ||
            normalizedPath.Contains(@"\swapfile.sys") ||
            normalizedPath.Contains(@"\bootmgr") ||
            normalizedPath.Contains(@"\ntuser.dat") ||
            normalizedPath.Contains(@"\thumbs.db") ||
            normalizedPath.Contains(@"\desktop.ini"))
        {
            return true;
        }

        // Check for Visual Studio and development tool paths that can cause access issues
        if (normalizedPath.Contains(@"\.vs\") ||
            normalizedPath.Contains(@"\bin\debug\") ||
            normalizedPath.Contains(@"\bin\release\") ||
            normalizedPath.Contains(@"\obj\") ||
            normalizedPath.Contains(@"\.git\") ||
            normalizedPath.Contains(@"\.svn\") ||
            normalizedPath.Contains(@"\node_modules\") ||
            normalizedPath.Contains(@"\packages\") ||
            normalizedPath.Contains(@"\__pycache__\") ||
            normalizedPath.Contains(@"\.nuget\"))
        {
            return true;
        }

        // Check for temp directories and user-specific protected folders
        if (normalizedPath.Contains(@"\temp\") ||
            normalizedPath.Contains(@"\tmp\") ||
            normalizedPath.Contains(@"\appdata\local\temp\") ||
            normalizedPath.Contains(@"\local settings\temp\") ||
            normalizedPath.Contains(@"\cookies\") ||
            normalizedPath.Contains(@"\recent\") ||
            normalizedPath.Contains(@"\sendto\") ||
            normalizedPath.Contains(@"\nethood\") ||
            normalizedPath.Contains(@"\printhood\"))
        {
            return true;
        }

        return false;
    }

    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return attrs.HasFlag(FileAttributes.Hidden) || 
                   attrs.HasFlag(FileAttributes.System) || 
                   attrs.HasFlag(FileAttributes.Device) ||
                   attrs.HasFlag(FileAttributes.ReparsePoint); // Avoid symlinks/junctions
        }
        catch
        {
            // If we can't get attributes, assume it's problematic
            return true;
        }
    }
}