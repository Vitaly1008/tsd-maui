using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

/// <summary>
/// Конвертер для отображения статуса коробки в виде текста
/// </summary>
public class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int status)
        {
            return status switch
            {
                0 => "Черновик",
                1 => "Активна",
                2 => "Пуста",
                3 => "Отгружена",
                4 => "Списана",
                5 => "Зарезервирована",
                _ => "Неизвестно"
            };
        }
        return "Неизвестно";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Конвертер для цвета статуса коробки
/// </summary>
public class StatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int status)
        {
            return status switch
            {
                0 => Colors.Gray,
                1 => Colors.Green,
                2 => Colors.Orange,
                3 => Colors.Blue,
                4 => Colors.Red,
                5 => Colors.Purple,
                _ => Colors.Gray
            };
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}