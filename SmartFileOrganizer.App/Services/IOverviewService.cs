using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Services;

public record OverviewCategory(string Name, int Count);
public record class OverviewNode(string Name, List<OverviewNode> Children)
{
    public int Files { get; set; }
}
public record OverviewResult(List<OverviewCategory> Categories, OverviewNode DestinationTree);

public interface IOverviewService
{
    OverviewResult Build(Plan executedPlan);
}