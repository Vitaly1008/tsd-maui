using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Converters;

/// <summary>
/// Конвертер для добавления отступа под статус-бар
/// </summary>
public class StatusBarPaddingConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Получаем базовый Padding (если передан)
        var basePadding = value as Thickness? ?? new Thickness(0);
        
        // Получаем высоту статус-бара
        var statusBarHeight = StatusBarHelper.GetStatusBarHeight();
        
        // Возвращаем новый Padding с отступом сверху
        return new Thickness(
            basePadding.Left,
            basePadding.Top + statusBarHeight,
            basePadding.Right,
            basePadding.Bottom
        );
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}