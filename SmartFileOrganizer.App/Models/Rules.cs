namespace SmartFileOrganizer.App.Models;

public enum RuleActionKind { MoveToFolder, Ignore }

public class Rule
{
    public string Name { get; set; } = "New Rule";
    public string Pattern { get; set; } = "*.pdf";            // glob on filename or full path
    public bool MatchFullPath { get; set; } = false;          // false = filename only
    public RuleActionKind Action { get; set; } = RuleActionKind.MoveToFolder;
    public string? DestinationFolder { get; set; }            // required when MoveToFolder
    public bool GroupByYear { get; set; } = false;            // optional: yyyy/
    public bool GroupByYearMonth { get; set; } = false;       // optional: yyyy/yyyy-MM/
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;                  // lower runs first

    public List<string> Scopes { get; set; } = new();
}

public class RuleSet
{
    public List<Rule> Rules { get; set; } = new();
}

public class RuleEvaluation
{
    public List<MoveOp> Moves { get; } = new();
    public HashSet<string> ClaimedSources { get; } = new(StringComparer.OrdinalIgnoreCase); // sources AI should NOT plan
    public List<string> IgnoredSources { get; } = new();
}
