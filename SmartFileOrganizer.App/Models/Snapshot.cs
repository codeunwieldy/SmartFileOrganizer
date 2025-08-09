namespace SmartFileOrganizer.App.Models;

public class Snapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Created { get; set; } = DateTime.UtcNow;

    // Map dest->source to revert quickly
    public List<(string From, string To)> ReverseMoves { get; set; } = new();

    public List<string> CreatedDirectories { get; set; } = new();

    public List<string> CreatedHardlinks { get; set; } = new();
}