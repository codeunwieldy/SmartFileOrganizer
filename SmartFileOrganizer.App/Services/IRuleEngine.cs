using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Services;

public interface IRuleEngine
{
    RuleEvaluation Evaluate(RuleSet rules, FileNode root);
}