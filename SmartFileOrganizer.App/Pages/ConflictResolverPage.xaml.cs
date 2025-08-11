using SmartFileOrganizer.App.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ConflictChoice = SmartFileOrganizer.App.Services.IExecutorService.ConflictChoice;

namespace SmartFileOrganizer.App.Pages;

public partial class ConflictResolverPage : ContentPage
{
    public class Row
    {
        public string Destination { get; init; } = "";
        public string Reason { get; init; } = "";
        public ConflictChoice Choice { get; set; } = ConflictChoice.Rename;
        public string? NewDestinationIfRename { get; set; }
    }

    public ObservableCollection<Row> Items { get; } = new();

    private Row? _selectedRow;

    public Row? SelectedRow
    {
        get => _selectedRow;
        set
        {
            _selectedRow = value;
            OnPropertyChanged();
        }
    }

    public TaskCompletionSource<List<IExecutorService.ConflictResolution>>? ResolveTask { get; set; }

    public ICommand ApplyCommand { get; }

    public ConflictResolverPage(IEnumerable<IExecutorService.Conflict> conflicts)
    {
        InitializeComponent();
        BindingContext = this;

        foreach (var c in conflicts)
            Items.Add(new Row { Destination = c.Destination, Reason = c.Reason });

        SelectedRow = Items.FirstOrDefault();

        ApplyCommand = new Command(async () =>
        {
            var results = Items
                .Select(i => new IExecutorService.ConflictResolution(
                    i.Destination,
                    i.Choice switch
                    {
                        ConflictChoice.Skip => IExecutorService.ConflictChoice.Skip,
                        ConflictChoice.Rename => IExecutorService.ConflictChoice.Rename,
                        ConflictChoice.Overwrite => IExecutorService.ConflictChoice.Overwrite,
                        _ => IExecutorService.ConflictChoice.Rename
                    },
                    i.NewDestinationIfRename))
                .ToList();

            await Navigation.PopAsync();
            ResolveTask?.TrySetResult(results);
        });
    }
}

/// <summary>
/// Converts ConflictChoice &lt;=&gt; Picker.SelectedIndex (Skip=0, Rename=1, Overwrite=2)
/// </summary>
public sealed class ConflictChoiceToIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ConflictChoice c ? c switch
        {
            ConflictChoice.Skip => 0,
            ConflictChoice.Rename => 1,
            ConflictChoice.Overwrite => 2,
            _ => 1
        } : 1;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is int i) ? i switch
        {
            0 => ConflictChoice.Skip,
            1 => ConflictChoice.Rename,
            2 => ConflictChoice.Overwrite,
            _ => ConflictChoice.Rename
        } : ConflictChoice.Rename;
}