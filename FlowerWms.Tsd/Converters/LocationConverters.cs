using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Converters;

// Конвертер списка коробок в отображение локации
// Если все коробки в одной локации - показывает ее код
// Если в разных - показывает количество уникальных локаций
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