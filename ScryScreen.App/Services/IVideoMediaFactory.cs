namespace ScryScreen.App.Services;

public interface IVideoMediaFactory<TMedia>
{
    TMedia CreateFromPath(string filePath);

    void Dispose(TMedia media);
}
