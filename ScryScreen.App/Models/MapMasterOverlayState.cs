using Avalonia.Media.Imaging;

namespace ScryScreen.App.Models;

public sealed record MapMasterOverlayState(WriteableBitmap MaskBitmap, double OverlayOpacity);
