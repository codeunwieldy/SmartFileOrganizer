using SmartFileOrganizer.App.Models;
using SmartFileOrganizer.App.ViewModels;

namespace SmartFileOrganizer.App.Pages.Controls;

public partial class PlanTreeView : ContentView
{
    public PlanTreeView() => InitializeComponent();

    // Drag files WITHIN the destination tree (re-parent)
    private void OnDragStarting(object sender, DragStartingEventArgs e)
    {
        if (sender is not Element el) return;
        if (el.BindingContext is not PlanTreeNode node) return;

        if (!node.IsFolder)
            e.Data.Properties["draggedDestPath"] = node.FullPath;
        else
            e.Cancel = true;
    }

    private void OnDrop(object sender, DropEventArgs e)
    {
        if (BindingContext is not AdvancedPlannerViewModel vm) return;
        if (sender is not Element el) return;

        // Accept drops only on folders
        if ((el.BindingContext as PlanTreeNode) is not { IsFolder: true } folder) return;
        var targetFolder = folder.FullPath;

        // Re-parent within the destination tree
        if (e.Data.Properties.TryGetValue("draggedDestPath", out var dragged) && dragged is string draggedDestPath)
        {
            vm.HandleInternalDropCommand?.Execute((draggedDestPath, targetFolder));
            return;
        }

        // External drop from CurrentTreeView
        if (e.Data.Properties.TryGetValue("sourcePath", out var source) && source is string sourcePath)
        {
            vm.HandleExternalDropCommand?.Execute((sourcePath, targetFolder));
        }
    }
}
