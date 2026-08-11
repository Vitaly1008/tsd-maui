using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Converters;

/// <summary>
/// Конвертер для добавления отступа под статус-бар Android
/// </summary>
public class StatusBarPaddingConverter : IValueConverter
{
    /// <summary>
    /// Добавляет высоту статус-бара к отступу сверху
    /// </summary>
    /// <param name="value">Базовый Padding</param>
    /// <returns>Новый Padding с отступом сверху</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var basePadding = value as Thickness? ?? new Thickness(0);
        var statusBarHeight = StatusBarHelper.GetStatusBarHeight();

        return new Thickness(
            basePadding.Value.Left,
            basePadding.Value.Top + statusBarHeight,
            basePadding.Value.Right,
            basePadding.Value.Bottom
        );
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}