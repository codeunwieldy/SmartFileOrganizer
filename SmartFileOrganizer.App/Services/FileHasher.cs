using System.Security.Cryptography;

namespace SmartFileOrganizer.App.Services;

public class FileHasher : IHashingService
{
    public async Task<string> HashFileAsync(string path, int partialBytes = 0, CancellationToken ct = default)
    {
        using var sha = SHA256.Create();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, useAsync: true);
        if (partialBytes > 0)
        {
            var buf = new byte[partialBytes];
            var read = await fs.ReadAsync(buf.AsMemory(0, partialBytes), ct);
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
        return Convert.ToHexString(sha.Hash!);
    }
}