using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>Where a battle comes from.</summary>
public enum BattleSource
{
    /// <summary>A wandering monster party caught the party in trek (<c>WanderTick</c>).</summary>
    Wanderer,

    /// <summary>The player used a monster party with no dialogue, which fights (<c>monsterpary_func</c>).</summary>
    MonsterParty,

    /// <summary>The party was ambushed as it camped (<c>Party::CampAmbushCheck</c>, trek state 2 after the camp, <c>0x80004ac0</c>).</summary>
    Camp,

    /// <summary>A script's battle op (<c>encounterDat_func</c>), in a conversation or a cut-scene.</summary>
    Script,
}

/// <summary>
/// What a battle needs to start, the same from each place one starts: a wanderer catching the party, a monster party used,
/// a camp ambush and a script. Combat (M3) fills in the rest as it builds the battle: how many of each enemy come between
/// their minimum and maximum, a camp ambush's enemies from the camp encounters by the terrain and the party's level, the
/// arena by the chunk, the bosses' flags by the enemies' records (in the first four slots), and the flee rules
/// (<see cref="CanFlee"/>), all in <c>docs/hardcoded-content.md</c>.
/// </summary>
/// <param name="Source">Where it comes from.</param>
/// <param name="Place">The chunk it starts in: where the player stands, or a cut-scene's stage.</param>
/// <param name="Terrain">That chunk's terrain, by which a camp ambush's enemies are chosen.</param>
/// <param name="Party">
/// The monster party fought (<c>aidyn:voxel/…</c>): the one that caught the party or was used, or the one talking in a
/// conversation whose script starts the battle, as the original fights the talker's party instead.
/// </param>
/// <param name="Enemies">
/// Who fights: a monster party's kinds of enemy, each with how many always come and how many can, or a script's
/// <see cref="DialogueCommand.AddEnemy"/>s in order, one each. Empty for a camp ambush until the camp encounters are
/// imported.
/// </param>
/// <param name="Arena">A script's arena (<see cref="DialogueCommand.SetBattlefield"/>, 0 when it sets one above 27), or null.</param>
/// <param name="Ambush">Whether the enemies came at the party: a wanderer catching it, or a camp ambush.</param>
/// <param name="CanFlee">
/// Whether the party may flee before the bosses and encounter flags have their say: monster parties' battles yes, camp
/// ambushes no, scripts' unless the script is a cut-scene.
/// </param>
/// <param name="EncounterFlag">
/// The battle's encounter flag, which its special rules key on: a fighting monster party's used flag, which winning sets;
/// a talking party's flag C (<see cref="MonsterPartyObject.BattleFlag"/>), which winning sets too; or a script's
/// <see cref="DialogueRecord.BattleFlag"/>, which a win doesn't set.
/// </param>
/// <param name="CollisionByte">A script's <see cref="DialogueCommand.SetCollideByte"/>.</param>
/// <param name="AniByte">A script's <see cref="DialogueCommand.SetAniByte"/>.</param>
public sealed record BattleSetup(
    BattleSource Source,
    ChunkRef? Place,
    int? Terrain,
    RecordId? Party,
    IReadOnlyList<PartyEnemy> Enemies,
    int? Arena,
    bool Ambush,
    bool CanFlee,
    RecordId? EncounterFlag,
    int CollisionByte = 0,
    int AniByte = 0)
{
    /// <summary>
    /// A battle with a monster party (<paramref name="id"/>, <c>aidyn:voxel/…</c>): its enemies, fleeing allowed, and its
    /// flag C if it talks, or else its used flag.
    /// </summary>
    public static BattleSetup WithParty(BattleSource source, RecordId id, MonsterPartyObject party, ChunkRef? place, int? terrain, bool ambush) =>
        new(source, place, terrain, id, party.Enemies, null, ambush, CanFlee: true, party.Dialogue is null ? party.Used?.Flag : party.BattleFlag);

    /// <summary>A camp ambush where the party camped: no fleeing (<c>Party::CampAmbushCheck</c>).</summary>
    public static BattleSetup CampAmbush(ChunkRef? place, int? terrain) =>
        new(BattleSource.Camp, place, terrain, null, [], null, Ambush: true, CanFlee: false, null);

    /// <summary>A script's battle (<c>encounterDat_func</c>): its enemies one each, its arena and bytes, and its battle flag.</summary>
    public static BattleSetup Scripted(IEnumerable<RecordId> enemies, int arena, int collisionByte, int aniByte, bool canFlee, RecordId? flag) =>
        new(BattleSource.Script, null, null, null, [.. enemies.Select(e => new PartyEnemy(e, 1, 1))], arena, Ambush: false, canFlee, flag,
            collisionByte, aniByte);

    /// <summary>The script's battle in a conversation with someone who wanders: the original fights their party instead, under its flag C.</summary>
    public BattleSetup Against(RecordId id, MonsterPartyObject party) =>
        this with { Party = id, Enemies = party.Enemies, EncounterFlag = party.BattleFlag };
}
