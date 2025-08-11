using SmartFileOrganizer.App.Services;

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
        var slices = _data.Categories
            .Select(c => new DonutSlice(c.Name, c.Count))
            .ToList();

        Donut.Drawable = new DonutDrawable(slices);
        Donut.Invalidate();
    }

    private void BuildTree()
    {
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
            return lbl; // CollectionView expects a View
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

// -------- graphics-only donut --------

internal readonly record struct DonutSlice(string Label, float Value);

internal sealed class DonutDrawable : IDrawable
{
    private readonly IReadOnlyList<DonutSlice> _slices;

    private static readonly Color[] _palette =
    {
        Colors.CornflowerBlue, Colors.MediumSeaGreen, Colors.Orange,
        Colors.MediumOrchid, Colors.Salmon, Colors.Goldenrod,
        Colors.SteelBlue, Colors.Tomato
    };

    public DonutDrawable(IReadOnlyList<DonutSlice> slices) => _slices = slices;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        float size = Math.Min(dirtyRect.Width, dirtyRect.Height);
        var bounds = new RectF(
            dirtyRect.Center.X - size / 2f + 8,
            dirtyRect.Center.Y - size / 2f + 8,
            size - 16,
            size - 16);

        float ring = Math.Max(12f, size * 0.10f);
        canvas.StrokeSize = ring;

        // background ring
        canvas.StrokeColor = Colors.Gray.WithAlpha(0.15f);
        Microsoft.Maui.Graphics.CanvasExtensions.DrawArc(canvas, bounds, -90, 270, true, false);

        float total = Math.Max(1f, _slices.Sum(s => s.Value));
        float start = -90f;
        int i = 0;

        foreach (var s in _slices.Where(s => s.Value > 0))
        {
            float sweep = 360f * (s.Value / total);
            canvas.StrokeColor = _palette[i++ % _palette.Length];
            Microsoft.Maui.Graphics.CanvasExtensions.DrawArc(canvas, bounds, start, start + sweep, true, false);
            start += sweep;
        }
    }
}