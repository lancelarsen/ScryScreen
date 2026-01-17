using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ScryScreen.App.Converters;

public sealed class ImageBrushFromImageConverter : IValueConverter
{
    private static readonly ConditionalWeakTable<IImageBrushSource, ImageBrush> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IImageBrushSource imageSource)
        {
            return null;
        }

        if (Cache.TryGetValue(imageSource, out var brush))
        {
            return brush;
        }

        brush = new ImageBrush
        {
            Source = imageSource,
            Stretch = Stretch.Fill,
        };

        Cache.Add(imageSource, brush);
        return brush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
