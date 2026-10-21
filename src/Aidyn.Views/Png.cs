using Godot;

namespace Aidyn.Views;

/// <summary>PNG files from the plugins' assets, as Godot images and textures.</summary>
public static class Png
{
    /// <summary>The image in a PNG file, or null if Godot can't read it.</summary>
    public static Image? Load(string path)
    {
        var image = new Image();
        return image.LoadPngFromBuffer(File.ReadAllBytes(path)) == Error.Ok ? image : null;
    }

    /// <summary>A texture of the image in a PNG file, or null if Godot can't read it.</summary>
    public static ImageTexture? Texture(string path) => Load(path) is { } image ? ImageTexture.CreateFromImage(image) : null;
}
