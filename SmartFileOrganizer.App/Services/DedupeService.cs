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
            foreach (var path in EnumerateFilesSafe(root))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(path);
                    files.Add(fi);
                    totalBytes += fi.Length;
                    enumeratedFiles++;
                    ReportProgress(JobStage.Estimating, $"Found {enumeratedFiles} files...");
                }
                catch (Exception ex)
                {
                    errors++;
                    ReportProgress(JobStage.Estimating, $"Error enumerating {path}: {ex.Message}");
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
        var q = new Queue<string>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var dir = q.Dequeue();
            string[] subs = Array.Empty<string>(), files = Array.Empty<string>();
            
            try 
            { 
                subs = Directory.GetDirectories(dir); 
            } 
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                // Directory was deleted while we were scanning
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
            catch
            {
                // Any other exception, skip this directory
                continue;
            }
            
            try 
            { 
                files = Directory.GetFiles(dir); 
            } 
            catch (UnauthorizedAccessException)
            {
                // Skip files we don't have access to, but still process subdirectories
            }
            catch (DirectoryNotFoundException)
            {
                // Directory was deleted while we were scanning
                continue;
            }
            catch (IOException)
            {
                // Network issues, device not ready, etc.
            }
            catch (System.Security.SecurityException)
            {
                // Security restrictions
            }
            catch (NotSupportedException)
            {
                // Invalid path format
            }
            catch
            {
                // Any other exception for files, continue with subdirectories
            }
            
            foreach (var s in subs) 
            {
                // Additional check to avoid known problematic paths
                if (!IsSystemPath(s))
                {
                    q.Enqueue(s);
                }
            }
            
            foreach (var f in files) 
            {
                if (!string.IsNullOrEmpty(f) && !IsSystemPath(f))
                {
                    yield return f;
                }
            }
        }
    }

    private static bool IsSystemPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        
        var normalizedPath = path.ToLowerInvariant();
        
        // Windows system paths that commonly cause access issues
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
            @"c:\documents and settings"
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
            normalizedPath.EndsWith(@"\dumpstak.log.tmp") ||
            normalizedPath.Contains(@"\hiberfil.sys") ||
            normalizedPath.Contains(@"\pagefile.sys") ||
            normalizedPath.Contains(@"\swapfile.sys"))
        {
            return true;
        }
        
        return false;
    }
}