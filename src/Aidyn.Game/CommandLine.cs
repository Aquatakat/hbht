using System.Globalization;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The options both Godot projects take after <c>--</c>: <c>--name value</c> pairs (<see cref="Parse"/>), and the
/// screenshot run the runtime and the editor share, which waits for the frames to draw, saves the window and quits,
/// giving up after a minute so a hang can't block a script.
/// </summary>
public static class CommandLine
{
    /// <summary>The <c>--name value</c> pairs in the arguments. Anything else is skipped.</summary>
    public static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                options[args[i][2..]] = args[i + 1];
        }
        return options;
    }

    /// <summary>A list of numbers joined with commas, such as <c>16,62</c>.</summary>
    public static float[] Floats(string text) =>
        [.. text.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture))];

    /// <summary>In screenshot mode, quits with an error if the screenshot hasn't been taken within <paramref name="seconds"/>.</summary>
    public static void Watchdog(SceneTree tree, double seconds) =>
        tree.CreateTimer(seconds).Timeout += () =>
        {
            GD.PrintErr("Timed out before the screenshot.");
            tree.Quit(2);
        };

    /// <summary>Waits for <paramref name="count"/> frames to be drawn.</summary>
    public static async Task Frames(GodotObject waiter, int count)
    {
        for (var i = 0; i < count; i++)
            await waiter.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    /// <summary>Saves an image as a PNG and says whether it could.</summary>
    public static Error Save(Image image, string path)
    {
        var error = image.SavePng(path);
        GD.Print(error == Error.Ok ? $"Saved {path}" : $"Couldn't save {path}: {error}");
        return error;
    }
}
