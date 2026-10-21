using Aidyn.Core.Characters;
using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core;

/// <summary>What carries out the game's transitions: the runtime, the editor or a test.</summary>
public interface ITransitionHost
{
    /// <summary>The game moves on as <paramref name="next"/> says; the rules it takes have already been carried out.</summary>
    void Transition(Transition next);
}

/// <summary>
/// Where the game goes as a mode, a menu or a script ends (ADR 0019): a window to open, a battle to start, a mode to show
/// or a word to say. The core decides each and carries out its rules, such as the hours a rest passes, and hands it to
/// the host in order (<see cref="ITransitionHost.Transition"/>), which shows it. <see cref="GameSession"/> and
/// <see cref="TrekLoop"/> send them.
/// </summary>
public abstract record Transition
{
    private Transition()
    {
    }

    /// <summary>A shop opens on the party, opened by <see cref="Items.Shop.Open"/>: the pause menu in its barter state (<c>InitShop</c>).</summary>
    public sealed record OpenShop(Shop Shop) : Transition;

    /// <summary>Training opens at a trainer: the pause menu in its training state (<c>init_skill_trainer</c>).</summary>
    public sealed record OpenTraining(Trainer Trainer) : Transition;

    /// <summary>
    /// A battle is to start, as a script's end or a map object decides it. Trek cues it (<see cref="TrekLoop.CueBattle"/>),
    /// so a host sees it only where nothing plays it, as on the editor's script page.
    /// </summary>
    public sealed record StartBattle(BattleSetup Setup) : Transition;

    /// <summary>A battle has begun on its arena, fading in (<c>Combat.Init</c>, <see cref="GameSession.BeginBattle"/>).</summary>
    public sealed record StartedBattle(Battle Battle, ZoneEngine Arena) : Transition;

    /// <summary>
    /// The battle's pause asks for its pause menu (combat's mode 3): from a turn (state 14) with the Party tab, and Flee if
    /// <paramref name="CanFlee"/>; from any other state (27), the System tab alone.
    /// </summary>
    public sealed record PauseBattle(bool PartyTab, bool CanFlee) : Transition;

    /// <summary>A battle is over and its arena gone (<c>Combat.Exit</c>): trek is about to be built again (<see cref="ResumeTrek"/> follows).</summary>
    public sealed record EndBattle(Battle Battle) : Transition;

    /// <summary>
    /// A battle was won and its arena is gone (game mode 6): the pause menu opens over black on the post-battle window (pause
    /// state 5), then the loot window (6), and trek is built again where the battle began once it closes
    /// (<see cref="GameSession.MenuClosed"/>).
    /// </summary>
    public sealed record PostBattle(BattleSpoils Spoils) : Transition;

    /// <summary>
    /// The game is over (a trigger's <see cref="TriggerEffect.GameOver"/>, as the game over's death scene ends, or the ending's
    /// credits): the original goes back to the title, and the runtime loads the last save, the user's choice.
    /// </summary>
    public sealed record GameOver : Transition;

    /// <summary>The party camped, as the World tab's Camp asked (<see cref="TrekLoop.Camp"/>).</summary>
    public sealed record Camp(CampOutcome Outcome) : Transition;

    /// <summary>Hours passed at once (<see cref="GameState.Lapse"/>), after a camp heal or a camp scene.</summary>
    public sealed record Rest(int Hours) : Transition;

    /// <summary>Something to tell the player, such as that a keeper keeps no shop.</summary>
    public sealed record Say(string Text) : Transition;

    /// <summary>A cut-scene has begun on its stage, its first nodes run (<c>DialogueModeInitPrescripted</c>).</summary>
    public sealed record StartCutScene(CutScene Scene, ZoneEngine Stage) : Transition;

    /// <summary>
    /// A cut-scene is over and its stage gone, or one couldn't start (<paramref name="Scene"/> null): trek is about to be
    /// built again (<see cref="ResumeTrek"/> follows).
    /// </summary>
    public sealed record EndCutScene(CutScene? Scene) : Transition;

    /// <summary>Trek is back where the player stood, its world built again and fading in (<see cref="TrekLoop.Resume"/>).</summary>
    public sealed record ResumeTrek : Transition;

