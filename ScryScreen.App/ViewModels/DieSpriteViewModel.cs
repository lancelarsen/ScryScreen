using CommunityToolkit.Mvvm.ComponentModel;

namespace ScryScreen.App.ViewModels;

public sealed class DieSpriteViewModel : ObservableObject
{
    private double _x;
    private double _y;
    private double _rotationDeg;
    private double _scale = 1;
    private double _opacity = 1;

    public required int Value { get; init; }
    public required int Sides { get; init; }

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public double RotationDeg
    {
        get => _rotationDeg;
        set => SetProperty(ref _rotationDeg, value);
    }

    public double Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, value);
    }
}
