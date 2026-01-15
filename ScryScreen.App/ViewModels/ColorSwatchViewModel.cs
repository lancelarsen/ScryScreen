using Avalonia.Media;

namespace ScryScreen.App.ViewModels;

public sealed record ColorSwatchViewModel(string Hex)
{
    public IBrush Brush
    {
        get
        {
            try
            {
                return new SolidColorBrush(Color.Parse(Hex));
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
    }
}
