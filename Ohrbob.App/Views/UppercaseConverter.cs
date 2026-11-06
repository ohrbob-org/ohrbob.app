using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ohrbob.App.Views;

// Bootstrap uppercases the station name via CSS text-transform.
public sealed class UppercaseConverter : IValueConverter
{
    public static readonly UppercaseConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpper(culture) ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
