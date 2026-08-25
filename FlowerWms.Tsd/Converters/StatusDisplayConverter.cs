using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Converters;

// Конвертер статуса транзакции в текст
public class StatusDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OfflineTransaction transaction)
            return string.Empty;
        
        if (transaction.is_synced == 1)
            return "✅ Синхронизирована";
        
        if (!string.IsNullOrEmpty(transaction.error_message))
            return $"❌ Ошибка: {transaction.error_message}";
        
        return "⏳ Ожидает";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");
}