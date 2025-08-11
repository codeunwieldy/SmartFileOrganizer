namespace SmartFileOrganizer.App.Models;

public sealed class DuplicateGroup
{
    public string Hash { get; init; } = "";
    public long SizeBytes { get; init; }
    public List<string> Paths { get; } = new(); // get-only, but the list itself is mutable
}