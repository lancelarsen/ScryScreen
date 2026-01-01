using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ScryScreen.App.Behaviors;

public static class NumericTextBoxBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Control, bool>("IsEnabled");

    private static readonly AttachedProperty<string> LastGoodTextProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Control, string>("LastGoodText", defaultValue: string.Empty);

    private static readonly Regex IntegerRegex = new("^-?\\d*$", RegexOptions.Compiled);

    static NumericTextBoxBehavior()
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
                textBox.PropertyChanged += OnTextBoxPropertyChanged;
                textBox.AddHandler(InputElement.TextInputEvent, OnTextInput, handledEventsToo: true);
                textBox.AddHandler(InputElement.KeyDownEvent, OnKeyDown, handledEventsToo: true);

                // Initialize last-good.
                var current = textBox.Text ?? string.Empty;
                if (IsAllowed(current))
                {
                    textBox.SetValue(LastGoodTextProperty, current);
                }
                else
                {
                    textBox.Text = string.Empty;
                    textBox.SetValue(LastGoodTextProperty, string.Empty);
                }
            }
            else
            {
                textBox.PropertyChanged -= OnTextBoxPropertyChanged;
                textBox.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
                textBox.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            }
        }
    }

    private static void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (e.Property == TextBox.TextProperty)
        {
            OnTextChanged(textBox, e.NewValue as string);
        }
    }

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        // Let control keys through; filter typed text to numeric.
        if (!IsAllowedInsertion(textBox, e.Text))
        {
            e.Handled = true;
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        // Block space.
        if (e.Key == Key.Space)
        {
            e.Handled = true;
        }

        // Allow minus only at start.
        if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
        {
            var caret = textBox.CaretIndex;
            if (caret != 0)
            {
                e.Handled = true;
            }
        }
    }

    private static void OnTextChanged(TextBox textBox, string? text)
    {
        var current = text ?? string.Empty;
        if (IsAllowed(current))
        {
            textBox.SetValue(LastGoodTextProperty, current);
            return;
        }

        var lastGood = textBox.GetValue(LastGoodTextProperty) ?? string.Empty;
        if (textBox.Text != lastGood)
        {
            textBox.Text = lastGood;
            textBox.CaretIndex = lastGood.Length;
        }
    }

    private static bool IsAllowed(string text) => IntegerRegex.IsMatch(text);

    private static bool IsAllowedInsertion(TextBox textBox, string? inserted)
    {
        if (string.IsNullOrEmpty(inserted))
        {
            return true;
        }

        // Only allow digits and optionally a leading '-'.
        var ins = inserted;
        if (!IsAllowed(ins))
        {
            return false;
        }

        // If user types '-', only allow it at the start and only once.
        if (ins.Contains('-'))
        {
            if (ins != "-")
            {
                return false;
            }

            if (textBox.CaretIndex != 0)
            {
                return false;
            }

            if ((textBox.Text ?? string.Empty).Contains('-'))
            {
                return false;
            }
        }

        return true;
    }
}
