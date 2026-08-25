using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Converters;

// Конвертер статуса транзакции в цвет
public class StatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OfflineTransaction transaction)
            return Colors.Gray;
        
        if (transaction.is_synced == 1)
            return Colors.Green;
        
        if (!string.IsNullOrEmpty(transaction.error_message))
            return Colors.Red;
        
        return Colors.Orange;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");
}