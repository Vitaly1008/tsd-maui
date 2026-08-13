using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Converters;

#region Базовый конвертер статуса коробки

// Базовый абстрактный класс для всех конвертеров статуса коробки
public abstract class BoxStatusBaseConverter : IValueConverter
{
    // Преобразует входное значение в BoxStatus (поддерживает BoxStatus, int, string)
    protected BoxStatus? GetStatus(object? value)
    {
        if (value == null)
            return null;

        // Прямое преобразование из enum
        if (value is BoxStatus status)
            return status;

        // Преобразование из int
        if (value is int intStatus && Enum.IsDefined(typeof(BoxStatus), intStatus))
            return (BoxStatus)intStatus;

        // Преобразование из string
        if (value is string stringStatus && int.TryParse(stringStatus, out int parsedInt) 
            && Enum.IsDefined(typeof(BoxStatus), parsedInt))
            return (BoxStatus)parsedInt;

        return null;
    }

    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);
    
    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"Обратное преобразование не поддерживается в {GetType().Name}");
}

#endregion

#region Конвертеры статуса коробки

// Конвертер статуса коробки в иконку (эмодзи)
public class BoxStatusIconConverter : BoxStatusBaseConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = GetStatus(value);
        
        return status switch
        {
            BoxStatus.Draft => "📝",      // Черновик
            BoxStatus.Active => "📦",     // Активная коробка
            BoxStatus.Empty => "📭",      // Пустая коробка
            BoxStatus.Shipped => "📤",    // Отгружена
            BoxStatus.Discarded => "🗑️",  // Списана
            BoxStatus.Reserved => "🔒",   // Зарезервирована
            _ => "❓"                     // Неизвестный статус
        };
    }
}

// Конвертер статуса коробки в текстовое описание на русском языке
public class BoxStatusTextConverter : BoxStatusBaseConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = GetStatus(value);
        
        return status switch
        {
            BoxStatus.Draft => "Черновик",
            BoxStatus.Active => "Активна",
            BoxStatus.Empty => "Пуста",
            BoxStatus.Shipped => "Отгружена",
            BoxStatus.Discarded => "Списана",
            BoxStatus.Reserved => "Зарезервирована",
            _ => "Неизвестно"
        };
    }
}

// Конвертер статуса коробки в цвет для UI
public class BoxStatusColorConverter : BoxStatusBaseConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = GetStatus(value);
        
        return status switch
        {
            BoxStatus.Draft => Colors.Gray,      // Серый - черновик
            BoxStatus.Active => Colors.Green,    // Зеленый - активна
            BoxStatus.Empty => Colors.Orange,    // Оранжевый - пуста
            BoxStatus.Shipped => Colors.Blue,    // Синий - отгружена
            BoxStatus.Discarded => Colors.Red,   // Красный - списана
            BoxStatus.Reserved => Colors.Purple, // Фиолетовый - зарезервирована
            _ => Colors.Gray
        };
    }
}

// Конвертер статуса коробки в фоновый цвет (светлые версии для карточек)
public class BoxStatusBackgroundConverter : BoxStatusBaseConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = GetStatus(value);
        
        return status switch
        {
            BoxStatus.Draft => Color.FromArgb("#F5F5F5"),
            BoxStatus.Active => Color.FromArgb("#E8F5E9"),
            BoxStatus.Empty => Color.FromArgb("#FFF3E0"),
            BoxStatus.Shipped => Color.FromArgb("#E3F2FD"),
            BoxStatus.Discarded => Color.FromArgb("#FFEBEE"),
            BoxStatus.Reserved => Color.FromArgb("#F3E5F5"),
            _ => Color.FromArgb("#FAFAFA")
        };
    }
}

#endregion