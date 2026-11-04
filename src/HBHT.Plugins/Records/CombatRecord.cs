namespace HBHT.Plugins.Records;

/// <summary>
/// What combat keeps of content, there being one, <c>aidyn:combat/0</c>: the enemy a roll that comes to none fights, the arena a
/// script's battle takes by default, loot's gold, the game over, the menus' words and the Shadow's rules. What's about one entity,
/// item or battle is on it (<see cref="EntityCombat"/>, <see cref="WeaponStats"/>, <see cref="ItemRecord"/>, and a battle's own rules,
/// <see cref="BattleRules"/>, on the script, npc or encounter that starts it). docs/systems/combat.md says where each is in the original.
/// </summary>
/// <param name="Fallback">The enemy a party's roll fights when it comes to none (<c>BattleSetup.FillEnemies</c>): a Skeleton.</param>
/// <param name="DefaultArena">
/// The Battle arena a script's battle takes when it names none, and a battle whose terrain chooses none before any other battle has
/// chosen one: the original's arena 0 (<c>Zones.ArenaChunks</c>, <c>0x800DCA74</c>), Battlefields A01.
/// </param>
/// <param name="LootGoldMultiplier">
/// What the gold a loot table rolls is multiplied by (<see cref="LootRecord.GoldMin"/>): 2, as a container doubles its roll
/// (<c>get_chest_loot</c>, a shift left by one) and a battle the sum of its chests' (<c>Combat.AddChestLoot</c>, <c>0x8005E0F0</c>).
/// </param>
/// <param name="GameOver">
/// What a lost battle plays (<c>Combat.StartGameOverScript</c>, <c>0x8001F860</c>): a script object's script, from it. With none, a lost
/// battle ends the game at once.
/// </param>
/// <param name="Text">
/// The combat menus' and Sense Aura's words, which the code keeps rather than a string table.
/// </param>
/// <param name="Shadow">The Shadow's rules in any battle it's in (<c>gamecombat.cpp</c>'s Shadow functions). With none, no battle has them.</param>
public sealed record CombatRecord(
    RecordId Id,
    RecordId Fallback,
    RecordId DefaultArena,
    int LootGoldMultiplier,
    CombatText Text,
    GameOverScene? GameOver = null,
    ShadowRule? Shadow = null);

/// <summary>
/// The Shadow's rules, in any battle it's in (<c>Combat.ShadowSlot</c>, <c>0x800D94CC</c>, set as its combatant is made): ADR 0008's
/// layer 3, a feature on the entity with its item. docs/hardcoded-content.md's "The Shadow" has the original's.
/// </summary>
/// <param name="Entity">The Shadow.</param>
/// <param name="Item">What the party carries for it to merge rather than vanish: the Horn of Kynon.</param>
/// <param name="Merges">Whom it merges with: Alaron.</param>
/// <param name="Radius">
/// How far its aura reaches, and within which a combatant's field is held to 1 (<c>Combat.NearShadow</c>, <c>0x80023F34</c>) and the
/// AI's paths are cut (<c>EnemyAI.ShadowSmoothPath</c>, <c>0x8005A734</c>): 10.
/// </param>
/// <param name="VanishRounds">
/// Round ends its aura works without the item; at the next it vanishes (<c>Combat.ShadowAura</c>, <c>0x80023AD0</c>): 4.
/// </param>
/// <param name="MergeReach">What its body's and the merger's radii are lengthened by for them to touch, and merge.</param>
/// <param name="Dexterity">Its base Dexterity as it's made while the party carries the item, which gives it the turns to reach him.</param>
/// <param name="Cinematic">What a merge plays (<c>Combat.MergeShadow</c>, <c>0x800675C0</c>, then game mode <c>0xE</c>).</param>
/// <param name="Boost">What the merger gains.</param>
public sealed record ShadowRule(RecordId Entity, RecordId Item, RecordId Merges, float Radius, int VanishRounds, float MergeReach,
    int Dexterity, RecordId Cinematic, ShadowBoost Boost);

