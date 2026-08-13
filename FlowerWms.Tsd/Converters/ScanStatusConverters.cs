using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

#region Статические цвета для сканера

// Базовые статические цвета для сканера
internal static class ScanColors
{
    public static readonly Color ScannedBackground = Color.FromArgb("#E8F5E9");
    public static readonly Color WaitingBackground = Color.FromArgb("#F5F5F5");
    public static readonly Color ScannedBorder = Color.FromArgb("#66BB6A");
    public static readonly Color WaitingBorder = Color.FromArgb("#BDBDBD");
    public static readonly Color ScannedText = Color.FromArgb("#212121");
    public static readonly Color WaitingText = Color.FromArgb("#9E9E9E");
}

#endregion

#region Конвертеры статуса сканирования

// Конвертер для фона статуса сканирования
public class ScanStatusBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value?.ToString()) 
            ? ScanColors.ScannedBackground 
            : ScanColors.WaitingBackground;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

// Конвертер для цвета границы статуса сканирования
public class ScanStatusBorderConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value?.ToString()) 
            ? ScanColors.ScannedBorder 
            : ScanColors.WaitingBorder;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

// Конвертер для иконки статуса сканирования
public class ScanStatusIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value?.ToString()) ? "✅" : "📷";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

// Конвертер для текста статуса сканирования
public class ScanStatusTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var scannedValue = value?.ToString();
        return !string.IsNullOrEmpty(scannedValue) 
            ? $"✅ Отсканировано: {scannedValue}" 
            : "📷 Ожидание сканирования";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

// Конвертер для цвета текста статуса сканирования
public class ScanStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value?.ToString()) 
            ? ScanColors.ScannedText 
            : ScanColors.WaitingText;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

// Конвертер для подсказки статуса сканирования (поддерживает параметр "invert")
public class ScanStatusSubtitleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString()?.ToLower() == "invert";
        var hasValue = !string.IsNullOrEmpty(value?.ToString());

        if (invert)
        {
            return hasValue 
                ? "Нажмите триггер для сканирования" 
                : "Коробка добавлена в список";
        }

        return hasValue 
            ? "Коробка добавлена в список" 
            : "Нажмите триггер для сканирования";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

#endregion