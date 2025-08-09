namespace SmartFileOrganizer.App.Services;
public interface IHashingService
{
    // Returns SHA-256 hex; partialBytes=0 means full file
    Task<string> HashFileAsync(string path, int partialBytes = 0, CancellationToken ct = default);
}

