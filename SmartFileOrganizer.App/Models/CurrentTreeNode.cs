using System.Collections.ObjectModel;

namespace SmartFileOrganizer.App.Models;

public class CurrentTreeNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsFolder { get; set; }
    public bool IsExpanded { get; set; } = false;
    public ObservableCollection<CurrentTreeNode> Children { get; set; } = new();
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
}