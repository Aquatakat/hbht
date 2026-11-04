using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;
using HBHT.Rom.Quests;
using HBHT.Rom.Text;

namespace HBHT.Rom.Import;

/// <summary>Combat's record (<c>aidyn:combat/0</c>): who fights, who doesn't, and the flee rules.</summary>
/// <summary>What combat's tables in code say of each item: which weapons fly and which strike in melee, and how far each flask spreads the AI.</summary>
internal sealed record ItemFacts(
    IReadOnlyDictionary<RecordId, Missile> Missiles, IReadOnlySet<RecordId> StrikesInMelee, IReadOnlyDictionary<RecordId, int> AiSpread);

/// <summary>What combat's tables in code say of each entity: its combat traits, and how its body holds weapons.</summary>
internal sealed record EntityFacts(
    IReadOnlyDictionary<RecordId, EntityCombat> Combat, IReadOnlySet<RecordId> WeaponsInModel, IReadOnlySet<RecordId> OtherHand);

internal static class CombatImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Combat");
        ctx.WriteRecords("combat.json", new[] { BuildCombat(ctx.Engine, ReadText(ctx.Rom)) });
    }

    /// <summary>
    /// The way Alaron faces in Oriana's hut after the Goblin Ambush, toward her: <c>Combat.Exit</c> (<c>0x80023060</c>) writes (1, 0)
    /// into trek's kept facing (<c>Zones.SavedPosition</c>'s at <c>0x800D9078</c>), which <c>InitGameTrek</c> copies into the body's
    /// own field (<c>+0x20</c>, <c>0x80020f40</c>), the original's reversed facing, so he faces (−1, 0).
    /// </summary>
    private static readonly System.Numerics.Vector2 OrianasHutHeading = new(-1, 0);

    /// <summary>
    /// The combat record: the Skeleton 0xBB (<c>BattleSetup.FillEnemies</c>), the encounters <c>0x800D26A0</c>'s three, the Goblin
    /// Ambush's rules (<c>Combat.Init</c>, <c>TurnOrder.Defeat</c>, <c>Combat.StateDefeat</c> and <c>Combat.Exit</c>), the game over's
    /// map, with its script read from the ROM's list (<see cref="EngineTables.GameOverScript"/>), and the Shadow's rules. What the code
    /// keeps about one entity or item goes on it (<see cref="Entities"/>, <see cref="Items"/>).
    /// </summary>
    internal static CombatRecord BuildCombat(EngineTables tables, CombatText text)
    {
        RecordId At(int place) => EntityAt(tables, place);
        return new CombatRecord(
            RecordId.Decimal(RecordTypes.Combat, 0),
            Fallback: At(0xBB),
            FleeByEncounter:
            [
                new(DbRecordIds.Flag(978), false),
                new(DbRecordIds.Flag(5250), true),
                new(DbRecordIds.Flag(3749), true),
            ],
            LootGoldMultiplier: 2,
            Encounters:
            [
                new(DbRecordIds.Flag(978), NonLethal: true, QuietVictory: true, Speaker: At(AlaronPlace),
                    End: new EncounterEnd(DbRecordIds.Flag(47), new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 6), 5, 3),
                        new System.Numerics.Vector3(5.9f, 0.5f, 3f), OrianasHutHeading, At(AlaronPlace), RestoresStrength: true),
                    HealsLeader: true, KeelOver: new KeelOver(At(AlaronPlace), KeelOverHits)),
            ],
            GameOver: new GameOverScene(RecordId.Hex(RecordTypes.Map, GameOverMap), RecordId.Hex(RecordTypes.Script, tables.GameOverScript)),
            Text: text,
            Shadow: Shadow(At));
    }

    private static RecordId EntityAt(EngineTables tables, int place) => DbRecordIds.Required(tables.EntityList[place], $"entity list place {place}");

    /// <summary>
    /// What the code keeps of each entity by its place in the entity list (<see cref="EngineTables.EntityList"/>), read from the ROM;
    /// the places are carried as facts, checked in 1.0's code: Sholeh is 0xA4 (<c>Combat.FindSholeh</c>); the Shadow 0xAC, with whom
    /// the party can flee (<c>Combat.ForbidFleeForBosses</c>) and whom no one can attack or cast at (<c>Combatant.CanAttack</c>,
    /// <c>Combatant.CanCast</c> through <c>Combat.ShadowSlot</c>); Pochanargat 0xAB, who never moves in battle
    /// (<c>Combatant.UpdateRadius</c>); the bystanders 0–0x56, 0x82–0x90 and 0x92–0x96 (<c>Combat.IsExcludedEnemy</c>); the bosses who
    /// forbid fleeing the twelve of <c>Combat.NoFleeEntities</c> (<c>0x800D2688</c>); the thirteen the AI gives the bosses' styles
    /// (<c>EnemyAI.HasBossStyle</c>, <c>0x8005BA40</c>, by its jump table at <c>0x800D24B0</c>); the boss flags (<see cref="BossFlags"/>);
    /// and the bodies that hold their weapons in their models and their bows the other way round.
    /// </summary>
    internal static EntityFacts Entities(EngineTables tables)
    {
        RecordId At(int place) => EntityAt(tables, place);
        static bool Bystander(int place) => place <= 0x56 || place is >= 0x82 and <= 0x90 or >= 0x92 and <= 0x96;
        var combat = new Dictionary<RecordId, EntityCombat>();
        void Set(int place, Func<EntityCombat, EntityCombat> change) =>
            combat[At(place)] = change(combat.GetValueOrDefault(At(place)) ?? new EntityCombat());

        Set(0xA4, c => c with { FightsForParty = true });
        foreach (var place in Enumerable.Range(0, tables.EntityList.Count).Where(Bystander))
            Set(place, c => c with { Bystander = true });
        foreach (var place in NoFleePlaces)
            Set(place, c => c with { Flee = FleeSay.Forbids });
        Set(ShadowPlace, c => c with { Flee = FleeSay.Allows, Untargetable = true });
        Set(0xAB, c => c with { Rooted = true });
        foreach (var (place, flag) in BossFlags)
            Set(place, c => c with { BossFlag = DbRecordIds.Flag(flag) });
        foreach (var place in BossStylePlaces)
            Set(place, c => c with { BossStyle = true });
        return new EntityFacts(combat, UndrawnWeaponPlaces.Select(At).ToHashSet(), OtherHandPlaces.Select(At).ToHashSet());
    }

    /// <summary><c>Combat.NoFleeEntities</c> (<c>0x800D2688</c>), in its order.</summary>
    internal static readonly IReadOnlyList<int> NoFleePlaces = [0xA8, 0xAA, 0xAB, 0xAD, 0xAE, 0xB4, 0xB3, 0xB2, 0xB1, 0xB0, 0xAF, 0xB5];

    /// <summary>The places <c>EnemyAI.HasBossStyle</c> (<c>0x8005BA40</c>) gives the bosses' styles.</summary>
    internal static readonly IReadOnlyList<int> BossStylePlaces = [0x57, 0x58, 0x59, 0x5E, 0x5F, 0x7A, 0xA7, 0xA8, 0xA9, 0xAB, 0xAD, 0xBA, 0xBC];

    /// <summary>The weapon classes whose weapons can drop as loot (<c>Combat.DropWeaponClasses</c>, <c>0x800D26C8</c>).</summary>
    internal static readonly IReadOnlyList<WeaponClass> DroppedClasses =
        [WeaponClass.Hafted, WeaponClass.Missile, WeaponClass.Pole, WeaponClass.Sword, WeaponClass.Thrown];

    internal static ItemFacts ItemFacts(ImportContext ctx) =>
        Items(ctx.Db, ctx.Items, ctx.Rom.Table(CodeTable.BowKindWeapons), ctx.Rom.Table(CodeTable.ThrownKindWeapons));

    /// <summary>
    /// What the code keeps of each item: the weapons that fly are the ROM's two lists of weapon DB positions
    /// (<c>Combat.BowKindWeapons</c> <c>0x800E0C08</c> and <c>Combat.ThrownKindWeapons</c> <c>0x800E0C14</c>, read per version), each with
    /// the numbers <see cref="Muzzles"/> carries; a listed weapon with no fact, a fact for none listed, or a name that differs is an
    /// error. Spikes, position 100, is the one ranged weapon allowed in melee. The flasks' radii are <see cref="FlaskDangers"/>.
    /// </summary>
    internal static ItemFacts Items(GameDb db, ItemTables items, ReadOnlySpan<byte> bowList, ReadOnlySpan<byte> thrownList) => new(
        Missiles(db, items, Positions(bowList), Positions(thrownList)).ToDictionary(m => m.Weapon, m => m.Missile),
        new HashSet<RecordId> { Weapon(db, SpikesPosition, "Spikes") },
        FlaskDangers.ToDictionary(f => RecordId.Hex(RecordTypes.Item, f.Item), f => f.Radius));

    /// <summary>
    /// The entities whose weapon isn't drawn in the hand, by their places in the entity list (<c>Combatant.HasNoWeaponAttachment</c>,
    /// <c>0x80066C5C</c>, the bytes at <c>0x800D2A0C</c> up to <c>0xFF</c>), carried as a fact: Shamsuk, the Gorgon, the Chaos Lieutenant,
    /// the Harpy, the Marquis, the Minotaur, the Minotaur Lord and Niesen.
    /// </summary>
    internal static readonly IReadOnlyList<int> UndrawnWeaponPlaces = [0xAD, 0x75, 0xA6, 0x76, 0xAA, 0x80, 0xA5, 0xA2];

    /// <summary>
    /// The entities who hold a bow at locator 0 and its arrow at 1 (<c>EntityBody.Hand</c>, <c>0x8006C8BC</c>, whose switch gives these
    /// places 0 and every other 1), carried as a fact: the bandit bosses and woodsmen, Becan and Keelin.
    /// </summary>
    internal static readonly IReadOnlyList<int> OtherHandPlaces = [0x57, 0x58, 0x59, 0x5C, 0x5D, 0x5E, 0x5F, 0x6C, 0x7A, 0x9C, 0xA1];

    /// <summary>
    /// The Shadow's rules (<c>gamecombat.cpp</c>'s Shadow functions, carried as facts read in 1.0's code; docs/hardcoded-content.md's
    /// "The Shadow"): entity list place 0xAC, the Horn of Kynon (<c>Combat.PartyHasHorn</c>, <c>0x80023AA4</c>), Alaron (place 0x99,
    /// <c>Combat.AlaronSlot</c>), the radius (the float at <c>0x800CE3CC</c>), the rounds without the Horn (<c>Combat.ShadowAura</c>,
    /// <c>0x80023AD0</c>), the merge's reach, the Dexterity it's made with when the party has the Horn, the Shadow's cinematic, and the
    /// merge's boost (<c>Combat.ShadowMergeBoost</c>).
    /// </summary>
    private static ShadowRule Shadow(Func<int, RecordId> at) => new(
        at(ShadowPlace), RecordId.Hex(RecordTypes.Item, HornOfKynon), at(AlaronPlace), Radius: 10, VanishRounds: 4, MergeReach: 0.5f,
        Dexterity: 30, Cinematic: RecordId.Decimal(RecordTypes.Cinematic, 1),
        new ShadowBoost(
            [Gain(4, 6), Gain(0, 3), Gain(1, 3), Gain(3, 3), Gain(5, 10)],
            Protection: 5, Least: 2, SpellCap: 10, Flag: DbRecordIds.Flag(566)));

    private static StatGain Gain(int stat, int amount) => new(RecordId.Decimal(RecordTypes.Stat, stat), amount);

    /// <summary>The Shadow's place in the entity list (its ID's low byte at <c>0x800E28EC</c>).</summary>
    private const int ShadowPlace = 0xAC;

    /// <summary>The Horn of Kynon, whose carrying makes the Shadow merge rather than vanish.</summary>
    private const int HornOfKynon = 0x0D03;

    /// <summary>
    /// <c>Combat.PartyPotionTier</c>'s tests (<c>0x8005C750</c>), in its order: the first flask the party holds gives the radius. The
    /// radii fall in that order, so the first held is the one with the greatest, which is how the port reads <see cref="ItemRecord.AiSpread"/>.
    /// </summary>
    internal static readonly IReadOnlyList<(int Item, int Radius)> FlaskDangers = [(0x1001, 7), (0x1000, 5), (0x1002, 4), (0x1003, 4)];

    /// <summary>
    /// The combat menus' and Sense Aura's words, C strings in the code segment (<see cref="CodeTable.CombatLayOnHands"/> on); the element
    /// names are the elements' own (<see cref="KindImporter"/>). Sense Aura's <c>Entity.AppendResistanceLine</c> picks the resistance
    /// line by the multiplier <c>m</c> (<c>0x8006FFB0</c>): "Immune" for <c>m &lt;= 0</c>, "Resistant" for <c>0 &lt; m &lt; 2.0</c>
    /// (the float at <c>0x800D3248</c>), "Vulnerable" for <c>m &gt;= 2.0</c>.
    /// </summary>
    internal static CombatText ReadText(RomImage rom)
    {
        string Line(CodeTable table) => MenuTables.Text(rom, table);
        var below = rom.Table(CodeTable.AuraResistantBelow);
        return new CombatText(Line(CodeTable.CombatLayOnHands), Line(CodeTable.CombatHealWithHerbs), Line(CodeTable.CombatPerform),
            Line(CodeTable.CombatNoSpells), Line(CodeTable.CombatNoItems),
            new SenseAuraText(Line(CodeTable.AuraFollower), Line(CodeTable.AuraLeader), Line(CodeTable.AuraHitpoints), Line(CodeTable.AuraLevel),
                Line(CodeTable.AuraProtection), Line(CodeTable.AuraImmune), Line(CodeTable.AuraResistant), Line(CodeTable.AuraVulnerable),
                Be.F32(below, 0)));
    }

    /// <summary>
    /// The Goblin Ambush's hits on Alaron before he keels over: <c>Combat.Init</c> sets <c>Combat.ShadowTurnsLeft</c> (<c>0x800D94CE</c>) to 2
    /// for encounter flag 978, and <c>Combatant.ShadowFightTrick</c> (<c>0x80065DCC</c>) counts it down.
    /// </summary>
    private const int KeelOverHits = 2;

    /// <summary>Alaron's place in the entity list (<c>0x99</c>), whom the Goblin Ambush's end heals and whose line its loss shows.</summary>
    private const int AlaronPlace = 153;

    /// <summary>
    /// The Dreamscape's map (Borg9 <c>0xD5F</c>), in which <c>Combat.StartGameOverScript</c> (<c>0x8001F860</c>) looks for the
    /// dialogue object that starts the game over script (<c>CodeTable.GameOverScenes</c>' first; its other two are never read).
    /// </summary>
    private const int GameOverMap = 0x0D5F;

    /// <summary>
    /// <c>Combat.SetKillFlags</c>' jump table (<c>0x800D2B60</c>, 19 cases from entity list place <c>0xA8</c>), read case by case in
    /// 1.0's code: the places that set a flag, and the flag. The rest (the Lizard Man Boss, the Marquis, the Shadow, Sheridan and
    /// four more) set none.
    /// </summary>
    internal static readonly IReadOnlyList<(int Place, int Flag)> BossFlags =
    [
        (0xA8, 154),  // Kitarak
        (0xAB, 3204), // Pochanargat
        (0xAD, 458),  // Shamsuk
        (0xAF, 3738), // Ksathra
        (0xB0, 3737), // Shatrevar
        (0xB1, 3736), // Mehrdad
        (0xB2, 3735), // Golnar
        (0xB3, 3734), // Nasim
        (0xB4, 3733), // Behrooz
        (0xB5, 3739), // Assim
        (0xBA, 533),  // Lugash
    ];

    /// <summary>The weapon DB position of Spikes, the one ranged weapon that may strike in melee too (<c>Weapon.AllowsMelee</c>, <c>0x8007B730</c>).</summary>
    private const int SpikesPosition = 100;

    /// <summary>What a bow flies as: Borg5 <c>0x348F</c>, the arrow (<c>Combatant.LoadWeaponScene</c>, <c>0x80066CF8</c>).</summary>
    private const int ArrowScene = 0x348F;

    /// <summary>The positions in a list that ends with <c>0xFF</c>.</summary>
    private static List<int> Positions(ReadOnlySpan<byte> list)
    {
        var positions = new List<int>();
        foreach (var position in list)
        {
            if (position == 0xFF)
                return positions;
            positions.Add(position);
        }
        throw new InvalidDataException("A weapon list doesn't end.");
    }

    private static RecordId Weapon(GameDb db, int position, string name) =>
        db.Weapons[position].Name == name
            ? DbRecordIds.Required(db.Weapons[position].Id)
            : throw new InvalidDataException($"Weapon position {position} is {db.Weapons[position].Name}, not {name}.");

    /// <summary>
    /// <c>Combatant.WeaponMuzzle</c> (<c>0x80066DD0</c>) is a switch on the weapon's position in the DB (jump table <c>0x800D2A18</c>,
    /// positions 53 to 102), which sets three numbers: the first (<see cref="Missile.Speed"/>) from the floats at <c>0x800D2AE0</c> to
    /// <c>0x800D2B44</c>, a second that the port doesn't use (20, 6, 12 or 8 for some, else 0), and the third (<see cref="Missile.Turn"/>),
    /// 90 for position 102 alone. A position it doesn't list gets 4.0 and 8. Read case by case from the disassembly.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, (string Name, float Speed, float Turn)> Muzzles = new Dictionary<int, (string, float, float)>
    {
        [53] = ("Bow of Accuracy", 9.9f, 0),
        [54] = ("Bow of Shielding", 8, 0),
        [55] = ("Bow of Thunder", 9, 0),
        [56] = ("Heartseeker Bow", 9, 0),
        [57] = ("Great Bow", 9, 0),
        [58] = ("Hunter's Bow", 8, 0),
        [59] = ("Long Bow", 8, 0),
        [60] = ("Short Bow", 5, 0),
        [94] = ("Dragon Fang", 8, 0),
        [95] = ("Throwing Iron", 8, 0),
        [96] = ("Throwing Knife", 5, 0),
        [97] = ("Cyclops Hurlstar", 5, 0),
        [98] = ("Dart of Distance", 8, 0),
        [99] = ("Poison Dart", 5, 0),
        [101] = ("Hatchet", 5, 0),
        [102] = ("Javelin", 5, 90),
    };

    /// <summary>The weapons that fly: the bow list's with the arrow, then the thrown list's with their own models (<c>LoadWeaponScene</c>).</summary>
    private static List<(RecordId Weapon, Missile Missile)> Missiles(GameDb db, ItemTables items, List<int> bows, List<int> thrown)
    {
        var listed = bows.Concat(thrown).ToList();
        if (listed.Count != Muzzles.Count || listed.Except(Muzzles.Keys).Any())
            throw new InvalidDataException($"The weapons that fly are {string.Join(", ", listed)}, but the facts are for {string.Join(", ", Muzzles.Keys)}.");
        return [.. bows.Select(p => Flying(p, true)).Concat(thrown.Select(p => Flying(p, false)))];

        (RecordId, Missile) Flying(int position, bool bow)
        {
            var (name, speed, turn) = Muzzles[position];
            // A weapon with no model loads no scene at all, so it couldn't fly.
            if (!items.WeaponModels.ContainsKey(position))
                throw new InvalidDataException($"{name} (position {position}) flies but has no model.");
            return (Weapon(db, position, name), new Missile(bow ? RecordId.Hex(RecordTypes.Scene, ArrowScene) : null, speed, turn));
        }
    }
}
