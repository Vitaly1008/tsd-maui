using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

// Конвертер для проверки, что значение равно 0
public class IsZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            int intValue => intValue == 0,
            double doubleValue => doubleValue == 0,
            float floatValue => floatValue == 0,
            long longValue => longValue == 0,
            decimal decimalValue => decimalValue == 0,
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}