using SmartFileOrganizer.App.Models;

namespace SmartFileOrganizer.App.Pages.Controls;

public partial class CurrentTreeView : ContentView
{
    public CurrentTreeView() => InitializeComponent();

    // Drag files out of the current tree (folders are not draggable)
    private void OnDragStarting(object sender, DragStartingEventArgs e)
    {
        if (sender is not Element el) return;
        if (el.BindingContext is not CurrentTreeNode node) return;

        if (!node.IsFolder)
            e.Data.Properties["sourcePath"] = node.FullPath;
        else
            e.Cancel = true;
    }

    private void OnDrop(object sender, DropEventArgs e)
    {
        // No-op for now (we only drag OUT of the current tree).
    }
}