    /// <summary>
    /// The credits play over trek held still (game mode 0x13); the ending's (<paramref name="Ending"/>) can't be skipped, end
    /// with "The End", and end the game (<see cref="GameOver"/> follows them).
    /// </summary>
    public sealed record PlayCredits(bool Ending = false) : Transition;

    /// <summary>A cinematic plays over everything (game mode 0xE).</summary>
    public sealed record PlayCinematic(CinematicRecord Cinematic) : Transition;

    /// <summary>The pause menu opens again, on the Theatre's page or as it last was (trek's state 8, <c>Theatre.Reopen</c>).</summary>
    public sealed record ReopenMenu(bool Theatre) : Transition;

    /// <summary>
    /// What a script's end leads to (<c>dialoug_func</c>'s ends): the hours a camp heal or a camp scene passes, then the
    /// keeper's shop or training, or its battle. It decides, and changes nothing; <see cref="CarryOut"/> passes the hours.
    /// </summary>
    /// <param name="keeper">Who keeps a shop or training it opens: the speaker at the end, or the one talking if they wander.</param>
    /// <param name="place">The chunk a battle would start in, and its terrain.</param>
    /// <param name="talker">The monster party talking, whose party a battle fights instead (<see cref="BattleSetup.Against"/>).</param>
    public static IReadOnlyList<Transition> AfterScript(GameState game, ScriptEnding ending, RecordId? keeper,
        (ChunkRef? Chunk, int? Terrain) place = default, (RecordId Id, MonsterPartyObject Party)? talker = null)
    {
        var next = new List<Transition>();
        if (ending.RestHours > 0)
            next.Add(new Rest(ending.RestHours));
        switch (ending.Kind)
        {
            case ScriptEndKind.Shop:
                next.Add(ShopOf(game, keeper));
                break;
            case ScriptEndKind.Train:
                next.Add(TrainingOf(game, keeper));
                break;
            case ScriptEndKind.Battle when ending.Battle is { } battle:
                battle = battle with { Place = place.Chunk, Terrain = place.Terrain };
                next.Add(new StartBattle(talker is var (id, party) ? battle.Against(id, party) : battle));
                break;
        }
        return next;
    }

    /// <summary>Passes the hours a <see cref="Rest"/> says (<see cref="GameState.Lapse"/>), and hands each transition to the host in turn.</summary>
    public static void CarryOut(GameState game, ITransitionHost host, IEnumerable<Transition> next)
    {
        foreach (var transition in next)
        {
            if (transition is Rest rest)
                game.Lapse(rest.Hours);
            host.Transition(transition);
        }
    }

    /// <summary>
    /// The keeper's first shop, opened on the party (<c>InitShop</c>). The original crashes when the keeper runs no shop,
    /// which no script in the game does; the port says so.
    /// </summary>
    private static Transition ShopOf(GameState game, RecordId? keeper)
    {
        var plugins = game.Plugins;
        var entity = keeper is { Type: RecordTypes.Entity } id ? plugins.TryGet<EntityRecord>(id) : null;
        if (entity is null || Shop.For(plugins.All<ShopRecord>(RecordTypes.Shop), entity.Id) is not { } record)
            return new Say($"{entity?.Name ?? keeper?.ToString() ?? "No one"} keeps no shop.");
        var party = game.Party;
        return new OpenShop(Shop.Open(record, entity, party, party.Inventory.Catalog, game.Flags));
    }

    /// <summary>
    /// Training with the keeper as the trainer (<c>init_skill_trainer</c>). The original opens it with empty lists when there's
    /// no keeper; the port says so.
    /// </summary>
    private static Transition TrainingOf(GameState game, RecordId? keeper)
    {
        if (keeper is not { Type: RecordTypes.Entity } id || game.Plugins.TryGet<EntityRecord>(id) is not { } entity)
            return new Say($"{keeper?.ToString() ?? "No one"} teaches nothing.");
        return new OpenTraining(Trainer.From(entity, game.Rules, game.Plugins.Get<SpellRecord>));
    }
}
