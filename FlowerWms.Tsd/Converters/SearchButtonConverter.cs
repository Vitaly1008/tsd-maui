using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

/// <summary>
/// Конвертер для текста кнопки поиска сервера
/// </summary>
public class SearchButtonTextConverter : IValueConverter
{
    /// <summary>
    /// Преобразует состояние поиска в текст кнопки
    /// </summary>
    /// <param name="value">True - выполняется поиск</param>
    /// <returns>"⏳ Поиск..." или "🔍 Поиск сервера"</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "⏳ Поиск..." : "🔍 Поиск сервера";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}