using System.Windows;
using System.Windows.Media;

namespace Hospitality.Pos.Desktop.ViewModels;

public static class Ui
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = [];

    public static SolidColorBrush Brush(string hex)
    {
        if (Cache.TryGetValue(hex, out var cached))
        {
            return cached;
        }

        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Cache[hex] = brush;
        return brush;
    }

    public static Geometry? Icon(string key) => Application.Current?.TryFindResource(key) as Geometry;
}
