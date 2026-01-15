using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ScryScreen.App.Behaviors;

public sealed class NumericTextBoxBehavior
{
    private NumericTextBoxBehavior() { }

    public static readonly AttachedProperty<bool> IsNumericOnlyProperty =
        AvaloniaProperty.RegisterAttached<NumericTextBoxBehavior, TextBox, bool>("IsNumericOnly");

    public static readonly AttachedProperty<bool> IsNumericDecimalOnlyProperty =
        AvaloniaProperty.RegisterAttached<NumericTextBoxBehavior, TextBox, bool>("IsNumericDecimalOnly");

    private enum NumericMode
    {
        None,
        Integer,
        Decimal,
    }

    private static readonly AttachedProperty<NumericMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<NumericTextBoxBehavior, TextBox, NumericMode>("Mode", defaultValue: NumericMode.None);

    private static readonly AttachedProperty<string> LastGoodTextProperty =
        AvaloniaProperty.RegisterAttached<NumericTextBoxBehavior, TextBox, string>("LastGoodText", defaultValue: string.Empty);

    private static readonly Regex IntegerRegex = new("^-?\\d*$", RegexOptions.Compiled);

    // Allow "", "-", "123", "123.", "123.45", "123,45"
    private static readonly Regex DecimalRegex = new("^-?\\d*([\\.,]\\d*)?$", RegexOptions.Compiled);

    static NumericTextBoxBehavior()
    {
        IsNumericOnlyProperty.Changed.AddClassHandler<TextBox>(OnIsNumericOnlyChanged);
        IsNumericDecimalOnlyProperty.Changed.AddClassHandler<TextBox>(OnIsNumericDecimalOnlyChanged);
    }

    public static bool GetIsNumericOnly(AvaloniaObject obj) => obj.GetValue(IsNumericOnlyProperty);

    public static void SetIsNumericOnly(AvaloniaObject obj, bool value) => obj.SetValue(IsNumericOnlyProperty, value);

    public static bool GetIsNumericDecimalOnly(AvaloniaObject obj) => obj.GetValue(IsNumericDecimalOnlyProperty);

    public static void SetIsNumericDecimalOnly(AvaloniaObject obj, bool value) => obj.SetValue(IsNumericDecimalOnlyProperty, value);

    private static void OnIsNumericDecimalOnlyChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is bool enabled)
        {
            if (enabled)
            {
                // If both are set, prefer decimal (it's a superset for digits).
                textBox.SetValue(ModeProperty, NumericMode.Decimal);
                Enable(textBox);
            }
            else
            {
                // If integer-only is still enabled, keep behavior active.
                if (textBox.GetValue(IsNumericOnlyProperty))
                {
                    textBox.SetValue(ModeProperty, NumericMode.Integer);
                    return;
                }

                textBox.SetValue(ModeProperty, NumericMode.None);
                Disable(textBox);
            }
        }
    }

    private static void OnIsNumericOnlyChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is bool enabled)
        {
            if (enabled)
            {
                // If decimal-only is also enabled, let that win.
                if (!textBox.GetValue(IsNumericDecimalOnlyProperty))
                {
                    textBox.SetValue(ModeProperty, NumericMode.Integer);
                }
                Enable(textBox);
            }
            else
            {
                // If decimal-only is still enabled, keep behavior active.
                if (textBox.GetValue(IsNumericDecimalOnlyProperty))
                {
                    textBox.SetValue(ModeProperty, NumericMode.Decimal);
                    return;
                }

                textBox.SetValue(ModeProperty, NumericMode.None);
                Disable(textBox);
            }
        }
    }

    private static void Enable(TextBox textBox)
    {
        textBox.PropertyChanged -= OnTextBoxPropertyChanged;
        textBox.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        textBox.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);

        textBox.PropertyChanged += OnTextBoxPropertyChanged;
        textBox.AddHandler(InputElement.TextInputEvent, OnTextInput, handledEventsToo: true);
        textBox.AddHandler(InputElement.KeyDownEvent, OnKeyDown, handledEventsToo: true);

        // Initialize last-good.
        var current = textBox.Text ?? string.Empty;
        if (IsAllowed(textBox, current))
        {
            textBox.SetValue(LastGoodTextProperty, current);
        }
        else
        {
            textBox.Text = string.Empty;
            textBox.SetValue(LastGoodTextProperty, string.Empty);
        }
    }

    private static void Disable(TextBox textBox)
    {
        textBox.PropertyChanged -= OnTextBoxPropertyChanged;
        textBox.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        textBox.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
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

        var mode = textBox.GetValue(ModeProperty);
        if (mode == NumericMode.None)
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
            if (textBox.SelectionStart != textBox.SelectionEnd)
            {
                var selectionStart = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
                if (selectionStart != 0)
                {
                    e.Handled = true;
                }
            }
            else
            {
                var caret = textBox.CaretIndex;
                if (caret != 0)
                {
                    e.Handled = true;
                }
            }
        }

        // In integer mode, block decimal separators.
        if (mode == NumericMode.Integer && (e.Key == Key.OemPeriod || e.Key == Key.Decimal || e.Key == Key.OemComma))
        {
            e.Handled = true;
        }
    }

    private static void OnTextChanged(TextBox textBox, string? text)
    {
        var current = text ?? string.Empty;
        if (IsAllowed(textBox, current))
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

    private static bool IsAllowed(TextBox textBox, string text)
    {
        var mode = textBox.GetValue(ModeProperty);
        return mode switch
        {
            NumericMode.Decimal => DecimalRegex.IsMatch(text),
            NumericMode.Integer => IntegerRegex.IsMatch(text),
            _ => true,
        };
    }

    private static bool IsAllowedInsertion(TextBox textBox, string? inserted)
    {
        if (string.IsNullOrEmpty(inserted))
        {
            return true;
        }

        var mode = textBox.GetValue(ModeProperty);
        if (mode == NumericMode.None)
        {
            return true;
        }

        if (mode == NumericMode.Decimal)
        {
            return IsAllowedInsertionDecimal(textBox, inserted);
        }

        // Integer mode: validate prospective text (handles paste and selection replacement cleanly).
        foreach (var ch in inserted)
        {
            if (char.IsDigit(ch) || ch == '-')
            {
                continue;
            }

            return false;
        }

        var prospective = BuildProspectiveText(textBox, inserted);
        return IntegerRegex.IsMatch(prospective);
    }

    private static bool IsAllowedInsertionDecimal(TextBox textBox, string inserted)
    {
        // Allow digits, optional leading '-', and a single '.' or ',' anywhere after the sign.
        foreach (var ch in inserted)
        {
            if (char.IsDigit(ch))
            {
                continue;
            }

            if (ch is '.' or ',')
            {
                continue;
            }

            if (ch == '-')
            {
                continue;
            }

            return false;
        }

        // Validate the prospective text (keeps copy/paste sane).
        var prospective = BuildProspectiveText(textBox, inserted);
        return DecimalRegex.IsMatch(prospective);
    }

    private static string BuildProspectiveText(TextBox textBox, string inserted)
    {
        var current = textBox.Text ?? string.Empty;

        // If there's a selection, assume it will be replaced.
        var start = Math.Clamp(Math.Min(textBox.SelectionStart, textBox.SelectionEnd), 0, current.Length);
        var end = Math.Clamp(Math.Max(textBox.SelectionStart, textBox.SelectionEnd), 0, current.Length);

        if (end > start)
        {
            current = current.Remove(start, end - start);
            var caret = Math.Clamp(start, 0, current.Length);
            return current.Insert(caret, inserted);
        }

        var caretIndex = Math.Clamp(textBox.CaretIndex, 0, current.Length);
        return current.Insert(caretIndex, inserted);
    }
}
