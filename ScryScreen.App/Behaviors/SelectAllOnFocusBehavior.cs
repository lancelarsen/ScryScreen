using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ScryScreen.App.Behaviors;

public sealed class SelectAllOnFocusBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<SelectAllOnFocusBehavior, TextBox, bool>("IsEnabled");

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

        SelectAllSoon(textBox);
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
            SelectAllSoon(textBox);
            e.Handled = true;
        }
    }

    private static void SelectAllSoon(TextBox textBox)
    {
        if (string.IsNullOrEmpty(textBox.Text))
        {
            return;
        }

        // Schedule after input processing so the pointer event doesn't immediately
        // collapse selection by placing the caret at the click position.
        Avalonia.Threading.Dispatcher.UIThread.Post(textBox.SelectAll, Avalonia.Threading.DispatcherPriority.Input);
    }
}
