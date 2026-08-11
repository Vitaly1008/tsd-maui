using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

/// <summary>
/// Статические цвета для единообразия
/// </summary>
internal static class ModeColors
{
    public static readonly Color ActiveBackground = Color.FromArgb("#2E7D32");
    public static readonly Color InactiveBackground = Color.FromArgb("#FFFFFF");
    public static readonly Color ActiveText = Color.FromArgb("#FFFFFF");
    public static readonly Color InactiveText = Color.FromArgb("#2E7D32");
    public static readonly Color ActiveBorder = Color.FromArgb("#2E7D32");
    public static readonly Color InactiveBorder = Color.FromArgb("#BDBDBD");
}

/// <summary>
/// Конвертер цвета фона для активного/неактивного режима
/// </summary>
public class ModeBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ModeColors.ActiveBackground : ModeColors.InactiveBackground;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}

/// <summary>
/// Конвертер цвета текста для активного/неактивного режима
/// </summary>
public class ModeTextColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ModeColors.ActiveText : ModeColors.InactiveText;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}

/// <summary>
/// Конвертер подписи для режимов перемещения/количества
/// </summary>
public class ModeLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Новая локация" : "Количество";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

/// <summary>
/// Конвертер цвета границы для активного/неактивного режима
/// </summary>
public class ModeBorderConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ModeColors.ActiveBorder : ModeColors.InactiveBorder;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}