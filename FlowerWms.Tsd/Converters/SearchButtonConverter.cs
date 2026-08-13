using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

// Конвертер для текста кнопки поиска сервера (True - выполняется поиск, False - ожидание)
public class SearchButtonTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "⏳ Поиск..." : "🔍 Поиск сервера";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}