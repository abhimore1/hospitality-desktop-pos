using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Hospitality.Pos.Desktop.Converters;

/// <summary>Shows an element when a boolean is false. Used for empty states.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>Collapses an element when a string is empty or whitespace.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Collapses an element when its value is null.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Formats integer paise as Indian rupees for the printable bill.</summary>
public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? "" : Services.Inr.Format(System.Convert.ToInt64(value));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Green dot for vegetarian dishes, red for non-vegetarian.</summary>
public sealed class VegBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Veg = Create("#16A34A");
    private static readonly SolidColorBrush NonVeg = Create("#B91C1C");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Veg : NonVeg;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush Create(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
