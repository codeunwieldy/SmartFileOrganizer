using SmartFileOrganizer.App.Services;
using SmartFileOrganizer.App.ViewModels;
using SmartFileOrganizer.App.Models;
using System.Collections.Specialized;

namespace SmartFileOrganizer.App.Pages;

public partial class MainPage : ContentPage
{


    public MainPage(MainViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        // Auto-scroll the progress list as lines are added
        vm.ProgressLines.CollectionChanged += ProgressLines_CollectionChanged;
    }

    private void ProgressLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (BindingContext is not MainViewModel vm) return;
        if (vm.ProgressLines.Count == 0) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                ProgressList.ScrollTo(vm.ProgressLines.Count - 1,
                                      position: ScrollToPosition.End,
                                      animate: true);
            }
            catch { /* layout race; ignore */ }
        });
    }

    private void OnPrefChanged(object sender, ToggledEventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            var mi = typeof(MainViewModel).GetMethod("SavePrefs",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            mi?.Invoke(vm, null);
        }
    }

    // Tree view zoom and navigation controls (Legacy - now handled by commands)
    private void OnZoomIn(object sender, EventArgs e)
    {
        try
        {
            if (BindingContext is MainViewModel vm)
            {
                vm.ZoomTreeCommand?.Execute(0.2);
            }
        }
        catch { /* ignore */ }
    }

    private void OnZoomOut(object sender, EventArgs e)
    {
        try
        {
            if (BindingContext is MainViewModel vm)
            {
                vm.ZoomTreeCommand?.Execute(-0.2);
            }
        }
        catch { /* ignore */ }
    }

    private void OnResetZoom(object sender, EventArgs e)
    {
        try
        {
            if (BindingContext is MainViewModel vm)
            {
                vm.ResetTreeViewCommand?.Execute(null);
            }
        }
        catch { /* ignore */ }
    }

    private void OnExpandAll(object sender, EventArgs e)
    {
        // Expand all nodes in the tree
        try
        {
            if (BindingContext is MainViewModel vm)
            {
                ExpandAllNodes(vm.CurrentRoot);
                vm.RefreshTreeView();
            }
        }
        catch { /* ignore */ }
    }

    private void OnCollapseAll(object sender, EventArgs e)
    {
        // Collapse all nodes in the tree
        try
        {
            if (BindingContext is MainViewModel vm)
            {
                CollapseAllNodes(vm.CurrentRoot);
                vm.RefreshTreeView();
            }
        }
        catch { /* ignore */ }
    }

    private void ExpandAllNodes(IEnumerable<CurrentTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsFolder)
            {
                node.IsExpanded = true;
                ExpandAllNodes(node.Children);
            }
        }
    }

    private void CollapseAllNodes(IEnumerable<CurrentTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsFolder)
            {
                node.IsExpanded = false;
                CollapseAllNodes(node.Children);
            }
        }
    }

    // Interactive tree gesture handlers
    private void OnTreeTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (BindingContext is MainViewModel vm && sender is GraphicsView graphicsView)
            {
                var position = e.GetPosition(graphicsView);
                if (position.HasValue)
                {
                    // Calculate which node was tapped based on position
                    var tappedNode = GetNodeAtPosition(position.Value, vm.CurrentRoot);
                    if (tappedNode != null)
                    {
                        vm.SelectTreeNodeCommand?.Execute(tappedNode);
                    }
                }
            }
        }
        catch { /* ignore */ }
    }

    private void OnTreePanned(object sender, PanUpdatedEventArgs e)
    {
        try
        {
            if (BindingContext is MainViewModel vm)
            {
                if (e.StatusType == GestureStatus.Running)
                {
                    var delta = new Microsoft.Maui.Graphics.Point(e.TotalX, e.TotalY);
                    vm.PanTreeCommand?.Execute(delta);
                }
            }
        }
        catch { /* ignore */ }
    }

    private void OnTreePinched(object sender, PinchGestureUpdatedEventArgs e)
    {
        try
        {
            if (BindingContext is MainViewModel vm)
            {
                if (e.Status == GestureStatus.Running)
                {
                    var zoomDelta = (e.Scale - 1.0) * 0.1; // Scale the zoom delta
                    vm.ZoomTreeCommand?.Execute(zoomDelta);
                }
            }
        }
        catch { /* ignore */ }
    }

    private CurrentTreeNode? GetNodeAtPosition(Microsoft.Maui.Graphics.Point position, IEnumerable<CurrentTreeNode> nodes)
    {
        // Simplified node hit testing - in a real implementation you'd need to track node positions
        const float nodeHeight = 24f;
        
        int nodeIndex = (int)((position.Y - 10) / nodeHeight);
        var flattenedNodes = FlattenNodes(nodes).ToList();
        
        if (nodeIndex >= 0 && nodeIndex < flattenedNodes.Count)
        {
            return flattenedNodes[nodeIndex];
        }
        
        return null;
    }

    private IEnumerable<CurrentTreeNode> FlattenNodes(IEnumerable<CurrentTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node.IsFolder && node.IsExpanded)
            {
                foreach (var child in FlattenNodes(node.Children))
                {
                    yield return child;
                }
            }
        }
    }
}
