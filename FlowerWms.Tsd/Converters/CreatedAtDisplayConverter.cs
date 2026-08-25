using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Converters;

// Конвертер даты создания в читаемый формат
public class CreatedAtDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OfflineTransaction transaction)
            return string.Empty;
        
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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");
}