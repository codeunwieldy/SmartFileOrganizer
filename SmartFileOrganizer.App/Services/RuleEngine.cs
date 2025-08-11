using SmartFileOrganizer.App.Models;
using System.Text.RegularExpressions;

namespace SmartFileOrganizer.App.Services;

public class RuleEngine : IRuleEngine
{
    public RuleEvaluation Evaluate(RuleSet rules, FileNode root)
    {
        var eval = new RuleEvaluation();
        var ordered = rules.Rules.Where(r => r.Enabled).OrderBy(r => r.Priority).ToList();
        Walk(root, ordered, eval);
        return eval;
    }

    private void Walk(FileNode node, List<Rule> rules, RuleEvaluation eval)
    {
        foreach (var c in node.Children)
        {
            if (c.IsDirectory) { Walk(c, rules, eval); continue; }

            foreach (var rule in rules)
            {
                // NEW: scope check (empty scopes = global)
                if (rule.Scopes is { Count: > 0 } &&
                    !rule.Scopes.Any(scope => IsUnder(c.Path, scope)))
                    continue;

                if (!IsMatch(c, rule)) continue;

                if (rule.Action == RuleActionKind.Ignore)
                {
                    eval.IgnoredSources.Add(c.Path);
                    eval.ClaimedSources.Add(c.Path);
                }
                else if (!string.IsNullOrWhiteSpace(rule.DestinationFolder))
                {
                    var destDir = rule.DestinationFolder!;
                    var created = DateTime.SpecifyKind(c.CreatedUtc, DateTimeKind.Utc);
                    if (rule.GroupByYearMonth)
                        destDir = Path.Combine(destDir, $"{created:yyyy}", $"{created:yyyy-MM}");
                    else if (rule.GroupByYear)
                        destDir = Path.Combine(destDir, $"{created:yyyy}");

                    var dest = Path.Combine(destDir, c.Name);
                    eval.Moves.Add(new MoveOp(c.Path, dest));
                    eval.ClaimedSources.Add(c.Path);
                }
                break; // first matching rule wins
            }
        }
    }

    private static bool IsUnder(string path, string root)
    {
        try
        {
            var p = Path.GetFullPath(path);
            var r = Path.GetFullPath(root);
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsMatch(FileNode file, Rule rule)
    {
        var input = rule.MatchFullPath ? file.Path : file.Name;
        var regex = GlobToRegex(rule.Pattern);
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase);
    }

    private static string GlobToRegex(string pattern)
    {
        // Very small glob -> regex converter: * ? and ** for directories
        var p = Regex.Escape(pattern)
            .Replace(@"\*\*", "§§DOUBLESTAR§§")
            .Replace(@"\*", "[^/\\\\]*")
            .Replace(@"\?", ".")
            .Replace("§§DOUBLESTAR§§", ".*");
        return $"^{p}$";
    }
}