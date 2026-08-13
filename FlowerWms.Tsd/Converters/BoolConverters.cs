using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

#region Конвертеры булевых значений

// Конвертер для проверки, что значение больше нуля (поддерживает int, double, float, long, decimal, ICollection)
public class GreaterThanZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            int intValue => intValue > 0,
            double doubleValue => doubleValue > 0,
            float floatValue => floatValue > 0,
            long longValue => longValue > 0,
            decimal decimalValue => decimalValue > 0,
            System.Collections.ICollection collection => collection.Count > 0,
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}

// Конвертер для проверки, что значение не равно null и не пустое (параметр "invert" для инвертирования)
public class IsNotNullOrEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString()?.ToLower() == "invert";
        var hasValue = value switch
        {
            null => false,
            string stringValue => !string.IsNullOrEmpty(stringValue),
            _ => true
        };

        return invert ? !hasValue : hasValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}

#endregion