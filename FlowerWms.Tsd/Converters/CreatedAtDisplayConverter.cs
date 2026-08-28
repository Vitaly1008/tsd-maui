using System.Globalization;
using FlowerWms.Tsd.Helpers;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

// Конвертер даты создания в читаемый формат
public class CreatedAtDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long timestamp && timestamp > 0)
        {
            try
            {
                var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
                return date.ToString("dd.MM.yyyy HH:mm");
            }
            catch
            {
                return "Дата неизвестна";
            }
        }
        
        if (value is OfflineTransaction transaction)
        {
            try
            {
                var date = DateTimeOffset.FromUnixTimeMilliseconds(transaction.created_at).LocalDateTime;
                return date.ToString("dd.MM.yyyy HH:mm");
            }
            catch
            {
                return "Дата неизвестна";
            }
        }
        
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");
}