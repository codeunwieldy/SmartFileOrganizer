using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartFileOrganizer.App.Models;
using SmartFileOrganizer.App.Services;
using System.Collections.ObjectModel;

namespace SmartFileOrganizer.App.ViewModels;

public partial class AdvancedPlannerViewModel : ObservableObject
{
    private readonly Plan _plan;
    private readonly FileNode _actualRoot;
    private readonly string _allowedRoot;

    public ObservableCollection<CurrentTreeNode> CurrentRoot { get; } = new();
    public ObservableCollection<PlanTreeNode> DestinationRoot { get; } = new();

    [ObservableProperty] private string currentBreadcrumb = "";
    [ObservableProperty] private string destinationBreadcrumb = "";

    public AdvancedPlannerViewModel(Plan plan, FileNode actualRoot, string allowedRoot)
    {
        _plan = plan;
        _actualRoot = actualRoot;
        _allowedRoot = allowedRoot;
        BuildCurrentTree();
        BuildDestinationTree();
    }

    private void BuildCurrentTree()
    {
        CurrentRoot.Clear();
        CurrentRoot.Add(BuildCurrent(_actualRoot));
    }

    private CurrentTreeNode BuildCurrent(FileNode n)
    {
        var node = new CurrentTreeNode
        {
            Name = n.Name,
            FullPath = n.Path,
            IsFolder = n.Children.Count > 0 || n.Path.EndsWith(Path.DirectorySeparatorChar) || Directory.Exists(n.Path)
        };
        foreach (var c in n.Children)
            node.Children.Add(BuildCurrent(c));
        node.FileCount = node.Children.Count(x => !x.IsFolder);
        node.FolderCount = node.Children.Count(x => x.IsFolder);
        return node;
    }

    private void BuildDestinationTree()
    {
        DestinationRoot.Clear();
        foreach (var move in _plan.Moves)
        {
            var destDir = Path.GetDirectoryName(move.Destination) ?? "";
            var fileName = Path.GetFileName(move.Destination);
            var folderNode = EnsureFolderPath(DestinationRoot, destDir);
            var fileNode = new PlanTreeNode
            {
                Name = "📄 " + fileName,
                FullPath = move.Destination,
                IsFolder = false,
                BoundMove = move
            };
            folderNode.Children.Add(fileNode);
            folderNode.FileCount++;
        }
    }

    private PlanTreeNode EnsureFolderPath(ObservableCollection<PlanTreeNode> roots, string destDir)
    {
        if (string.IsNullOrWhiteSpace(destDir))
        {
            var r = roots.FirstOrDefault(n => n.FullPath == "");
            if (r is null)
            {
                r = new PlanTreeNode { Name = "🗂️ (root)", FullPath = "", IsFolder = true };
                roots.Add(r);
            }
            return r;
        }

        var parts = destDir.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string currentPath = destDir.StartsWith('/') ? "/" : (Path.IsPathRooted(destDir) ? Path.GetPathRoot(destDir)! : "");
        ObservableCollection<PlanTreeNode> cursor = roots;
        PlanTreeNode? last = null;
        foreach (var p in parts)
        {
            currentPath = string.IsNullOrEmpty(currentPath) ? p : Path.Combine(currentPath, p);
            var existing = cursor.FirstOrDefault(n => n.IsFolder && string.Equals(n.Name.Replace("🗂️", "").TrimStart(' '), p, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existing = new PlanTreeNode { Name = "🗂️ " + p, FullPath = currentPath, IsFolder = true };
                cursor.Add(existing);
            }
            last = existing;
            cursor = existing.Children;
        }
        return last ?? new PlanTreeNode { Name = "🗂️ (root)", FullPath = "", IsFolder = true };
    }

    // Update breadcrumbs from UI taps
    [RelayCommand] public void SelectCurrent(string path) => CurrentBreadcrumb = path;

    [RelayCommand] public void SelectDestination(string path) => DestinationBreadcrumb = path;

    // Drag from Current -> drop on Destination folder: create/update move
    [RelayCommand]
    public void HandleExternalDrop((string draggedPath, string targetFolderPath) args)
    {
        var (sourcePath, targetFolder) = args;
        if (PathGuards.IsSystemPath(sourcePath) || PathGuards.IsSystemPath(targetFolder)) return;
        if (!PathGuards.IsUnderRoot(sourcePath, _allowedRoot)) return;
        if (!PathGuards.IsUnderRoot(targetFolder, _allowedRoot)) return;

        var fileName = Path.GetFileName(sourcePath);
        var newDest = Path.Combine(targetFolder, fileName);

        // Find existing move for this source; update or add
        var i = _plan.Moves.FindIndex(m => string.Equals(m.Source, sourcePath, StringComparison.OrdinalIgnoreCase));
        MoveOp updated;
        if (i >= 0)
        {
            updated = new MoveOp(sourcePath, newDest);
            _plan.Moves[i] = updated;
        }
        else
        {
            updated = new MoveOp(sourcePath, newDest);
            _plan.Moves.Add(updated);
        }

        // Reflect in destination tree
        var parent = EnsureFolderPath(DestinationRoot, targetFolder);
        parent.Children.Add(new PlanTreeNode { Name = "📄 " + fileName, FullPath = newDest, IsFolder = false, BoundMove = updated });
        parent.FileCount++;
    }

    // Drag a file within destination tree to another folder (re-parent)
    [RelayCommand]
    public void HandleInternalDrop((string draggedDestPath, string targetFolderPath) args)
    {
        var (draggedPath, targetFolder) = args;
        if (PathGuards.IsSystemPath(targetFolder)) return;
        if (!PathGuards.IsUnderRoot(targetFolder, _allowedRoot)) return;

        // Locate node & parent
        var (parent, node) = FindNodeAndParent(DestinationRoot, draggedPath);
        if (node is null || node.IsFolder) return;

        parent?.Children.Remove(node);
        var fileName = node.Name.Replace("📄 ", "").Trim();
        var newDest = Path.Combine(targetFolder, fileName);

        // Update move
        var idx = _plan.Moves.FindIndex(m => string.Equals(m.Destination, draggedPath, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            var src = _plan.Moves[idx].Source;
            var updated = new MoveOp(src, newDest);
            _plan.Moves[idx] = updated;
            node.BoundMove = updated;
            node.FullPath = newDest;
        }

        var targetNode = EnsureFolderPath(DestinationRoot, targetFolder);
        targetNode.Children.Add(node);
        targetNode.FileCount++;
    }

    private (PlanTreeNode? parent, PlanTreeNode? node) FindNodeAndParent(ObservableCollection<PlanTreeNode> nodes, string fullPath, PlanTreeNode? parent = null)
    {
        foreach (var n in nodes.ToList())
        {
            if (n.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                return (parent, n);
            var (p, found) = FindNodeAndParent(n.Children, fullPath, n);
            if (found is not null) return (p, found);
        }
        return (null, null);
    }

    public Plan GetEditedPlan() => _plan;
}