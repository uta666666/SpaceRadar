using System.Globalization;
using System.Windows.Data;

namespace SpaceRadar.Converters;

[ValueConversion(typeof(bool), typeof(string))]
public class TopNScopeTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? " (現在のフォルダー)" : " (全体)";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
