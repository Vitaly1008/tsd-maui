using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Views.Behaviors;

/// <summary>
/// Поведение для валидации числового ввода
/// </summary>
public class NumericValidationBehavior : Behavior<Entry>
{
    protected override void OnAttachedTo(Entry entry)
    {
        entry.TextChanged += OnEntryTextChanged;
        base.OnAttachedTo(entry);
    }

    protected override void OnDetachingFrom(Entry entry)
    {
        entry.TextChanged -= OnEntryTextChanged;
        base.OnDetachingFrom(entry);
    }

    private void OnEntryTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry)
        {
            if (!string.IsNullOrEmpty(entry.Text) && !int.TryParse(entry.Text, out _))
            {
                entry.Text = e.OldTextValue ?? string.Empty;
            }
        }
    }
}