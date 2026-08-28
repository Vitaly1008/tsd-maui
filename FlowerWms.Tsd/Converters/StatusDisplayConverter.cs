using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Converters;

// Конвертер статуса транзакции в текст
public class StatusDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Проверяем разные варианты входных данных
        if (value is OfflineTransaction transaction)
        {
            if (transaction.is_synced == 1)
                return "✅ Синхронизирована";
            
            if (!string.IsNullOrEmpty(transaction.error_message))
                return $"❌ Ошибка: {transaction.error_message}";
            
            return "⏳ Ожидает";
        }

        // Если передано только значение is_synced
        if (value is int isSynced)
        {
            return isSynced == 1 ? "✅ Синхронизирована" : "⏳ Ожидает";
        }

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");
}