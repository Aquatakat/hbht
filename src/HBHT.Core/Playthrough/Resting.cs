using HBHT.Core.Characters;
using HBHT.Core.MapObjects;
using HBHT.Core.Ui;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>
/// The party camps when it's hurt, as a player does: hit points come back only by camping, resting at an inn, healing spells and
/// potions, and nine conversations ([camping](../../../docs/systems/camping.md)). While a living member is below
/// <see cref="Below"/> of their most, the pilot (<see cref="PilotOptions.Rest"/>) opens the pause menu on the World tab where the
/// player stands in trek on ground the terrain lets the party camp on, and this, a menu policy over another, chooses Camp: the
/// game's own camp follows (<see cref="Trek.TrekLoop.Camp"/>), its half heal and eight hours, or its ambush, which the battle pilot
/// plays.
/// </summary>
public sealed class Resting(IMenuPolicy inner) : IMenuPolicy
{
    /// <summary>What the menu does otherwise.</summary>
    public IMenuPolicy Inner { get; } = inner;

    /// <summary>A living member below this share of their most hit points makes the party camp.</summary>
    public float Below { get; init; } = 0.6f;

    /// <summary>The ticks after a camp before another.</summary>
    public int Cooldown { get; init; } = 300;

    /// <summary>How many times the party camped.</summary>
    public int Camps { get; private set; }

    private bool _wanted;
    private long _asked = -1;

    /// <summary>Whether a living member is below <see cref="Below"/> of their most hit points.</summary>
    public bool Hurt(Party party) => party.Members.Any(m => !m.IsDead && m.Hp < m.MaxHp * Below);

    /// <summary>
    /// Opens the pause menu on the World tab to camp, if the party is hurt, the player has the controls in trek, the terrain he stands on
    /// lets the party camp, and the last camp was <see cref="Cooldown"/> ago: true if it did, and the camp follows.
    /// </summary>
    public bool TryCamp(PlaythroughHost host)
    {
        var ticks = host.Clock.Ticks;
        if (_asked >= 0 && ticks - _asked < Cooldown)
            return false;
        _wanted = false;
        if (!host.PlayerHasControl || host.Session is not { Mode: GameMode.Trek } session || !Hurt(host.Game.Party))
            return false;
        var trek = session.Trek;
        if (trek.Chunks.ChunkAt(trek.Player.Sphere.Position) is not { } chunk
            || host.Game.Plugins.TryGet<TerrainRecord>(chunk.Terrain) is not { Camp: > 0 })
            return false;
        _wanted = true;
        _asked = ticks;
        session.OpenMenu(PauseTab.World);
        return true;
    }

    public MenuAction Press(PlaythroughHost host, PauseMenu menu)
    {
        if (!_wanted || menu.Tab != PauseTab.World)
            return Inner.Press(host, menu);
        var world = menu.World;
        if (world.Message is not null || world.Journal is not null)
        {
            // Refused after all: the message closed, the menu left.
            _wanted = false;
            return MenuAction.Accept;
        }
        if (world.Cursor != WorldChoice.Camp)
            return MenuAction.Up;
        _wanted = false;
        Camps++;
        return MenuAction.Accept;
    }

    public int? Popup(PlaythroughHost host, TrekPopup popup) => Inner.Popup(host, popup);
}
