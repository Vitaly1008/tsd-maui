using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

/// <summary>
/// Конвертер для инвертирования булевого значения
/// </summary>
/// <example>
/// <Label IsVisible="{Binding IsLoading, Converter={StaticResource InverseBool}}" />
/// </example>
public class InverseBooleanConverter : BooleanConverter<bool>
{
    protected override bool TrueValue => false;
    protected override bool FalseValue => true;
}

/// <summary>
/// Конвертер для проверки, что значение больше нуля
/// </summary>
/// <remarks>
/// Поддерживает: int, double, float, long, decimal, ICollection
/// </remarks>
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

/// <summary>
/// Конвертер для проверки, что значение не равно null и не пустое
/// </summary>
/// <remarks>
/// Поддерживает параметр "invert" для инвертирования результата
/// </remarks>
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

/// <summary>
/// [УСТАРЕЛ] Используйте IsNotNullOrEmptyConverter
/// </summary>
[Obsolete("Используйте IsNotNullOrEmptyConverter")]
public class StringNotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string stringValue && !string.IsNullOrEmpty(stringValue);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}