/// <summary>
/// What merging with the Shadow gives (<c>Combat.ShadowMergeBoost</c>): <paramref name="Stats"/> added to the merger's base,
/// <paramref name="Protection"/> to its protection; for each skill the Shadow has, the merger's raised by the difference, or by
/// <paramref name="Least"/> if that isn't positive; each of the Shadow's spells learned, or raised the same way to at most
/// <paramref name="SpellCap"/>; then <paramref name="Flag"/> set.
/// </summary>
public sealed record ShadowBoost(IReadOnlyList<StatGain> Stats, int Protection, int Least, int SpellCap, RecordId Flag);

/// <summary>A stat (<c>aidyn:stat/n</c>) raised by so much.</summary>
public sealed record StatGain(RecordId Stat, int Amount);

/// <summary>
/// The game over: the script of the script object <paramref name="From"/> (the Dreamscape's), started from it as a cut-scene. The
/// original names the script and starts it from the first script object in the Dreamscape's map that plays it, which the importer finds.
/// </summary>
public sealed record GameOverScene(RecordId From);

/// <summary>
/// A battle's own rules (ADR 0035), on what starts it: a script's <see cref="ScriptRecord.BattleRules"/>, an npc's or an encounter's
/// <see cref="PartyObject.BattleRules"/>. The original keys them by the battle's encounter flag (<c>Combat.ApplyFleeRuleByFlag</c>,
/// <c>0x800D26A0</c>; <c>Combat.NonLethal</c>, <c>0x800DA0DC</c>; <c>Combat.Exit</c>'s branch, <c>0x80023060</c>), which is the one
/// its source gives, so the importer puts them on each source of that flag: the Goblin Ambush's (978) on script <c>0x0365</c> and its
/// three npcs, Lugash's (5250) on script <c>0x04D1</c>, the Marquis's (3749) on his npc.
/// </summary>
/// <param name="Flee">
/// Whether the party can flee, with the last word over the bosses' (<c>Combat.ApplyFleeRuleByFlag</c>): the Goblin Ambush forbids it,
/// Lugash's and the Marquis's fights allow it. Null leaves it to the battle's source and its enemies.
/// </param>
/// <param name="NonLethal">
/// Losing isn't the game's end: the party's defeat line is <paramref name="Speaker"/>'s own (the combat strings' <c>0x74</c>),
/// then after 120 frames the screen fades, the encounter flag is set, the party fought is remembered, and the clock
/// goes on to 6:00 the next day (<c>Combat.StateDefeat</c>, <c>0x80022590</c>).
/// </param>
/// <param name="QuietVictory">A win shows no victory line (<c>TurnOrder.Victory</c>).</param>
/// <param name="Speaker">Whose portrait the non-lethal defeat line shows: Alaron.</param>
/// <param name="End">What follows the battle however it ends, won, lost or fled.</param>
/// <param name="HealsLeader">The party's first member is healed in full as the battle starts (<c>Combat.Init</c>, <c>0x80022D1C</c>).</param>
/// <param name="KeelOver">A member who keels over after so many hits rather than dying (<c>Combatant.ShadowFightTrick</c>, <c>0x80065DCC</c>).</param>
public sealed record BattleRules(bool? Flee = null, bool NonLethal = false, bool QuietVictory = false, RecordId? Speaker = null,
    EncounterEnd? End = null, bool HealsLeader = false, KeelOver? KeelOver = null);

/// <summary>
/// A member held up in a battle with its own rules (<c>Combatant.ShadowFightTrick</c>, <c>0x80065DCC</c>, the Goblin Ambush's hold on
/// Alaron): each blow that would hurt <paramref name="Member"/> takes 1 hit point instead, and the <paramref name="Hits"/>th takes its
/// Strength to 0, so it can't act.
/// </summary>
public sealed record KeelOver(RecordId Member, int Hits);

