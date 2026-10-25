using HBHT.Core;
using HBHT.Core.Characters;
using HBHT.Core.Flags;
using HBHT.Core.Sky;
using HBHT.Plugins;
using HBHT.Plugins.Saves;
using HBHT.Rom.Rig;

namespace HBHT.Editor;

/// <summary>
/// The game the editor shows (the core's <see cref="GameState"/>): a new game's, a checkpoint's or a
/// save's. Flag pages show and set its flags, scripts play against it and change it, and the pages
/// that try things on its party use copies. It can be saved, for the runtime to start from.
/// </summary>
public sealed class EditorGame
{
    private readonly PluginSet _plugins;
    /// <summary>The party as it was loaded, which resetting it goes back to.</summary>
    private SaveParty _loadedParty;

    /// <summary>A new game. Throws if the plugins are older than the records a game needs.</summary>
    public EditorGame(PluginSet plugins)
    {
        _plugins = plugins;
        Game = GameState.NewGame(plugins);
        _loadedParty = Game.SaveParty();
        Sky = SkyOver(Game);
    }

    public GameState Game { get; private set; }

    /// <summary>
    /// The sky over where the game stands, at its time of day, which lights the previews' lit display lists as the RSP does
    /// (<see cref="Views.N64Material.SetSkyLight"/>), as trek's sky lights the runtime's.
    /// </summary>
    public SkyDirector Sky { get; private set; }

    public FlagNetwork Network => Game.Flags;

    /// <summary>What the game is, such as "a new game" or a checkpoint's label.</summary>
    public string Name { get; private set; } = "a new game";

    /// <summary>Whether it's a new game's, so pages call its party "New game Alaron".</summary>
    public bool IsNewGame { get; private set; } = true;

    /// <summary>
    /// After loading a checkpoint, how the port's derived flags compare with the console's; after
    /// loading a save, what it couldn't use.
    /// </summary>
    public string? Check { get; private set; }

    /// <summary>The console's values, from the loaded checkpoint.</summary>
    public IReadOnlyList<bool>? Console { get; private set; }

    public void NewGame()
    {
        Start(GameState.NewGame(_plugins), "a new game", isNew: true);
        Check = null;
    }

    /// <summary>
    /// A checkpoint (an ares state, see docs/reference-rig.md), as a save made from it loads
    /// (<see cref="CheckpointSave"/>), and how the port's derived flags compare with the console's. A
    /// state with no party gives a new game's party with the checkpoint's plain flags.
    /// </summary>
    public void LoadCheckpoint(string path)
    {
        var checkpoint = AresCheckpoint.Read(path);
        var console = checkpoint.Flags;
        var label = CheckpointSave.Label(path);
        GameState game;
        try
        {
            game = GameState.Load(_plugins, CheckpointSave.Convert(checkpoint, _plugins, label));
        }
        catch (InvalidDataException)
        {
            game = GameState.NewGame(_plugins);
            game.Flags.Restore(h => h < console.Count && console[h]);
        }
        Start(game, label, isNew: false);
        Console = console;
        var derived = Enumerable.Range(0, Math.Min(console.Count, Network.Count)).Where(Network.IsDerived).ToList();
        var wrong = derived.Where(h => Network.Peek(h) != console[h]).ToList();
        Check = wrong.Count == 0
            ? $"The network reproduces all {derived.Count:N0} of the checkpoint's derived flags. {console.Count(v => v):N0} flags are set."
            : $"{wrong.Count:N0} of {derived.Count:N0} derived flags differ from the console's: {string.Join(", ", wrong.Take(20))}{(wrong.Count > 20 ? "…" : "")}";
    }

    /// <summary>A save in the port's format.</summary>
    public void LoadSave(string path)
    {
        var game = GameState.Load(_plugins, SaveFile.Read(path));
        Start(game, game.Label ?? Path.GetFileNameWithoutExtension(path), isNew: false);
        Check = game.Problems.Count == 0 ? null : $"Loading it couldn't use: {string.Join(" ", game.Problems)}";
    }

    /// <summary>Saves the game as it stands, flags, party and all, where the save it came from stood.</summary>
    public void Save(string path)
    {
        Game.Label = Name;
        SaveFile.Write(path, Game.Save());
    }

    /// <summary>Puts the party back as it was loaded.</summary>
    public void ResetParty() => Game.LoadParty(_loadedParty);

    /// <summary>A copy of the party to try things on, which doesn't write the flags.</summary>
    public Party CopyParty() => Game.CopyParty();

    /// <summary>After a flag is set or cleared by hand.</summary>
    public void Edited()
    {
        if (!Name.EndsWith(", edited", StringComparison.Ordinal))
            Name += ", edited";
    }

    private void Start(GameState game, string name, bool isNew)
    {
        Game = game;
        Sky = SkyOver(game);
        _loadedParty = game.SaveParty();
        (Name, IsNewGame, Console) = (name, isNew, null);
    }

    /// <summary>The sky of the chunk the game stands in, chosen at once, as a save's or a new game's start chooses it.</summary>
    private static SkyDirector SkyOver(GameState game)
    {
        var sky = new SkyDirector(game.Plugins, game.Clock, game.Weather, game.Random.Twinkle);
        var start = GameStart.For(game);
        sky.Enter(start.Set.Chunks.FirstOrDefault(c => c.Name == start.Chunk));
        sky.Apply();
        return sky;
    }
}
