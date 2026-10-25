using HBHT.Plugins;
using Godot;

namespace HBHT.Views;

/// <summary>
/// What the game and the editor know of each other and of where they run (docs/systems/runtime.md#the-data-folder): the app folder
/// that a portable install keeps its data in, and starting the other app, exported or in development.
/// </summary>
/// <remarks>
/// Exported, the two are side by side: on Windows and Linux one folder holds <c>hbht(.exe)</c> and <c>hbht-editor(.exe)</c>, each
/// with its <c>.pck</c>; on macOS <c>HBHT.app</c> and <c>HBHT Editor.app</c> stand side by side. In development both run under the
/// Godot editor's binary, each from its project in the repository's <c>src</c>.
/// </remarks>
public static class AppLauncher
{
    /// <summary>The editor's option, after <c>--</c>, when the game started it for an import: it then offers to play.</summary>
    public const string FromGame = "from-game";

    private const string GameName = "hbht", EditorName = "hbht-editor";
    private const string GameBundle = "HBHT.app", EditorBundle = "HBHT Editor.app";
    private const string GameProject = "HBHT.Game", EditorProject = "HBHT.Editor";

    /// <summary>Whether this is an exported app, rather than a project run by the Godot editor's binary.</summary>
    public static bool Exported => OS.HasFeature("template");

    private static bool MacOS => OS.HasFeature("macos");

    /// <summary>
    /// Hands <see cref="DataPaths"/> the folder the app runs from, when exported and not on macOS, so a portable install is found.
    /// Both apps call it before anything reads a path.
    /// </summary>
    public static void UseAppFolder()
    {
        if (Exported && !MacOS)
            DataPaths.UseAppFolder(Path.GetDirectoryName(OS.GetExecutablePath()));
    }

    /// <summary>Whether the command line's user arguments hold <c>--from-game</c>, with a value or without.</summary>
    public static bool StartedFromGame(string[] userArgs) => userArgs.Contains("--" + FromGame);

    /// <summary>Starts the editor, telling it the game sent it (<see cref="FromGame"/>), and says whether it started.</summary>
    public static bool LaunchEditor(bool fromGame) =>
        Launch(EditorName, EditorBundle, EditorProject, fromGame ? ["--" + FromGame, "1"] : []);

    /// <summary>Starts the game and says whether it started.</summary>
    public static bool LaunchGame() => Launch(GameName, GameBundle, GameProject, []);

    private static bool Launch(string name, string bundle, string project, string[] userArgs)
    {
        var (path, args) = Command(name, bundle, project, userArgs);
        if (path is null)
            return false;
        var pid = OS.CreateProcess(path, args);
        if (pid < 0)
        {
            GD.PrintErr($"Couldn't start {path} {string.Join(' ', args)}.");
            return false;
        }
        GD.Print($"Started {path} {string.Join(' ', args)} (process {pid}).");
        return true;
    }

    /// <summary>The executable and the arguments that start the other app, or a null path if it isn't where it should be.</summary>
    private static (string? Path, string[] Args) Command(string name, string bundle, string project, string[] userArgs)
    {
        var executable = OS.GetExecutablePath();
        // Godot takes the user's arguments after "--".
        string[] user = userArgs.Length > 0 ? ["--", .. userArgs] : [];
        if (!Exported)
        {
            // The project beside this one in the repository's src, run by the same Godot binary.
            var here = Path.TrimEndingDirectorySeparator(ProjectSettings.GlobalizePath("res://"));
            var other = Path.GetFullPath(Path.Combine(here, "..", project));
            return File.Exists(Path.Combine(other, "project.godot")) ? (executable, ["--path", other, .. user]) : Missing(other);
        }
        if (MacOS)
        {
            // <folder>/HBHT.app/Contents/MacOS/hbht: the folder the bundles stand in is four up from the binary.
            var bundles = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(executable))));
            var app = Path.Combine(bundles ?? "", bundle);
            string[] passed = user.Length > 0 ? ["--args", .. user] : [];
            return Directory.Exists(app) ? ("/usr/bin/open", ["-n", app, .. passed]) : Missing(app);
        }
        var sibling = Path.Combine(Path.GetDirectoryName(executable) ?? "", OS.HasFeature("windows") ? name + ".exe" : name);
        return File.Exists(sibling) ? (sibling, user) : Missing(sibling);
    }

    private static (string?, string[]) Missing(string where)
    {
        GD.PrintErr($"Couldn't find {where}.");
        return (null, []);
    }
}
