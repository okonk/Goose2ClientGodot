using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MapEditor.Core;

namespace MapEditor.App.Controls;

internal sealed class TerrainColorSwatchConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
        => value is TerrainColor color ? new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
        => throw new NotSupportedException();
}
