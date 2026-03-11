using System.Globalization;
using Avalonia.Data.Converters;

namespace SpaceRader.Avalonia.Converters;

public class TopNScopeTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? " (現在のフォルダー)" : " (全体)";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
