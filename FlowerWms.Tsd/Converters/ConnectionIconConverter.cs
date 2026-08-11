using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

/// <summary>
/// Конвертер статуса подключения в иконку
/// </summary>
public class ConnectionIconConverter : IValueConverter
{
    /// <summary>
    /// Преобразует булево значение в иконку сети
    /// </summary>
    /// <param name="value">True - онлайн, False - офлайн</param>
    /// <returns>📶 для онлайн, 📴 для офлайн</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "📶" : "📴";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}

/// <summary>
/// Конвертер статуса синхронизации в иконку
/// </summary>
public class SyncStatusIconConverter : IValueConverter
{
    /// <summary>
    /// Преобразует статус синхронизации в иконку
    /// </summary>
    /// <param name="value">True - синхронизация выполняется</param>
    /// <returns>🔄 для синхронизации, 📶 для ожидания</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "🔄" : "📶";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}