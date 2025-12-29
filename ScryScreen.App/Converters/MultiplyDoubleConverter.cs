using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ScryScreen.App.Converters;

public sealed class MultiplyDoubleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return null;
        }

        var factor = ParseFactor(parameter);
        if (factor is null)
        {
            return AvaloniaProperty.UnsetValue;
        }

        if (value is double d)
        {
            return d * factor.Value;
        }

        if (value is float f)
        {
            return f * factor.Value;
        }

        if (value is int i)
        {
            return i * factor.Value;
        }

        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return null;
        }

        var factor = ParseFactor(parameter);
        if (factor is null || factor.Value == 0)
        {
            return AvaloniaProperty.UnsetValue;
        }

        if (value is double d)
        {
            return d / factor.Value;
        }

        return AvaloniaProperty.UnsetValue;
    }

    private static double? ParseFactor(object? parameter)
    {
        if (parameter is null)
        {
            return null;
        }

        if (parameter is double dd)
        {
            return dd;
        }

        if (parameter is float ff)
        {
            return ff;
        }

        if (parameter is int ii)
        {
            return ii;
        }

        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
