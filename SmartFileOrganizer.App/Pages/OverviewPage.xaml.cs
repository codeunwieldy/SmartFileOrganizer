using Microcharts;
using SkiaSharp;
using SmartFileOrganizer.App.Services;
using static System.Net.Mime.MediaTypeNames;
#if WINDOWS
using System.Diagnostics;
#endif

namespace SmartFileOrganizer.App.Pages;

public partial class OverviewPage : ContentPage
{
    private readonly OverviewResult _data;
    private readonly string _exampleFolderToOpen;

    public OverviewPage(OverviewResult data, string? exampleOpenFolder)
    {
        InitializeComponent();
        _data = data;
        _exampleFolderToOpen = exampleOpenFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        BuildDonut();
        BuildTree();
    }

    private void BuildDonut()
    {
        var entries = _data.Categories.Select((c, i) =>
            new ChartEntry(c.Count)
            {
                Label = c.Name,
                ValueLabel = c.Count.ToString(),
                // Color left default; Microcharts will auto-pick
            }).ToList();

        Donut.Chart = new DonutChart
        {
            Entries = entries,
            HoleRadius = 0.6f,
            LabelTextSize = 28
        };
    }

    private void BuildTree()
    {
        // Flatten to a simple list with indent text (quick and readable)
        var items = new List<(int depth, string text)>();
        void Walk(OverviewNode n, int d)
        {
            items.Add((d, $"{new string('·', d)} {n.Name} {(n.Files > 0 ? $"({n.Files})" : "")}".Trim()));
            foreach (var c in n.Children.OrderByDescending(x => x.Files))
                Walk(c, d + 1);
        }
        Walk(_data.DestinationTree, 0);

        Tree.ItemsSource = items.Select(i => new { Text = i.text });
        Tree.ItemTemplate = new DataTemplate(() =>
        {
            var lbl = new Label { FontSize = 14 };
            lbl.SetBinding(Label.TextProperty, "Text");
            return new ViewCell { View = lbl };
        });
    }

    private async void OnOpenFolder(object sender, EventArgs e)
    {
        try
        {
#if WINDOWS
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_exampleFolderToOpen}\"") { UseShellExecute = true });
#elif MACCATALYST
            UIKit.UIApplication.SharedApplication.OpenUrl(new Foundation.NSUrl(_exampleFolderToOpen, true));
#else
            await DisplayAlert("Open", _exampleFolderToOpen, "OK");
#endif
        }
        catch (Exception ex)
        {
            await DisplayAlert("Open failed", ex.Message, "OK");
        }
    }
}

