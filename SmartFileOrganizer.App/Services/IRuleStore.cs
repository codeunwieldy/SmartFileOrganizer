using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Services;

public interface IRuleStore
{
    Task<RuleSet> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(RuleSet set, CancellationToken ct = default);
}