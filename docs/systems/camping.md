# Camping (roadmap 2.7)

How the party camps from the World tab's Camp ([ui](ui.md#the-world-tab)): the check, the ambush, the half heal, the camp scene and the eight hours. From 1.0's machine code (`campMenuController` at `0x80041020`, `Party::CampAmbushCheck` `0x80078d14`, `get_ranger_camping_bool` `0x800788cc`, `Party::CampHeal` `0x80078ba4`, `dialoug_ref_obj_func` `0x80004b0c`, `campfire_func` `0x80004cd4`, `World::Lapse8Hours` `0x8007c098`) and the base plugin's records.

## Where

- **The only check is the terrain** of the chunk the player is in: its camp factor must be above 0 (`camp_float_array`, `0x800e2f38`, 30 floats). Nothing else is checked: not towns, enemies, the map set or the time. Refused, the menu stays open and says "The party cannot camp here.", a literal in the code, in the Party tab's message box.
- **The factors:** 20 for terrains 0, 3, 4 and 27; 15 for 1 and 24; 10 for 2, 5, 7 and 17; 50 for 10; 30 for 20, 21 and 22; 0 for the rest. So 164 of the Overworld's 172 chunks allow camping (terrains 19 and 23 don't), and no other map set does, but EhudUnder's C09 (terrain 4) and MiscInteriors' I05 (terrain 2).
- **Combat's pause menu** has no World tab, so there's no camping in combat.

## What happens

`Party::CampAmbushCheck`, as the menu closes:

1. **The ambush roll**, once per camp. Only while derived flag 5004 holds (two of Abrecan, Godric and Rheda have joined; flags 89–91). The member best at Ranger (`GetMostSkilledMember`: the four slots in order, dead or not, the later on a tie) gives r, their modified Ranger, 0 if it's 0 or less and at most 10. The party is ambushed if d100 ≤ trunc((15 − r) × 1/15 × the factor), in single precision: the factor in percent with no Ranger, a third of it at 10. The 1/15 is 0.0666667 (`0x800d3814`). Ambushed, nothing else happens: no heal and no time.
2. **Otherwise the half heal** (`CampHeal` with half set, the table `{1.0, 0.5}` at `0x800d386c`): each living member regains half their maximum hit points, truncated, up to what's missing, and 24,000 ticks of play pass for their effects (8 hours at the clock's 72: `Entity::IncEffects`), so a potion with 8 hours or less left runs out. Then every battery, worn or in the party's inventory, refills by half its capacity (`multi_equip_StamMod`). The loop covers every stack. Nothing is eaten, and no skill or attribute changes the amount. The camping scripts have no `CampHeal` op, so camping heals half, not fully; only nine conversations heal fully.
3. **The camp scene**: flag 4240 is set while the game looks through the map objects of the chunk the player is in (`gameVars.borg9DatPointer`), and the first dialogue object whose flag A is one of eight (`Camp_CheckDialougFlags`, the list at `0x800d79f0`) and holds starts its script, as using it would. Distance doesn't count: `dialouge_trigger_check` is called without it (`0x800113cc`). With none, the Campsite's map (`aidyn:map/0x185C`, MiscInteriors C05) gives its first dialogue object's script, `aidyn:dialogue/0x185B`: Alaron alone at the fire, saying "Ahhhhhh. Pass the marshmallows.". 4240 is cleared again.
4. **Eight hours pass when the scene ends**, as after any script marked `rest` ([scripts](scripts.md)): the clock moves on and the calendar's flags are written (`Lapse8Hours`). Time played doesn't.

The eight camp scenes, each derived from 4240 and its own conditions, and each once, as its done flag clears it:

| Flag | Script | Chunks | When |
| --- | --- | --- | --- |
| 5056 | `0x03CC` | B07 | Told to fight the goblins (145), Kitarak alive (154) |
| 5011 | `0x03D2` | B06 | 156 set, 158 clear |
| 5150 | `0x049C` | N21, Q21 | After Shamsuk (458), Godric and Becan (90, 375) |
| 5151 | `0x049D` | M22, P21 | After Shamsuk, Abrecan in the party (89) |
| 5154 | `0x04A0` | N23, P22 | After Shamsuk, Arturo in the party (247) |
| 5155 | `0x04A1` | O23, P23 | After Shamsuk, Baird in the party (380) |
| 5163 | `0x04AA` | R26 | Baird in the party |
| 5191 | `0x04EB` | I15, J15, J16 | Dougal in the party (574) |

## The ambush's enemies

From the camp encounters table ([db](../formats/db.md), ROM `0x1FEDE50`, 80 bytes a terrain: `CampEncounters.Load`, `0x800096DC`): the terrain's row, at the tier of the party's average **modified** Level, dead members counted (`Party.AverageLevel`, `0x80075134`: 5 or less, 6–10, 11–15, 16–20, over 20), two groups of an enemy with a minimum and maximum; the five slots after them are never read. They're made into a monster party's with its least the two minimums summed and its most the two maximums, and rolled as one is (`BattleSetup.FillEnemies`, with the level trim and the Skeleton for none). The arena is the terrain's; the collision byte 2 (an ambush), the animation byte 0, no fleeing, no bonus loot and no encounter flag, so a win sets nothing and remembers nothing. Terrains with no camping have rows of Chaos Spellweavers alone.

**Terrain 27** (six Overworld chunks, N25, O24–O26, P24 and P25, camp factor 20) allows camping but has no row: the original reads the String/Cheats table's ciphertext after it (`0x1FEE6C0`), in which none of the ten IDs is an entity, so `Combat.IsExcludedEnemy` drops them all and the battle has no enemy. What that does in play (a win at once, or a crash) isn't settled ([user checks](../user-checks.md#combat)). Until it is, the port says "The party is ambushed, but no one comes." and goes back to trek, with no battle, no heal and no time (`GameSession.BeginBattle`).

## In the port

- **The records:** each terrain's factor is its `camp` (`aidyn:terrain/n`), and the rest is the camping record (`aidyn:camping/0`): the refusal's text, the ambush's flag and numbers, the heal's share and effect time, the check flag, the eight scenes' flags and the Campsite's map. The importer reads the factors and the scenes' flags from the ROM and carries the rest as facts ([hardcoded content](../hardcoded-content.md)).
- **The rules:** the World tab checks the terrain (`WorldMenu.Accept`), and `TrekLoop.Camp` rolls the ambush (`Camping`, on the world's random stream), heals (`Party.CampHeal`, which a script's camp heal also uses, fully) and cues the scene as a dialogue object's cut-scene. The hours pass as the scene ends (`GameState.Lapse`), for any script that ends with rest hours: a camp scene, or a conversation's camp heal.
- **The ambush** starts a battle (`BattleSetup.CampAmbush`: where the party camped, its terrain's ambush at the party's tier, no fleeing), which trek fades out into: no heal and no time, as in the original. The camp encounters are each terrain's `Ambushes`, five tiers of the two kinds, and the tiers' levels are the camping record's `AmbushLevels`; terrain 27 has none ([above](#the-ambushs-enemies)).
- **The way in** is the pause menu's exit (`PauseExit.Camp`), which `GameSession` carries out once the menu has rolled up, as the original's trek state 0x11 runs its camp (`TrekLoop.CampStart`, `0x80004ac0`).
