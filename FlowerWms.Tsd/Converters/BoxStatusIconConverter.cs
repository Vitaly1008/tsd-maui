using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Converters;

/// <summary>
/// Базовый абстрактный класс для всех конвертеров статуса коробки
/// </summary>
/// <remarks>
/// Предоставляет единый метод GetStatus для преобразования значений в BoxStatus
/// Поддерживает: BoxStatus, int, string (числовое представление)
/// </remarks>
public abstract class BoxStatusBaseConverter : IValueConverter
{
    /// <summary>
    /// Преобразует входное значение в BoxStatus
    /// </summary>
    /// <param name="value">Значение для преобразования (BoxStatus, int, string)</param>
    /// <returns>BoxStatus или null если преобразование невозможно</returns>
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

/// <summary>
/// Конвертер статуса коробки в иконку (эмодзи)
/// </summary>
/// <example>
/// <Label Text="{Binding Status, Converter={StaticResource BoxStatusIcon}}" />
/// </example>
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

/// <summary>
/// Конвертер статуса коробки в текстовое описание на русском языке
/// </summary>
/// <example>
/// <Label Text="{Binding Status, Converter={StaticResource BoxStatusText}}" />
/// </example>
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

/// <summary>
/// Конвертер статуса коробки в цвет для UI
/// </summary>
/// <example>
/// <BoxView Color="{Binding Status, Converter={StaticResource BoxStatusColor}}" />
/// </example>
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

/// <summary>
/// Конвертер статуса коробки в фоновый цвет (светлые версии для карточек)
/// </summary>
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

/// <summary>
/// Конвертер списка коробок в отображение локации
/// </summary>
/// <remarks>
/// Если все коробки в одной локации - показывает ее код
/// Если в разных - показывает количество уникальных локаций
/// </remarks>
public class LocationDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not System.Collections.IEnumerable collection)
            return "Не указана";

        var boxes = collection.Cast<Box>().ToList();
        
        if (!boxes.Any())
            return "Не указана";

        var locations = boxes
            .Where(box => !string.IsNullOrEmpty(box.LocationCode))
            .Select(box => box.LocationCode)
            .Distinct()
            .ToList();

        return locations.Count switch
        {
            0 => "Не указана",
            1 => locations.First(),
            _ => $"{locations.Count} разных"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");
}