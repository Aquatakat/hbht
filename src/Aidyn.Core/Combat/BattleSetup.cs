using Aidyn.Core.Rng;
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
/// <see cref="DialogueCommand.AddEnemy"/>s in order, one each, or a camp ambush's two kinds.
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
    /// <summary>A monster party's least and most enemies (<see cref="MonsterPartyObject.MinSize"/>, <see cref="MonsterPartyObject.MaxSize"/>).</summary>
    public (int Min, int Max) Size { get; init; } = (0, Battle.MaxEnemies);

    /// <summary>The share of each kind the level trim drops, by the party's level over the enemies' in thirds (<c>0x800CD470</c>).</summary>
    private static readonly float[] LevelTrim = [0.3f, 0.2f, 0.1f, 0f];

    /// <summary>
    /// A battle with a monster party (<paramref name="id"/>, <c>aidyn:voxel/…</c>): its enemies, fleeing allowed, and its
    /// flag C if it talks, or else its used flag.
    /// </summary>
    public static BattleSetup WithParty(BattleSource source, RecordId id, MonsterPartyObject party, ChunkRef? place, int? terrain, bool ambush) =>
        new(source, place, terrain, id, party.Enemies, null, ambush, CanFlee: true, party.Dialogue is null ? party.Used?.Flag : party.BattleFlag)
        {
            Size = (party.MinSize, party.MaxSize),
            BonusLoot = party.Loot,
            PartyGate = party.Visible,
        };

    /// <summary>
    /// A camp ambush where the party camped (<c>CampEncounters.Load</c>, <c>0x800096DC</c>): no fleeing, no loot of its own and no
    /// flag; its enemies the terrain's (<see cref="TerrainRecord.Ambushes"/>) at the tier of the party's average level, dead
    /// members counted, rolled as a monster party's with its least and most the two kinds' summed. A terrain with none, as 27,
    /// has nobody to fight.
    /// </summary>
    public static BattleSetup CampAmbush(ChunkRef? place, int? terrain, PluginSet plugins, Characters.Party party)
    {
        var enemies = terrain is { } t && TerrainRecord.Find(plugins, t)?.Ambushes is { Count: > 0 } tiers
            ? tiers[Math.Min(AmbushTier(plugins, party.AverageLevel), tiers.Count - 1)].Enemies
            : [];
        return new(BattleSource.Camp, place, terrain, null, enemies, null, Ambush: true, CanFlee: false, null)
        {
            Size = (enemies.Sum(e => e.Min), enemies.Sum(e => e.Max)),
        };
    }

    /// <summary>
    /// The camp ambush's tier for the party's average level: 0 up to the camping record's first level (5), then one more past
    /// each of the others (10, 15 and 20).
    /// </summary>
    public static int AmbushTier(PluginSet plugins, int level) =>
        plugins.TrySingleton<CampingRecord>()?.AmbushLevels is { } levels ? levels.Count(l => level > l) : 0;

    /// <summary>
    /// What the monster party carries besides its enemies' chests (<c>0x800D9470</c>): a loot table rolled as a chest with no
    /// enemy, or one item. A script's battle and a camp ambush have none; the original keeps the last monster party's for a
    /// script's battle, which the port doesn't, the user's choice.
    /// </summary>
    public RecordId? BonusLoot { get; init; }

    /// <summary>
    /// The monster party's own gate, its flag A (<c>0x800D9482</c>, <c>0x800D9484</c>), which must still hold for a win to remember
    /// it when its encounter flag can't be set.
    /// </summary>
    public FlagGate? PartyGate { get; init; }

    /// <summary>A script's battle (<c>encounterDat_func</c>): its enemies one each, its arena and bytes, and its battle flag.</summary>
    public static BattleSetup Scripted(IEnumerable<RecordId> enemies, int arena, int collisionByte, int aniByte, bool canFlee, RecordId? flag) =>
        new(BattleSource.Script, null, null, null, [.. enemies.Select(e => new PartyEnemy(e, 1, 1))], arena, Ambush: false, canFlee, flag,
            collisionByte, aniByte);

    /// <summary>The script's battle in a conversation with someone who wanders: the original fights their party instead, under its flag C.</summary>
    public BattleSetup Against(RecordId id, MonsterPartyObject party) =>
        this with
        {
            Party = id, Enemies = party.Enemies, EncounterFlag = party.BattleFlag, Size = (party.MinSize, party.MaxSize),
            BonusLoot = party.Loot, PartyGate = party.Visible,
        };

    /// <summary>
    /// The enemy list, at most 12: a monster party's or a camp ambush's rolled (<see cref="FillEnemies"/>), or a script's as
    /// it gave them.
    /// </summary>
    /// <param name="party">The party, whose level the roll trims by.</param>
    /// <param name="level">An entity's level, by its record.</param>
    /// <param name="fallback">The entity a roll come to none fights (<see cref="CombatRecord.Fallback"/>).</param>
    public IReadOnlyList<RecordId> EnemyList(Characters.Party party, Func<RecordId, int> level, IRandom random, RecordId fallback) =>
        Party is not null || Source == BattleSource.Camp ? FillEnemies(party, level, random, fallback)
        : [.. Enemies.SelectMany(e => Enumerable.Repeat(e.Entity, e.Min)).Take(Battle.MaxEnemies)];

    /// <summary>
    /// A monster party's enemies (<c>BattleSetup.FillEnemies</c>, <c>0x80009374</c>): first each kind's least, in the kinds'
    /// order (<c>BattleSetup.SureKinds</c>, <c>0x80008EC8</c>); then, unless that reached the party's most, for each kind
    /// whose most is above its least, a roll of 0 to the difference more, in order (<c>BattleSetup.ChooseKinds</c>,
    /// <c>0x800090A4</c>), stopping at the party's most or 12, and none at all if it comes to fewer than the party's least.
    /// Then, where the party's level is below the enemies' average, each kind loses a share by how far below
    /// (<c>BattleSetup.TrimByLevel</c>, <c>0x800088E4</c>); and a list come to nothing is the <paramref name="fallback"/> alone, the
    /// combat record's Skeleton (<c>EntityList[0xBB]</c>).
    /// </summary>
    public IReadOnlyList<RecordId> FillEnemies(Characters.Party party, Func<RecordId, int> level, IRandom random, RecordId fallback)
    {
        var (least, most) = Size;
        var list = new List<RecordId>();
        foreach (var kind in Enemies)
        {
            for (var i = 0; i < kind.Min; i++)
            {
                list.Add(kind.Entity);
                if (list.Count >= most || list.Count >= Battle.MaxEnemies)
                    goto Sure;
            }
        }
    Sure:
        // Twelve sure ones under the party's most would have the original write a thirteenth; no party has so many.
        if (list.Count < most && list.Count < Battle.MaxEnemies)
        {
            foreach (var kind in Enemies)
            {
                if (kind.Min >= kind.Max)
                    continue;
                var extra = random.Range(0, kind.Max - kind.Min);
                for (var i = 0; i < extra; i++)
                {
                    list.Add(kind.Entity);
                    if (list.Count >= most || list.Count >= Battle.MaxEnemies)
                        goto Trim;
                }
            }
            if (list.Count < least)
                list.Clear();
        }
    Trim:
        TrimByLevel(list, party, level);
        return list.Count == 0 ? [fallback] : list;
    }

    /// <summary>
    /// <c>BattleSetup.TrimByLevel</c>: where the party's average level is below the enemies', each kind in order loses
    /// 0.3, 0.2 or 0.1 of its count, truncated, by the party's level × 3 over the enemies', from the front of the list. As
    /// the original's scan, an entry that moves up into a removed one's place is passed over.
    /// </summary>
    private void TrimByLevel(List<RecordId> list, Characters.Party party, Func<RecordId, int> level)
    {
        if (list.Count == 0 || party.Count == 0)
            return;
        var partyLevel = (byte)party.AverageLevel;
        var enemyLevel = list.Sum(level) / list.Count;
        if (enemyLevel == 0 || partyLevel >= enemyLevel)
            return;
        var share = LevelTrim[(int)(partyLevel * 3f / enemyLevel)];
        foreach (var kind in Enemies)
        {
            var remove = (int)(list.Count(e => e == kind.Entity) * share);
            for (var j = 0; j < list.Count && remove > 0; j++)
            {
                if (list[j] != kind.Entity)
                    continue;
                list.RemoveAt(j);
                remove--;
            }
        }
    }

    /// <summary>
    /// The arena by the terrain of the chunk the battle starts in (<c>BattleSetup.ChooseArena</c>, <c>0x80008698</c>), from
    /// the terrain's record (<see cref="TerrainRecord.Arenas"/>): a roll over its range, or with two ranges, a d2 first, 1
    /// for the first. Null where the terrain chooses none, which keeps the last battle's.
    /// </summary>
    public int? ChooseArena(PluginSet plugins, IRandom random)
    {
        if (Terrain is not { } terrain || TerrainRecord.Find(plugins, terrain)?.Arenas is not { Count: > 0 } arenas)
            return null;
        var range = arenas.Count > 1 && random.RollD(1, 2) != 1 ? arenas[1] : arenas[0];
        return random.Range(range.Min, range.Max);
    }
}
