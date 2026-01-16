using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.App.Models;

namespace ScryScreen.App.ViewModels;

public sealed partial class ConditionDefinitionViewModel : ViewModelBase
{
    public ConditionDefinitionViewModel(ConditionDefinition definition)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));

        Id = definition.Id;
        IsBuiltIn = definition.IsBuiltIn;
        IsManualOnly = definition.IsManualOnly;
        Name = definition.Name;
        ColorHex = definition.ColorHex;
    }

    public Guid Id { get; }

    public bool IsBuiltIn { get; }

    public bool IsCustom => !IsBuiltIn;

    public bool IsManualOnly { get; }

    public bool CanEditIdentity => IsCustom;

    public string DisplayName => IsBuiltIn ? Name : $"*{Name}";

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string colorHex = "#FFFFFFFF";

    public IBrush ColorBrush
    {
        get
        {
            if (TryParseColor(ColorHex, out var color))
            {
                return new SolidColorBrush(color);
            }

            return Brushes.Transparent;
        }
    }

    public ConditionDefinition ToModel()
        => new(Id, Name, ColorHex, IsBuiltIn, IsManualOnly);

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnColorHexChanged(string value) => OnPropertyChanged(nameof(ColorBrush));

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            color = Color.Parse(hex.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }
}
