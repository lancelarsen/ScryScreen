namespace ScryScreen.App.Models;

public enum MapMasterMaskType
{
    Black,
    Dirt,
    FogNight,
    Fog,
    Rock1,
    Rock2,

    // Legacy (kept for backwards-compatibility with persisted sessions)
    Stone,
}
