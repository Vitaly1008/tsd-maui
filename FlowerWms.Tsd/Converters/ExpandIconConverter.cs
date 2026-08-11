using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

/// <summary>
/// Конвертер состояния раскрытия в иконку
/// </summary>
public class ExpandIconConverter : IValueConverter
{
    /// <summary>
    /// Преобразует булево состояние в иконку
    /// </summary>
    /// <param name="value">True - раскрыто, False - свернуто</param>
    /// <returns>▼ для раскрытого, ▶ для свернутого</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "▼" : "▶";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}