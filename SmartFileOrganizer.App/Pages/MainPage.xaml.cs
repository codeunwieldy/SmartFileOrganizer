using SmartFileOrganizer.App.Services;
using SmartFileOrganizer.App.ViewModels;
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
}
