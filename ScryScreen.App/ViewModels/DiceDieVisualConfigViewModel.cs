using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScryScreen.App.ViewModels;

public partial class DiceDieVisualConfigViewModel : ObservableObject
{
    private readonly Action _onChanged;

    public DiceDieVisualConfigViewModel(int sides, double dieScale, double numberScale, Action onChanged)
    {
        Sides = sides;
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        this.dieScale = dieScale;
        this.numberScale = numberScale;
    }

    public int Sides { get; }

    public string DisplayName => Sides == 100 ? "d100" : $"d{Sides}";

    [ObservableProperty]
    private double dieScale = 1.0;

    [ObservableProperty]
    private double numberScale = 1.0;

    partial void OnDieScaleChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.5, 1.75);
        if (Math.Abs(clamped - value) > 1e-9)
        {
            DieScale = clamped;
            return;
        }

        _onChanged();
    }

    partial void OnNumberScaleChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.5, 2.0);
        if (Math.Abs(clamped - value) > 1e-9)
        {
            NumberScale = clamped;
            return;
        }

        _onChanged();
    }
}
