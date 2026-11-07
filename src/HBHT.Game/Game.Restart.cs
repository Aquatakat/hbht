using HBHT.Core;
using HBHT.Plugins;
using HBHT.Plugins.Saves;
using Godot;

namespace HBHT.Game;

/// <summary>
/// A change of renderer (ADR 0038): Godot can't change its renderer while it runs, so the game saves where it is, restarts on the
/// renderer chosen, and comes back to the options' Renderer, saying how it went.
/// </summary>
public partial class Game
{
    /// <summary>The save a change of renderer restarts from: in the data folder, apart from the player's saves, and gone once read.</summary>
    private static string RestartSave => Path.Combine(DataPaths.Root, "restart" + SaveFile.Extension);

    /// <summary>
    /// The options' Renderer chose another (<see cref="Transition.Restart"/>): the game is saved to <see cref="RestartSave"/>, unless
    /// it's the title's menu, and the program quits to start again on the renderer, with Godot's own options as they were but none
    /// of the game's, which would play again over the save. The choice is kept only once the new run has started on it
    /// (<see cref="ComeBack"/>), so a renderer that can't start leaves the next launch on the one that could.
    /// </summary>
    private void Restart(Transition.Restart restart)
    {
        List<string> args = [.. Renderers.GodotArgs(OS.GetCmdlineArgs()), "--rendering-method", Renderers.Name(restart.Renderer), "--"];
        if (restart.FromTitle)
        {
            args.AddRange(["--title", "logo"]);
        }
        else
        {
            if (SaveTo(RestartSave, null) is { } error)
            {
                Say($"The game couldn't be saved to change the renderer: {error}");
                return;
            }
            args.AddRange(["--save", RestartSave]);
        }
        args.AddRange(["--restarted", restart.Renderer.ToString()]);
        GD.Print($"Restarting on {restart.Renderer.Name()}: {string.Join(' ', args)}");
        OS.SetRestartOnExit(true, [.. args]);
        Quit();
    }

    /// <summary>
    /// The run a change of renderer started (<c>--restarted</c>): the renderer is kept as the player's if Godot started it, the save it
    /// came back from goes, and the options open at the Renderer, saying whether it changed, over trek or over the title.
    /// </summary>
    private void ComeBack(Renderer asked, LaunchOptions options)
    {
        var running = Renderers.Running;
        if (running == asked && _settings.Renderer != asked)
        {
            _settings.Renderer = asked;
            _settings.Save(DataPaths.Settings);
            Renderers.Choose(asked);
        }
        if (options.Save is { } save && Path.GetFullPath(save) == Path.GetFullPath(RestartSave))
        {
            try
            {
                File.Delete(save);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"Couldn't delete {save}: {e.Message}");
            }
        }
        ShowRestarted(asked, running, tries: 120);
    }

    /// <summary>
    /// Opens the options at the Renderer, over the title's menu or trek's, once whichever the game came back to can: the title as it
    /// shows, trek once it may open its menu. Gives up after so many frames, the choice kept all the same.
    /// </summary>
    private void ShowRestarted(Renderer asked, Renderer running, int tries)
    {
        if (_session is not { } session)
            return;
        if (session.Title is { } title)
        {
            title.ShowMenu().ShowRestarted(asked, running);
            return;
        }
        if (session.OpenRestarted(asked, running) || tries <= 0)
            return;
        GetTree().ProcessFrame += Retry;

        void Retry()
        {
            GetTree().ProcessFrame -= Retry;
            ShowRestarted(asked, running, tries - 1);
        }
    }
}
