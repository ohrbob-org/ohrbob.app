using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ohrbob.App.Services;

public class WidthToVisibilityConverter : IValueConverter
{
    public double Threshold { get; set; } = 600;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            return width >= Threshold;
        }
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
