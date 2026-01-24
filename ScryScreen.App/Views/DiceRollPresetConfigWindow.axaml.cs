using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ScryScreen.App.ViewModels;

namespace ScryScreen.App.Views;

public partial class DiceRollPresetConfigWindow : Window
{
    public event EventHandler? DeleteRequested;

    private readonly string _originalExpression = string.Empty;
    private readonly string _originalName = string.Empty;

    public DiceRollPresetConfigWindow()
    {
        InitializeComponent();
    }

    public DiceRollPresetConfigWindow(DiceRollPresetViewModel preset)
    {
        InitializeComponent();
        DataContext = preset;

        _originalExpression = preset.Expression;
        _originalName = preset.DisplayName;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (DataContext is DiceRollPresetViewModel preset)
        {
            var expr = (preset.Expression ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(expr))
            {
                preset.Expression = _originalExpression;
                return;
            }

            preset.Expression = expr;

            var name = (preset.DisplayName ?? string.Empty).Trim();
            preset.DisplayName = string.IsNullOrWhiteSpace(name) ? expr : name;
        }

        Close();
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DeleteRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
