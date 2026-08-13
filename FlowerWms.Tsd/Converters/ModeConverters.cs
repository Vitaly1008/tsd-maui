using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

#region Статические цвета для конвертеров режимов

// Статические цвета для единообразия
internal static class ModeColors
{
    public static readonly Color ActiveBackground = Color.FromArgb("#2E7D32");
    public static readonly Color InactiveBackground = Color.FromArgb("#FFFFFF");
    public static readonly Color ActiveText = Color.FromArgb("#FFFFFF");
    public static readonly Color InactiveText = Color.FromArgb("#2E7D32");
    public static readonly Color ActiveBorder = Color.FromArgb("#2E7D32");
    public static readonly Color InactiveBorder = Color.FromArgb("#BDBDBD");
}

#endregion

#region Конвертеры цветов для активного/неактивного режима

// Конвертер цвета фона для активного/неактивного режима
public class ModeBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ModeColors.ActiveBackground : ModeColors.InactiveBackground;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}

// Конвертер цвета текста для активного/неактивного режима
public class ModeTextColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ModeColors.ActiveText : ModeColors.InactiveText;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}

// Конвертер цвета границы для активного/неактивного режима
public class ModeBorderConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ModeColors.ActiveBorder : ModeColors.InactiveBorder;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => false;
}

#endregion

#region Конвертер подписи для режимов

// Конвертер подписи для режимов перемещения/количества
public class ModeLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Новая локация" : "Количество";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

#endregion