using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Converters;

// Конвертер для добавления отступа под статус-бар Android
public class StatusBarPaddingConverter : IValueConverter
{
    // Добавляет высоту статус-бара к отступу сверху
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var basePadding = value as Thickness? ?? new Thickness(0);
        var statusBarHeight = StatusBarHelper.GetStatusBarHeight();

        return new Thickness(
            basePadding.Left,
            basePadding.Top + statusBarHeight,
            basePadding.Right,
            basePadding.Bottom
        );
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}