using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

#region Базовые абстрактные конвертеры

// Базовый абстрактный класс для конвертеров с поддержкой преобразования значений
public abstract class BaseConverter<TFrom, TTo> : IValueConverter
{
    // Преобразует значение из TFrom в TTo
    protected abstract TTo ConvertValue(TFrom value, object? parameter);

    // Преобразует значение из TTo в TFrom (обратное преобразование)
    protected abstract TFrom ConvertBackValue(TTo value, object? parameter);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (value is TFrom typedValue)
                return ConvertValue(typedValue, parameter);

            if (TryConvertToTFrom(value, out var converted))
                return ConvertValue(converted, parameter);

            return default(TTo);
        }
        catch (Exception ex)
        {
            LogError($"Ошибка в конвертере {GetType().Name}: {ex.Message}");
            return default(TTo);
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (value is TTo typedValue)
                return ConvertBackValue(typedValue, parameter);

            if (TryConvertToTTo(value, out var converted))
                return ConvertBackValue(converted, parameter);

            return default(TFrom);
        }
        catch (Exception ex)
        {
            LogError($"Ошибка в обратном преобразовании {GetType().Name}: {ex.Message}");
            return default(TFrom);
        }
    }

    // Пытается преобразовать значение в тип TFrom через IConvertible
    private bool TryConvertToTFrom(object? value, out TFrom result)
    {
        result = default!;
        if (value is not IConvertible)
            return false;

        try
        {
            result = (TFrom)System.Convert.ChangeType(value, typeof(TFrom));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Пытается преобразовать значение в тип TTo через IConvertible
    private bool TryConvertToTTo(object? value, out TTo result)
    {
        result = default!;
        if (value is not IConvertible)
            return false;

        try
        {
            result = (TTo)System.Convert.ChangeType(value, typeof(TTo));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Логирование ошибок
    private static void LogError(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
    }
}

// Базовый конвертер для преобразования boolean в другие типы
public abstract class BooleanConverter<TResult> : BaseConverter<bool, TResult>
{
    // Значение для true
    protected abstract TResult TrueValue { get; }

    /// Значение для false
    protected abstract TResult FalseValue { get; }

    protected override TResult ConvertValue(bool value, object? parameter)
        => value ? TrueValue : FalseValue;

    protected override bool ConvertBackValue(TResult value, object? parameter)
        => Equals(value, TrueValue);
}

// Конвертер для отображения значения в зависимости от условия
public abstract class ConditionalConverter<TValue, TResult> : BaseConverter<TValue, TResult>
{
    // Проверяет условие для значения
    protected abstract bool IsConditionMet(TValue value, object? parameter);

    // Значение при выполнении условия
    protected abstract TResult ValueIfTrue { get; }

    // Значение при невыполнении условия
    protected abstract TResult ValueIfFalse { get; }

    protected override TResult ConvertValue(TValue value, object? parameter)
        => IsConditionMet(value, parameter) ? ValueIfTrue : ValueIfFalse;

    protected override TValue ConvertBackValue(TResult value, object? parameter)
        => throw new NotSupportedException($"Обратное преобразование не поддерживается в {GetType().Name}");
}

#endregion

#region Конкретные реализации конвертеров

// Конвертер для проверки, что значение равно null
public class IsNullConverter : ConditionalConverter<object?, bool>
{
    protected override bool IsConditionMet(object? value, object? parameter)
        => value == null;

    protected override bool ValueIfTrue => true;
    protected override bool ValueIfFalse => false;
}

// Конвертер для проверки, что коллекция не пуста
public class CollectionNotEmptyConverter : ConditionalConverter<System.Collections.ICollection?, bool>
{
    protected override bool IsConditionMet(System.Collections.ICollection? value, object? parameter)
        => value != null && value.Count > 0;

    protected override bool ValueIfTrue => true;
    protected override bool ValueIfFalse => false;
}

// Конвертер для сравнения значения с константой
public class EqualsValueConverter : BaseConverter<object?, bool>
{
    protected override bool ConvertValue(object? value, object? parameter)
    {
        if (parameter == null)
            return false;

        // Прямое сравнение
        if (Equals(value, parameter))
            return true;

        // Сравнение строк с учетом регистра
        if (value is string strValue && parameter is string strParam)
            return string.Equals(strValue, strParam, StringComparison.Ordinal);

        // Сравнение чисел
        if (value is IComparable comparable && parameter is IConvertible convertible)
        {
            try
            {
                var paramValue = System.Convert.ChangeType(parameter, value.GetType());
                return comparable.CompareTo(paramValue) == 0;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    protected override object? ConvertBackValue(bool value, object? parameter)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");
}

// Конвертер для инвертирования булевого значения
public class InvertBooleanConverter : BooleanConverter<bool>
{
    protected override bool TrueValue => false;
    protected override bool FalseValue => true;
}

#endregion