using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

// Конвертер типа операции в текст на русском
public class OperationTypeDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var operationType = value?.ToString();
        
        return operationType switch
        {
            "Receiving" => "Приемка",
            "Shipping" => "Отгрузка",
            "Receiving_autosave" => "Автосохранение (приемка)",
            "Shipping_autosave" => "Автосохранение (отгрузка)",
            "start_operation" => "Начало операции",
            "scan" => "Сканирование",
            "confirm_operation" => "Подтверждение",
            "end_operation" => "Завершение",
            _ => operationType ?? "Неизвестно"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");
}