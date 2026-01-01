using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ScryScreen.App.Behaviors;

public static class SelectAllOnFocusBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Control, bool>("IsEnabled");

    static SelectAllOnFocusBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(AvaloniaObject obj) => obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is bool enabled)
        {
            if (enabled)
            {
                textBox.GotFocus += OnGotFocus;
                textBox.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
            }
            else
            {
                textBox.GotFocus -= OnGotFocus;
                textBox.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            }
        }
    }

    private static void OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (!string.IsNullOrEmpty(textBox.Text))
        {
            textBox.SelectAll();
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (!textBox.IsFocused)
        {
            textBox.Focus();
            e.Handled = true;
        }
    }
}
