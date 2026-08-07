using System.Globalization;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Converters;


public class BoxStatusIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int status)
        {
            return status switch
            {
                1 => "📦",     // Active
                3 => "📤",     // Shipped
                4 => "🗑️",    // Discarded
                5 => "🔒",     // Reserved
                _ => "📦"
            };
        }
        return "📦";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoxStatusLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int status)
        {
            return status switch
            {
                1 => "Активна",
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

public class BoxStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int status)
        {
            return status switch
            {
                1 => Colors.Green,
                3 => Colors.Blue,
                4 => Colors.Red,
                5 => Colors.Orange,
                _ => Colors.Gray
            };
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LocationDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Collections.IEnumerable collection)
        {
            var boxes = collection.Cast<Box>().ToList();
            if (boxes.Any())
            {
                var locations = boxes.Where(b => !string.IsNullOrEmpty(b.LocationCode))
                                     .Select(b => b.LocationCode)
                                     .Distinct()
                                     .ToList();
                
                if (locations.Count == 1)
                    return locations.First();
                else if (locations.Count > 1)
                    return $"{locations.Count} разных";
            }
        }
        return "Не указана";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}