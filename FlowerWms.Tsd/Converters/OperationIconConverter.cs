using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

// Конвертер типа операции в иконку (эмодзи)
public class OperationIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var operationType = value?.ToString();
        
        return operationType switch
        {
            "Receiving" => "📥",
            "Shipping" => "📤",
            "Receiving_autosave" => "💾",
            "Shipping_autosave" => "💾",
            "start_operation" => "🚀",
            "scan" => "📷",
            "confirm_operation" => "✅",
            "end_operation" => "⏹️",
            _ => "📋"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");
}