using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Converters;

// Конвертер количества попыток в текст
public class RetryCountDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OfflineTransaction transaction)
            return string.Empty;
        
        return transaction.retry_count == 0 
            ? "Попыток: 0" 
            : $"Попыток: {transaction.retry_count}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");
}