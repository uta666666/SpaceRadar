using System.Globalization;
using System.Windows.Data;
using SpaceRadar.Utilities;

namespace SpaceRadar.Converters;

[ValueConversion(typeof(long), typeof(string))]
public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
            return FileSizeFormatter.FormatSize(bytes);
        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
