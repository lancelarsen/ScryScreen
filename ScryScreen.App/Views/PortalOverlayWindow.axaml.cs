using Avalonia.Controls;

namespace ScryScreen.App.Views;

public partial class PortalOverlayWindow : Window
{
    public PortalOverlayWindow()
    {
        InitializeComponent();

        // Ensure this window never intercepts clicks/drag gestures.
        IsHitTestVisible = false;
        Focusable = false;
    }
}
