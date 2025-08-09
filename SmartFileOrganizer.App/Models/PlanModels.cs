namespace SmartFileOrganizer.App.Models;

public record MoveOp(string Source, string Destination);
public record HardlinkOp(string LinkPath, string TargetExistingPath);

public class Plan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ScopeDescription { get; set; } = "";
    public List<MoveOp> Moves { get; set; } = new();
    public List<string> DeleteEmptyDirectories { get; set; } = new();
    public List<HardlinkOp> Hardlinks { get; set; } = new();
}