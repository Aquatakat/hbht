namespace Aidyn.Plugins.Records;

/// <summary>
/// The sounds engine code plays (<c>aidyn:sounds/0</c>, the only one): the menus', the scripts', the containers', the
/// weather's, and the title's and credits' music. Each is an <c>aidyn:sequence/…</c>. Which music a map set plays is
/// the map set's (<see cref="MapSetRecord.Music"/>, <see cref="ChunkEntry.Music"/>).
/// </summary>
/// <param name="Script">What a script's <c>PlaySound</c> plays, by its operand (<c>dialoug_SFX</c>).</param>
/// <param name="Experience">A script gives a party member experience.</param>
/// <param name="GoldGained">Gold is gained: a script's, a container's or a sale's.</param>
/// <param name="GoldPaid">Gold is paid.</param>
/// <param name="Containers">A container opening, by its kind.</param>
/// <param name="Explosions">A trapped container exploding: one at random.</param>
/// <param name="Rain">Plays while it rains.</param>
/// <param name="Thunder">One at random as lightning flashes.</param>
/// <param name="Title">The title screen's music.</param>
/// <param name="Credits">The credits' music.</param>
/// <param name="Kinds">
/// The bodies' kinds (<see cref="BodySounds.Kind"/>): how loud their footsteps are and which they make. A kind past
/// the list's end, or null, makes none.
/// </param>
public sealed record SoundsRecord(
    RecordId Id,
    MenuSounds Menu,
    IReadOnlyList<RecordId> Script,
    RecordId Experience,
    RecordId GoldGained,
    RecordId GoldPaid,
    IReadOnlyList<RecordId> Containers,
    IReadOnlyList<RecordId> Explosions,
    RecordId Rain,
    IReadOnlyList<RecordId> Thunder,
    RecordId Title,
    RecordId Credits,
    IReadOnlyList<BodyKind?> Kinds);

/// <summary>
/// A point in a body's animation that plays a sound (an audio key, <c>AudioKeys.PlayEntityKey</c>): on the tick the
/// animation started by <paramref name="Request"/> reaches <c>Frac / 65500</c> of its length, at least its first frame.
/// </summary>
/// <param name="Sounds">One plays at random. With none, the body's weapon's sound plays, in combat.</param>
public sealed record SoundKey(int Frac, int Request, IReadOnlyList<RecordId> Sounds);

/// <summary>A body's sounds: its kind, which its footsteps go by, and its voice's and weapon's keys.</summary>
/// <param name="Kind">An index into <see cref="SoundsRecord.Kinds"/>. Kind 0 makes no sound at all.</param>
public sealed record BodySounds(int Kind, IReadOnlyList<SoundKey> Keys);

/// <summary>A kind of body's footsteps (<c>AudioKeys.PlaySurfaceSteps</c>).</summary>
/// <param name="Loudness">Scales them, 0 to 1.</param>
/// <param name="Surfaces">By the collision surface last walked on.</param>
public sealed record BodyKind(float Loudness, IReadOnlyList<FootstepSurface> Surfaces);

/// <summary>A surface's footsteps: its keys for sneaking, walking and running, null for none.</summary>
public sealed record FootstepSurface(SoundKey? Sneak, SoundKey? Walk, SoundKey? Run)
{
    public IEnumerable<SoundKey> Keys => new[] { Sneak, Walk, Run }.OfType<SoundKey>();
}

/// <summary>The menus' sounds.</summary>
/// <param name="Cursor">Up or down in the pause menu and the title's.</param>
/// <param name="PageCursor">A page's own cursor: the Party tab's lists changing, the World tab's cursor, an option changing.</param>
/// <param name="Accept">A is pressed, whatever it does.</param>
/// <param name="Back">B or Start is pressed.</param>
/// <param name="TabBack">
/// The pause menu's tabs slide back, or in as it opens on the System or Party tab; a popup or an item's details open.
/// </param>
/// <param name="TabForward">
/// The tabs slide forward, or in as the menu opens on the World tab; a popup or an item's details close.
/// </param>
/// <param name="Member">The party member shown changes.</param>
/// <param name="LevelUp">The level-up page shows a member gone up a level.</param>
/// <param name="CheatAccepted">A cheat code is accepted.</param>
/// <param name="CheatRejected">A cheat code is rejected.</param>
public sealed record MenuSounds(
    RecordId Cursor,
    RecordId PageCursor,
    RecordId Accept,
    RecordId Back,
    RecordId TabBack,
    RecordId TabForward,
    RecordId Member,
    RecordId LevelUp,
    RecordId CheatAccepted,
    RecordId CheatRejected);
