using System;
using Avalonia;
using Avalonia.Data.Converters;
using System.Globalization;

namespace ScryScreen.App.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }

        return value is null ? null : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }

        return value is null ? null : AvaloniaProperty.UnsetValue;
    }
}
