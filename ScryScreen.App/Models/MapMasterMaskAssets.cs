using System;
using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ScryScreen.App.Models;

public static class MapMasterMaskAssets
{
    private static readonly ConcurrentDictionary<MapMasterMaskType, IImage?> _cache = new();

    public static IImage? GetTexture(MapMasterMaskType type) => _cache.GetOrAdd(type, static t => Load(t));

    private static IImage? Load(MapMasterMaskType type)
    {
        var uri = type switch
        {
            MapMasterMaskType.Black => null,
            MapMasterMaskType.Dirt => new Uri("avares://ScryScreen.App/Assets/Images/Background%20Dirt.png"),
            MapMasterMaskType.FogNight => new Uri("avares://ScryScreen.App/Assets/Images/Background%20Fog%20Night.png"),
            MapMasterMaskType.Fog => new Uri("avares://ScryScreen.App/Assets/Images/Background%20Fog.png"),
            MapMasterMaskType.Rock1 => new Uri("avares://ScryScreen.App/Assets/Images/Background%20Rock%201.png"),
            MapMasterMaskType.Rock2 => new Uri("avares://ScryScreen.App/Assets/Images/Background%20Rock%202.png"),

            // Legacy mapping
            MapMasterMaskType.Stone => new Uri("avares://ScryScreen.App/Assets/Images/Background%20Rock%201.png"),

            _ => null,
        };

        if (uri is null)
        {
            return null;
        }

        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }
}
