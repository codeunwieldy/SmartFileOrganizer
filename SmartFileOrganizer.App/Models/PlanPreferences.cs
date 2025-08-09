namespace SmartFileOrganizer.App.Models;

public class PlanPreferences
{
    public bool GroupByType { get; set; } = true;     // Images/Docs/Archives/Code/etc.
    public bool GroupByDate { get; set; } = false;    // YYYY/YYYY-MM subfolders
    public bool GroupByProject { get; set; } = false; // Infer project from filenames/paths
    public bool KeepFolderNames { get; set; } = true; // Preserve existing parent names when sensible
    public bool FlattenSmallFolders { get; set; } = true; // Merge folders with few files
}

