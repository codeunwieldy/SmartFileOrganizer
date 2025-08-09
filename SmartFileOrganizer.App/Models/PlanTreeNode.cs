using System.Collections.ObjectModel;

namespace SmartFileOrganizer.App.Models;

public class PlanTreeNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsFolder { get; set; }
    public ObservableCollection<PlanTreeNode> Children { get; set; } = new();
    public MoveOp? BoundMove { get; set; } // if a file node corresponds to a move
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
}