/// <summary>
/// What follows a battle with its own rules, however it ends (<c>Combat.Exit</c>): the party's leader's Strength back to its base,
/// <paramref name="Flag"/> set, the party taken to <paramref name="Place"/>, and <paramref name="Healed"/> healed in full.
/// </summary>
/// <param name="Place">The chunk trek starts again in.</param>
/// <param name="Position">Where in the chunk trek starts again.</param>
/// <param name="Heading">The way the player faces there, across the ground.</param>
public sealed record EncounterEnd(RecordId? Flag, RecordId Place, System.Numerics.Vector3 Position, System.Numerics.Vector2 Heading,
    RecordId? Healed, bool RestoresStrength);

/// <summary>
/// The combat menus' words and Sense Aura's, which 1.0 keeps as C strings in its code segment, not in a string table. A Sense Aura
/// line's <c>%d</c>, <c>%lu</c> and <c>%s</c> are printf's, as the ROM has them.
/// </summary>
/// <param name="LayOnHands">The skill list's row for laying on hands (<c>CombatMenu.PickLayOnHands</c>), <c>0x800D4880</c>: "Lay On Hands".</param>
/// <param name="HealWithHerbs">Its row for healing with herbs (<c>CombatMenu.PickHealWithHerbs</c>), <c>0x800D4890</c>: "Heal With Herbs".</param>
/// <param name="Perform">The Troubadour's row (<c>CombatMenu.PickTroubadour</c>), <c>0x800D48A0</c>: "Perform".</param>
/// <param name="NoSpells">What C-down's list says when it has no spells or skills (<c>CombatMenu.OpenSpells</c>), <c>0x800D4858</c>.</param>
/// <param name="NoItems">What C-up's list says when it has no potions or item spells (<c>CombatMenu.OpenItems</c>), <c>0x800D4834</c>.</param>
/// <param name="Aura">Sense Aura's popup.</param>
public sealed record CombatText(
    string LayOnHands,
    string HealWithHerbs,
    string Perform,
    string NoSpells,
    string NoItems,
    SenseAuraText Aura);

/// <summary>
/// The lines of Sense Aura's popup (<c>Entity.SenseAura</c>, <c>0x80070070</c>), a line more at each power over 0, 2, 4, 6 and 8
/// (<c>Entity.AppendResistanceLine</c>, <c>0x8006FFB0</c>, builds a resistance's).
/// </summary>
/// <param name="Follower">The line for a party member who isn't the leader, <c>0x800D3250</c>.</param>
/// <param name="Leader">The line for the party's leader, <c>0x800D325C</c>.</param>
/// <param name="Hitpoints">The total hitpoints, printf's <c>%d</c>, <c>0x800D3264</c>.</param>
/// <param name="Level">The level, printf's <c>%lu</c>, <c>0x800D3278</c>.</param>
/// <param name="Protection">The armor's total protection, printf's <c>%d</c>, <c>0x800D3284</c>.</param>
/// <param name="Immune">A resistance line, printf's <c>%s</c> the element, for a multiplier of 0 or less, <c>0x800D3214</c>.</param>
/// <param name="Resistant">A resistance line for a multiplier above 0 and below <paramref name="ResistantBelow"/>, <c>0x800D3224</c>.</param>
/// <param name="Vulnerable">A resistance line for a multiplier of <paramref name="ResistantBelow"/> or more, <c>0x800D3234</c>.</param>
/// <param name="ResistantBelow">
/// The float the resistance's multiplier (<c>Resistance.Damage</c>) is compared with (<c>0x800D3248</c>): 2.0. A line shows only for an
/// element the entity has a resistance entry for, by the element's name (<see cref="ElementRecord.Name"/>).
/// </param>
public sealed record SenseAuraText(
    string Follower,
    string Leader,
    string Hitpoints,
    string Level,
    string Protection,
    string Immune,
    string Resistant,
    string Vulnerable,
    float ResistantBelow);
