using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ScryScreen.App.Converters;

public sealed class NotZeroToBoolConverter : IValueConverter
{
    public static readonly NotZeroToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return false;

        if (value is int i)
            return i != 0;

        if (value is long l)
            return l != 0;

        if (value is uint ui)
            return ui != 0;

        if (value is ulong ul)
            return ul != 0;

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
