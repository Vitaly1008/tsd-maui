using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

#region Конвертеры иконок подключения и синхронизации

// Конвертер статуса подключения в иконку сети (True - онлайн, False - офлайн)
public class ConnectionIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "📶" : "📴";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}

// Конвертер статуса синхронизации в иконку (True - синхронизация выполняется, False - ожидание)
public class SyncStatusIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "🔄" : "📶";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}

#endregion