using System.Globalization;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Converters;

/// <summary>
/// Базовый абстрактный класс для конвертеров с поддержкой преобразования значений
/// </summary>
/// <typeparam name="TFrom">Исходный тип данных</typeparam>
/// <typeparam name="TTo">Целевой тип данных</typeparam>
/// <remarks>
/// Предоставляет шаблон для Convert с проверкой типов и обработкой ошибок
/// </remarks>
public abstract class BaseConverter<TFrom, TTo> : IValueConverter
{
    /// <summary>
    /// Преобразует значение из TFrom в TTo
    /// </summary>
    /// <param name="value">Исходное значение</param>
    /// <param name="parameter">Дополнительный параметр</param>
    /// <returns>Преобразованное значение или default(TTo)</returns>
    protected abstract TTo ConvertValue(TFrom value, object? parameter);

    /// <summary>
    /// Преобразует значение из TTo в TFrom (обратное преобразование)
    /// </summary>
    /// <param name="value">Исходное значение</param>
    /// <param name="parameter">Дополнительный параметр</param>
    /// <returns>Преобразованное значение или default(TFrom)</returns>
    protected abstract TFrom ConvertBackValue(TTo value, object? parameter);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (value is TFrom typedValue)
                return ConvertValue(typedValue, parameter);

            // Попытка преобразовать через TypeConverter
            if (value != null && value is IConvertible convertible)
            {
                try
                {
                    var converted = (TFrom)System.Convert.ChangeType(value, typeof(TFrom));
                    return ConvertValue(converted, parameter);
                }
                catch
                {
                    // Игнорируем ошибки конвертации
                }
            }

            return default(TTo);
        }
        catch (Exception ex)
        {
            // Логирование ошибки в будущем
            System.Diagnostics.Debug.WriteLine($"Ошибка в конвертере {GetType().Name}: {ex.Message}");
            return default(TTo);
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (value is TTo typedValue)
                return ConvertBackValue(typedValue, parameter);

            if (value != null && value is IConvertible convertible)
            {
                try
                {
                    var converted = (TTo)System.Convert.ChangeType(value, typeof(TTo));
                    return ConvertBackValue(converted, parameter);
                }
                catch
                {
                    // Игнорируем ошибки конвертации
                }
            }

            return default(TFrom);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка в обратном преобразовании {GetType().Name}: {ex.Message}");
            return default(TFrom);
        }
    }
}

/// <summary>
/// Базовый конвертер для преобразования boolean в другие типы
/// </summary>
/// <typeparam name="TResult">Целевой тип</typeparam>
public abstract class BooleanConverter<TResult> : BaseConverter<bool, TResult>
{
    /// <summary>
    /// Значение для true
    /// </summary>
    protected abstract TResult TrueValue { get; }

    /// <summary>
    /// Значение для false
    /// </summary>
    protected abstract TResult FalseValue { get; }

    protected override TResult ConvertValue(bool value, object? parameter)
        => value ? TrueValue : FalseValue;

    protected override bool ConvertBackValue(TResult value, object? parameter)
        => Equals(value, TrueValue);
}

/// <summary>
/// Конвертер для отображения значения в зависимости от условия
/// </summary>
/// <typeparam name="TValue">Тип проверяемого значения</typeparam>
/// <typeparam name="TResult">Тип результата</typeparam>
public abstract class ConditionalConverter<TValue, TResult> : BaseConverter<TValue, TResult>
{
    /// <summary>
    /// Проверяет условие для значения
    /// </summary>
    protected abstract bool IsConditionMet(TValue value, object? parameter);

    /// <summary>
    /// Значение при выполнении условия
    /// </summary>
    protected abstract TResult ValueIfTrue { get; }

    /// <summary>
    /// Значение при невыполнении условия
    /// </summary>
    protected abstract TResult ValueIfFalse { get; }

    protected override TResult ConvertValue(TValue value, object? parameter)
        => IsConditionMet(value, parameter) ? ValueIfTrue : ValueIfFalse;

    protected override TValue ConvertBackValue(TResult value, object? parameter)
        => throw new NotSupportedException($"Обратное преобразование не поддерживается в {GetType().Name}");
}

/// <summary>
/// Конвертер для проверки, что значение равно null
/// </summary>
/// <example>
/// <Label IsVisible="{Binding Items, Converter={StaticResource IsNull}}" />
/// </example>
public class IsNullConverter : ConditionalConverter<object?, bool>
{
    protected override bool IsConditionMet(object? value, object? parameter)
        => value == null;

    protected override bool ValueIfTrue => true;
    protected override bool ValueIfFalse => false;
}

/// <summary>
/// Конвертер для проверки, что коллекция не пуста
/// </summary>
/// <example>
/// <Label IsVisible="{Binding Items, Converter={StaticResource CollectionNotEmpty}}" />
/// </example>
public class CollectionNotEmptyConverter : ConditionalConverter<System.Collections.ICollection?, bool>
{
    protected override bool IsConditionMet(System.Collections.ICollection? value, object? parameter)
        => value != null && value.Count > 0;

    protected override bool ValueIfTrue => true;
    protected override bool ValueIfFalse => false;
}

/// <summary>
/// Конвертер для сравнения значения с константой
/// </summary>
/// <example>
/// <Label IsVisible="{Binding Status, Converter={StaticResource EqualsValue}, ConverterParameter=1}" />
/// </example>
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

/// <summary>
/// Конвертер для инвертирования булевого значения (обертка над InverseBooleanConverter)
/// </summary>
public class InvertBooleanConverter : BooleanConverter<bool>
{
    protected override bool TrueValue => false;
    protected override bool FalseValue => true;
}