using SmartFileOrganizer.App.Models;
using SmartFileOrganizer.App.ViewModels;

namespace SmartFileOrganizer.App.Pages.Controls;

public partial class PlanTreeView : ContentView
{
    public PlanTreeView() => InitializeComponent();

    private void OnTapped(object sender, TappedEventArgs e)
    {
        if (BindingContext is not AdvancedPlannerViewModel vm) return;
        if (sender is not Element el) return;

        // Can be folder or file node
        if (el.BindingContext is PlanTreeNode node)
            vm.SelectDestinationCommand?.Execute(node.FullPath);
    }

    private void OnDragStarting(object sender, DragStartingEventArgs e)
    {
        if (sender is not Element el) return;
        if (el.BindingContext is not PlanTreeNode node) return;

        // Allow dragging files within the destination tree (re-parent)
        if (!node.IsFolder)
            e.Data.Properties["draggedDestPath"] = node.FullPath;
        else
            e.Cancel = true;
    }

    private void OnDrop(object sender, DropEventArgs e)
    {
        if (BindingContext is not AdvancedPlannerViewModel vm) return;
        if (sender is not Element el) return;

        // Drop target must be a folder in the destination tree
        var targetFolder = (el.BindingContext as PlanTreeNode) is { IsFolder: true } folder
            ? folder.FullPath
            : null;

        if (string.IsNullOrWhiteSpace(targetFolder))
            return;

        // Internal re-parent within destination tree
        if (e.Data.Properties.TryGetValue("draggedDestPath", out var dragged) && dragged is string draggedDestPath)
        {
            vm.HandleInternalDropCommand?.Execute((draggedDestPath, targetFolder));
            return;
        }

        // External drop from CurrentTreeView (dragged file source)
        if (e.Data.Properties.TryGetValue("sourcePath", out var source) && source is string sourcePath)
        {
            vm.HandleExternalDropCommand?.Execute((sourcePath, targetFolder));
        }
    }
}
