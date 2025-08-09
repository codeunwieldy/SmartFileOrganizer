namespace SmartFileOrganizer.App.Models;

public class FileNode
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<FileNode> Children { get; set; } = new();
}