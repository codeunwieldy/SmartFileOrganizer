using System.Security.Cryptography;

namespace SmartFileOrganizer.App.Services;

public class FileHasher : IHashingService
{
    public async Task<string> HashFileAsync(string path, int partialBytes = 0, CancellationToken ct = default)
    {
        try
        {
            // Check if file exists and is accessible before trying to open it
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}");

            using var sha = SHA256.Create();
            
            // Use more defensive file opening with better error handling
            FileStream? fs = null;
            try
            {
                fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 64, useAsync: true);
            }
            catch (UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException($"Access denied to file: {Path.GetFileName(path)}");
            }
            catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020)) // File in use
            {
                throw new IOException($"File is in use by another process: {Path.GetFileName(path)}");
            }
            catch (IOException ex)
            {
                throw new IOException($"I/O error accessing file: {Path.GetFileName(path)} - {ex.Message}");
            }
            
            await using (fs)
            {
                if (partialBytes > 0)
                {
                    var buf = new byte[Math.Min(partialBytes, (int)fs.Length)];
                    var read = await fs.ReadAsync(buf.AsMemory(0, buf.Length), ct);
                    sha.TransformFinalBlock(buf, 0, read);
                }
                else
                {
                    var buf = new byte[1024 * 128];
                    int read;
                    while ((read = await fs.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        sha.TransformBlock(buf, 0, read, null, 0);
                    }
                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                }
            }
            
            return Convert.ToHexString(sha.Hash!);
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation
        }
        catch (UnauthorizedAccessException)
        {
            throw; // Re-throw with our custom message
        }
        catch (IOException)
        {
            throw; // Re-throw with our custom message
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unexpected error hashing file {Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }
}