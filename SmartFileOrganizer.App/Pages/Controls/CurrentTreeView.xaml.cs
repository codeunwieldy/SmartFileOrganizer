using SmartFileOrganizer.App.Models;
using SmartFileOrganizer.App.ViewModels;

namespace SmartFileOrganizer.App.Pages.Controls;

public partial class CurrentTreeView : ContentView
{
    public CurrentTreeView() => InitializeComponent();

    private void OnTapped(object sender, TappedEventArgs e)
    {
        if (BindingContext is not AdvancedPlannerViewModel vm) return;
        if (sender is not Element el) return;
        if (el.BindingContext is not CurrentTreeNode node) return;

        // If you add a command (below), use it; otherwise call a method
        vm.SelectCurrentCommand?.Execute(node.FullPath);
        // or: vm.SelectCurrent(node.FullPath);
    }

    private void OnDragStarting(object sender, DragStartingEventArgs e)
    {
        if (sender is not Element el) return;
        if (el.BindingContext is not CurrentTreeNode node) return;

        if (!node.IsFolder) // allow dragging files only
            e.Data.Properties["sourcePath"] = node.FullPath;
        else
            e.Cancel = true;
    }

    private void OnDrop(object sender, DropEventArgs e)
    {
        // TODO: handle drop if you need it
    }
}