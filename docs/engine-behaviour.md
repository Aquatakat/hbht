# Aidyn Chronicles: Engine Behaviour Reference

Sep 25, 2026 · @Ryan

## Purpose and conventions

This document describes how the Aidyn Chronicles: The First Mage engine behaves, top to bottom, as observed from play. It is the foundation for an eventual OpenMW-style reimplementation (likely in Godot) that runs on content from a user-supplied ROM, possibly with a plugin system in the spirit of Bethesda's ESM/ESP files.

It describes systems and rules, not specific content. Spell lists, item tables, and per-area data belong in later appendices.

Treat this document as generally correct as far as being a broad overview, but it's written from memory, so some specific details may be incorrect.

Details marked **(decomp)** were checked against the [blackgamma7/Aidyn](https://github.com/blackgamma7/Aidyn) decompilation (Sep 26, 2026) rather than observed in play. They reflect what the pseudocode says, which is reliable for logic and constants, but they should still be spot-checked in an emulator before the reimplementation depends on them.

Terminology follows the game where it has its own words: **trek mode** for free exploration, **battle mode** for combat, and **Alaron** for the player character.

## Game modes

The game runs in two primary modes: trek mode and battle mode. Menus, dialogue, and cut-scenes layer on top of these rather than being separate modes of their own.

**Trek mode** is free exploration of the overworld. The player can go anywhere, with progress gated at different stages of the game by things like locked doors. Only Alaron is shown on screen; the rest of the party is present but invisible.

**Battle mode** is turn-based combat in a separate arena themed to the area where the battle began. Battles can start in five ways:

- Through dialogue.
- Through an area trigger.
- A hostile creature noticing the player in trek mode.
- The player sneaking up on a creature and activating it.
- Ambushes while camping.

Movement controls are identical between the two modes, as is the camera.

## World

The overworld is one seamless worldspace with loading hidden from the player. Interiors mostly live inside it, and there are no visible load transitions anywhere.

### Loading

Loading is invisible. **(decomp)** It's chunk streaming, not chokepoints. Each map is a grid of chunks roughly 100 units square, and the engine keeps a 3×3 block of chunks loaded around the player, swapping rows and columns in as the player crosses chunk boundaries. Each chunk has two scene models and one collision-and-objects file.

**(decomp)** Some world objects exist only when an Expansion Pak is present, and others only when it isn't. A reimplementation should use the Expansion Pak set.

### Terrain

Terrain is made of interlocking meshes of varying shapes, including donut shapes that form bridges and caves. Collision typically matches the visible geometry, but is sometimes adjusted to add invisible walls. Terrain has an unusually high draw distance for the platform.

### Interiors

Doors transport the player into interiors instantly, with a very short fade to black.

**(decomp)** Not all interiors are in the overworld. The game is split into 17 map sets:

| Kind | Map sets |
| --- | --- |
| Overworld | Overworld |
| Towns and dungeons | Goblin Camp, Port Saiid, Lighthouse, Gwernia Castle, Erromon, Terminor, Barrows, Roog's Cave, Ehud's tower (underground), Ugarit, Ugarit 2, Chaos Isle |
| Shared interior sets | Gwernia interiors, miscellaneous interiors, Jundar interiors |
| Battle | Battle arenas |

Doors are teleporter objects that can move the player to any chunk in any map set, which is why the transitions look identical. It's still unconfirmed whether town exteriors also exist in the overworld set, or only in their own sets.

**(decomp)** Camping isn't blocked by geofenced town regions. No-camp zones are dialogue trigger volumes placed in the map and gated by a small set of camp-specific flags. When the player camps inside one, its dialogue plays instead of the normal campfire. The same mechanism provides the special story campfire scenes.

### Draw distance tiers

There are three observable draw distance tiers:

| Tier | What it covers | Distance |
| --- | --- | --- |
| Terrain | Landscape meshes | Very long |
| Objects | Trees, some buildings | Medium |
| Actors and containers | NPCs, creatures, chests, herb piles, etc. | Short, sometimes quite short |

**(decomp)** Each placed scene object stores its own draw distance, so the object tier is a content convention rather than a fixed engine setting. Distances are from the player, not the camera, and each model fades over its last unit rather than popping. Chests are drawn to 10, most trees to 40–70. See [rendering](systems/rendering.md#draw-distance).

## Time, weather, and aspect

The game runs a continuous day/night cycle with a calendar, and time of day feeds into combat through the solar/lunar aspect system.

### Day/night and calendar

Time advances continuously in trek mode. The calendar is shown in the pause menu. Lighting shifts to a bluer moonlight at night, and fog amounts vary.

Camping advances time by eight hours.

### Weather

**(decomp)** The calendar does feed the weather, through the season. Each weather roll works like this:

1. 25% of the time, the weather is clear.
2. Otherwise, precipitation happens if a d100 roll is at or under the terrain type's chance plus the season's chance.
3. Precipitation is rain with storm winds 70% of the time (snow on snowy terrain) and fog the other 30%.

**(decomp)** Time of day, calendar, moon phase, weather, and terrain are all mirrored into event flags, so dialogue and map objects can react to them.

**(decomp)** Weather affects combat: throwing potions takes accuracy penalties at night, in fog, and in storms, with an extra penalty per unit of distance beyond 5 in bad weather.

### Time-dependent content

Shops are always open. However, some later-game shopkeepers aren't in their shops at night, which looks like an incomplete implementation of shop closing hours.

Some encounters appear only at night, and some only during the day. This is rare, similar to the absent shopkeepers.

### Aspect

Aspect is a cross-cutting property: each character, some weapons, and each spell is either **solar** or **lunar**. Solar things perform better during the day; lunar things perform better at night. A character's aspect is shown in the pause menu under their portrait.

**(decomp)** For attack accuracy, a character whose aspect matches the time of day gets ×1.25, and one whose aspect is opposed gets ×0.75. Only night versus not-night matters. Enemies with an opposed aspect are also slightly more likely to flee.

## Actors, stealth, and containers

Actors are humanoids and monsters with per-mesh animation sets and simple trek-mode behaviour. Containers are interactive world objects with three states.

### Actors

Each actor mesh tends to carry its own unique animations: attacking, defending, walking, running, and speaking. There is no turning animation; actors rotate in place.

### Trek-mode behaviour

In trek mode, actors wait, wander, or chase. Almost all actors stop and face the player when nearby.

Hostile creatures are unsophisticated, similar to Morrowind. They follow what is presumably a path grid until they need to leave it to chase the player, then pursue in a straight line. This can leave them stuck on scenery.

### Stealth

**(decomp)** Holding Z while moving sneaks; it isn't a toggle. Without Z, pushing the stick less than 80% of the way walks and pushing it further runs.

A sneaking player who goes unnoticed gets no feedback except that the creature's behaviour doesn't switch to chasing. There are no audio cues or visible indicators. The Stealth skill improves the odds of going unnoticed.

Sneaking up on a creature and activating it starts a battle with alternative spawn positions meant to favour the player (see Battle mode).

### Containers

Containers include chests, barrels, sacks, piles of gold, herbs, gemstones, and spice. Each has three states: **closed**, **open**, and **destroyed**.

Containers can be locked and trapped:

- **Locks** are picked with the Thief skill. Locked doors use exactly the same system as locked containers.
- **Traps** are disarmed with the Mechanic skill. A failed disarm triggers the container's destroyed state.

The Ranger skill increases the number of consumables found in natural-looking containers such as herb and spice piles.

## Party

The party holds up to four characters, including Alaron. Membership is gated by dialogue and, for some characters, by quest state.

### Visibility

Only Alaron appears in trek mode. The full party appears in battles and cut-scenes.

### Joining and leaving

Potential party members are usually found in towns, sometimes with quests attached, sometimes just standing around. Finding the right dialogue adds them immediately if a slot is free.

If the party is full, the game prompts the player to speak to each current member, who offers a leave/stay dialogue choice. Some members refuse to leave under conditions:

- Brenna never leaves.
- Rheda only leaves after her quest to cure Niesen has concluded.

### Party-aware cut-scenes

Main story cut-scenes generally have a slot for every possible party member. If a character isn't in the party, their lines are skipped. Some character-specific cut-scenes only trigger with a particular combination of party members.

### Death

Party member deaths in battle are permanent. If Alaron's HP reaches zero, the battle is lost.

## Characters

Characters have six attributes, twelve general skills, and six weapon skills, all raised by spending XP. There are no formal classes, but each character has a fixed set of skills they can ever learn, which soft-classes them.

### Level and hit points

Level rises at increasing XP thresholds, in the usual JRPG pattern.

Maximum HP is the sum of level, Endurance, and Stamina.

### Attributes

Attributes are raised by spending XP after a battle.

| Attribute | Effect |
| --- | --- |
| Intelligence | Ranged weapons and magic |
| Dexterity | Turn frequency and movement radius in battle |
| Willpower | Magic damage resistance |
| Strength | Melee damage |
| Endurance | Hit points (believed to be its only use) |
| Stamina | Hit points, and the depletable resource for skill and spell use |

Because Stamina is both part of max HP and the resource spent on skills, using most skills and spells effectively costs hit points.

### General skills

| Skill | Effect |
| --- | --- |
| Alchemist | Brewing potions; throwing potions in battle |
| Diplomat | Makes NPCs friendlier (no practical effect) |
| Healer | Healing effectiveness in and out of battle |
| Loremaster | Length and number of books readable in libraries |
| Mechanic | Disarming container traps; crafting armour |
| Merchant | Better buy and sell prices |
| Ranger | Lower ambush chance when camping; more consumables from natural containers |
| Stealth | Sneaking past enemies |
| Thief | Picking locks; backstabs in battle |
| Troubadour | Enables the troubadour performance in battle |
| Warrior | More critical hits, higher physical damage, better hit chance |
| Wizard | As Warrior, for magic |

### Weapon skills

Weapon skills primarily affect hit chance. A character with zero in a weapon skill can't equip that weapon type.

| Skill | Covers |
| --- | --- |
| Hafted | Maces, hammers, blunt weapons |
| Missile | Bows and crossbows |
| Pole | Polearms, spears |
| Sword | Swords |
| Thrown | Throwing knives, poison darts |
| Shield | Shield effectiveness (zero means no shield) |

### Raising skills

Skills and weapon skills can be raised two ways: by spending XP after a battle, or at a trainer for gold plus a smaller XP cost. A skill at level zero must be taught to at least level 1 by a trainer; it can't be started from nothing after a battle.

### Skill restrictions

Some characters can never learn some skills. For example, most magic users can't use shields, and warrior characters can't learn Wizard.

## Magic

Spells belong to four schools plus a set of unschooled spells. Each cast costs one reagent plus stamina (or spell battery), and targeting follows one of three patterns.

### Schools

There is a lot of functional overlap between schools.

| School | General shape |
| --- | --- |
| Naming | Stat buffs and debuffs not found in other schools; Teleportation |
| Elemental | Mostly elemental attack spells |
| Necromancy | Death-themed attacks; utility such as Cheat Death; one immobilizing spell |
| Star | Grab bag of utility spells; one immobilizing spell |
| Unschooled | Warding and dispelling specific schools (e.g. vs. Necromancy, Dispel Elemental); Mirror, a chance to reflect any spell |

Alaron can cast from every school, presumably for plot reasons. Other characters either can't cast at all (warriors and similar) or are limited to one school plus the unschooled spells.

Spells can only be cast in battle. The pause menu's spell list is informational.

**(decomp)** Photosynthesis can't be cast at night.

### Learning and levelling

Spells are learned from scrolls found in the world or from trainers. Once learned, they're levelled like skills: at trainers or with XP after battles.

### Casting cost

Each cast consumes:

- **One reagent**: an herb, spice, or gemstone.
- **Stamina**, unless the caster has items with **spell battery**, which is drawn from first.

Spell battery regenerates slowly over time outside of battle, similar to enchanted items in Morrowind.

### Targeting

All spell targeting uses a radius centred on or controlled by the caster.

**(decomp)** The radius is the spell's **effective level** multiplied by the spell's base range value. Intelligence plays no part. The effective level is the lower of the caster's Wizard skill and the spell's level; for item spells, it's the item's fixed level.

**(decomp)** Spells need line of sight. Arena geometry blocks them, and so do other combatants' bodies (see Battle mode).

**Single target.** A radius appears around the caster; the player selects one ally, enemy, or themselves within it. Self-cast and enemy-cast use identical targeting. Wind is unusual in being castable on either self or an enemy.

**(decomp)** Cast on an enemy, Wind does damage. Cast on an ally or self, it applies a timed Wind enchantment instead. When that enchantment ends, the weather is set to fog, which suggests Wind clears fog and storms (and their penalties) while it lasts. That part is inferred and unconfirmed.

**Caster-centred area.** The game picks the targets within the radius; the player can't choose. The targeting is smart: it won't put damage spells on allies, and recasting will cover the members missed the first time.

**(decomp)** Most area spells affect half the effective spell level in targets, rounded up (level 1–2: one target, 3–4: two, and so on). A few spells are flagged to hit every valid target instead.

**Player-placed area.** The player moves the radius to a spot within a reach set by the same stat soup. This is indiscriminate: everyone inside is affected, allies included, and nobody is affected if it's empty. Probably only Fireball and Teleportation use it; Teleportation moves the caster to the chosen spot.

### Durations

Buffs and debuffs have durations, but they're long enough that they don't usually expire within a typical fight.

### Item spells

Some items carry spells: wands, some swords, and other equipment. They're cast the same way as learned spells, with two differences:

- The effective spell level is fixed by the item and not shown to the player.
- Each spell on each item has its own hidden spell battery.

In practice, an item spell can be cast once or twice before it needs time to recharge.

## Items and equipment

Inventory is unlimited, gold is unlimited, and crafting happens by using skills from the pause menu rather than at workstations.

### Item categories

| Category | Behaviour |
| --- | --- |
| Equipment | Weapons, armour, accessories; equipped to slots |
| Potions | Usable at any time; some can be thrown in battle |
| Skill consumables | Consumed by skills: herbs, spices, gemstones (magic reagents, Healer), beast hides (Mechanic), etc. |
| Spell scrolls | Teach a spell when used |
| Keys | Open specific locks |
| Quest items | Must be carried; can't be used or interacted with |
| Sapphire Gems | Do nothing |

### Equipment slots

Each character has one weapon slot, which holds a melee weapon, thrown weapon, missile weapon (bow or crossbow), or a thrown potion.

**(decomp)** The full slot list is weapon, head, cloak, amulet, body armour, gloves, belt, boots, shield, wand, and two rings. Wands have their own slot, which rope and banner items also use. There's no legwear slot and no separate bracers slot; gauntlets are glove-slot items.

**(decomp)** The equipment code also has three scroll slots, and scrolls carry charges the same way wands do. That suggests scrolls can be equipped and cast from, not only used to learn a spell. This hasn't been checked in play.

Equipping a weapon or shield requires at least level 1 in the matching weapon skill.

### Crafting through skills

Selecting a usable skill in the pause menu opens a popover listing the required items; confirming consumes them and produces the result.

- **Alchemist** brews potions from ingredients.
- **Healer** consumes an herb for a stronger heal.
- **Mechanic** turns a beast hide into a piece of armour.

All other skills apply automatically in the world or in battle.

### Shops

Shops use a standard buy/sell panel built on the pause menu layout (see UI). Shopkeepers have infinite gold.

Some shopkeepers won't buy certain item categories. Those items are hidden from the sell list entirely rather than greyed out. The Merchant skill improves prices in both directions.

## Dialogue and cut-scenes

Dialogue is a text box with an optional portrait and branching choices. Cut-scenes come in two kinds: dialogue-driven scenes with scripted cues, and four fully keyframed animated scenes.

### Dialogue

Activating most non-hostile actors begins dialogue. The box typically shows up to three lines at a time alongside a portrait. Portraits are almost universal but not required. There's no apparent limit on dialogue length; the box grows to fit.

Dialogue can:

- Add items.
- Award experience points.
- Open a shop menu.
- Open training (probably; trainers are NPCs).
- Start a battle.
- Add a party member, or offer a party member the leave/stay choice.

Dialogue often presents choices, two or more with no known upper limit. Choices lead into branches that frequently converge before the conversation ends, which suggests each choice simply points at a specific dialogue line rather than a strict tree.

The same dialogue box stays on screen during battles as a running log (see Battle mode).

### Scripted cut-scenes

Scripted cut-scenes start from area triggers in trek mode. On top of regular dialogue, they add cued camera movements and actor cues (walk to a point, play an animation, and so on) that fire as the player advances through the dialogue.

### Full animation cut-scenes

Four fully animated cut-scenes exist. The player has no control. Animation and dialogue play as keyframed, and dialogue appears without portraits. The range of animation is unusual for the N64, close to what Perfect Dark does. The pause menu has an unlocked cut-scenes option, probably for replaying these.

## Battle mode

Battle is turn-based on a small, roughly circular arena. The player controls one party member at a time with trek-mode movement, and each turn ends with a single action.

### Arenas

Arenas are condensed approximations of the overworld area where the battle started. They're all roughly the same size and roughly circular; a starting character needs about 5–6 turns to walk end to end. Most are fairly flat. Some have a small hill or obstacles such as rocks, walls, or scenery pieces.

Each arena has fixed spawn points:

- A standard player spawn and a standard enemy spawn.
- Alternative spawns used when the player sneaks up on a creature. These are meant to be advantageous, but in practice make little difference.
- Reserve spawn points for enemies 5–12 (see below).

### Party sizes

The player party has 1–4 characters. An enemy party has 1–12, but only four can be on the battlefield at once. When an enemy is defeated or flees, the next reserve enemy spawns at whichever fixed spawn point is farthest from the player party. That includes the original enemy spawn, if the party has moved away from it.

### Turn order

One combatant acts at a time, from either side. Nothing prevents the same character from taking consecutive turns.

**(decomp)** Turn frequency comes from **base** Dexterity alone, so buffs like Haste don't add turns. Each round, every combatant gets a number of turns from their Dexterity band, and the order within the round is shuffled randomly. Some bands alternate between rounds:

| Base Dexterity | Turns per round |
| --- | --- |
| 1–18 | 1 |
| 19–24 | Alternates 1 and 2 |
| 25–30 | 2 |
| 31–34 | Alternates 2 and 3 |
| 35+ | 3 |

### A turn

At the start of a turn, a circular **field of influence** appears around the active character. The character can move freely within it.

**(decomp)** The field's radius comes only from **modified** Dexterity, via a lookup table:

| Dexterity | 1–3 | 4–7 | 8–12 | 13–16 | 17–21 | 22–26 | 27–28 | 29–30 | 31–33 | 34–36 | 37+ |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Radius | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 |

After that:

- Haste adds its spell level to the radius, and Stellar Gravity subtracts its level.
- The result is clamped between 1 and 13.
- Starting a turn next to an enemy ends a troubadour performance.
- Starting a turn near the Shadow limits the radius to 1.
- Pochanargat can't move at all.

Choosing any of these actions ends the turn:

- Skip turn.
- Melee attack an enemy within the field.
- Ranged attack an enemy within reach, based on weapon skill.
- Equip a thrown potion.
- Throw the equipped potion.
- Drink a potion.
- Equip a different weapon.
- Begin a troubadour performance.
- Continue a troubadour performance.
- Cast a spell (single target, caster-centred area, or player-placed area; see Magic).
- Heal an ally with the Healer skill.

**(decomp)** The battle controls and menus are as follows:

- **C-up** opens potions and item spells. The potions are the drinkable ones, meaning healing and later potion types except Stealth. The item spells can come from the weapon, armour, shield, or any worn item that has charges left.
- **C-down** opens spells and skills: learned spells (only those whose reagent the party has), Troubadour, Healer by hand, and healing with herbs.
- Both menus stay open only while their button is held.
- Thrown flasks (Fire, Inferno, Sleep Gas, Acid) aren't in C-up. They're equipped to the weapon slot and used as an attack.
- **Z** or **L** during movement skips the turn.
- **B**, **Z**, or **L** cancels an action that's been selected but not confirmed.
- **Start** opens the pause menu. In battle it only offers the System and Party tabs.

**(decomp)** Ranged attacks, thrown potions, and spells all need line of sight. The check fails if arena geometry or another living combatant's body is in the way.

### Troubadour

Starting a performance locks the character into performing until it wears off. It gives a small boost to everything, comparable to Luck in Morrowind.

### Enemy AI

Enemies have a small pool of about four special attacks, plus a melee or ranged attack to fall back on. They appear to pick from this pool at random. They may focus on one target and stick with it.

Enemies decide to flee based on their own health and whether the enemy party's **leader** has died.

**(decomp)** The leader is whichever enemy on the initial battlefield has the highest base Intelligence; ties go to the later one. It isn't a hand-set property, which explains why it's usually intuitive but not always.

**(decomp)** Fleeing is a morale check. It only happens if all of these are true:

- The arena has flee points, which are reference points named "Flee N".
- The enemy is out of stamina, or at most half of the enemy party is left.
- The enemy's morale is below 12. Enemies with a morale stat of 12 or more never flee.

When those hold, the enemy flees if its modified morale is lower than a 2d6 roll. Morale starts from the entity's morale stat, and these modifiers apply:

| Condition | Modifier |
| --- | --- |
| Opposed aspect | −1 |
| Recently hit | −2 |
| Out of stamina | −1 |
| Fewer than half of its allies left | −1 |
| Under any spell effect | −1 |
| Leader dead | −4 |
| Fewer than half of the player party left | +2 |
| Its target is out of stamina | +3 |

The leader's own morale also shifts the result.

Reserve enemies can flee the moment they arrive, which the combat log announces.

### Win and loss

The player wins when every enemy is gone by any combination of:

- Fleeing.
- HP reduced to zero.
- Immobilized by spell effects, or by having stats reduced below zero.

The player loses when every party member is immobilized, or when Alaron's HP reaches zero. Other party members who die stay dead permanently.

### Fleeing

The player can flee from the pause menu. It always succeeds with no penalty, but places the party back at what appears to be a checkpoint in trek mode rather than where the battle started.

**(decomp)** Fleeing isn't available in boss battles: Kitarak, Marquis, Pochanargat, Ehud, Sheridan, Behrooz, Nasim, Golnar, Mehrdad, Shatrevar, Ksathra, and Assim. It is available against the Shadow.

**(decomp)** The checkpoints are breadcrumbs. While the player walks in trek mode, the game records their position at a regular interval, skipping any spot within 10 units of an existing breadcrumb, and keeps the last 15. On fleeing, the party is placed at the breadcrumb in the same map set that's **farthest** from where the battle happened. Breadcrumbs aren't related to saves.

### Rewards

Winning opens a modified pause menu across two screens:

1. **Experience.** XP and items are awarded for every downed enemy, including ones that fled. The full XP amount goes to each party member present; it isn't split. Level-ups are shown here, and XP can be spent on attributes, skills, and spells.
2. **Loot.** Dropped items appear in a popover. A takes an item; B abandons it permanently.

### Battle feedback

The dialogue box stays on screen and narrates the battle: "Alaron begins action", "Alaron attacks Cave Bear", "Cave Bear takes 23 damage".

Damage and healing numbers pop up briefly above the affected character. Status effects also display above heads, cycling through each active effect. With only one or two effects, an icon shows briefly followed by a long blank gap, possibly unintentionally. One guess is that the cycle has fixed slots regardless of how many effects are active.

### Camera

The battle camera is the same floating third-person camera as trek mode (see UI).

## UI, controls, and saving

The pause menu is a dense, power-user hub with three top-level tabs, and its centre tab is reused for shops, training, and post-battle screens. The trek-mode HUD is minimal.

### Pause menu: top level

**(decomp)** Z (or L) moves to the previous tab and R to the next. There are three tabs, left to right:

1. **System.** Save, load, options, credits, unlocked cut-scenes, help, quit.
2. **Party.** The combined character, inventory, and skills screen (below).
3. **World.** World map, calendar, camp, and journal.

### Party tab layout

The top left shows the selected party member's portrait, with aspect and spell school (if any) beneath it. C-left and C-right cycle party members. To the right: an HP bar, current and maximum HP, level, and party gold. **(decomp)** XP isn't shown here.

Below that, the left side shows a generic humanoid silhouette with icon squares for each equipped item in its body position. This area is covered whenever a popover opens.

The right side is a scrolling panel with four tabs, switched with the analogue stick left and right:

| Tab | Purpose |
| --- | --- |
| Inventory | Examine, equip, or use items. Sorted by who has it equipped, then category, then alphabetically. |
| Stats | Informational. XP to next level sits at the bottom. |
| Skills | Informational, plus use for Healer, Alchemist, and Mechanic (opens a popover). |
| Spells | Informational only; spells can't be cast outside battle. |

C-down on any entry opens a popover with a short description and any relevant stats.

### Reuses of the party tab

**Barter.** The trader's items appear in the popover over the equipment silhouette. The analogue stick switches between buying their items and selling yours.

**Training.** The panel shows trainable skills and spells. Left and right switch between spells and skills, and both panels move together so current levels are visible alongside what's for sale.

**Post-battle loot.** The popover lists dropped items. A takes, B abandons.

### World tab

The world map shows the whole worldspace at a very high level, with fog of war. It's revealed through story progress and by finding or buying maps in the world. The calendar, camp button, and journal also live here.

### Journal

The journal is a list of entries, like the original Morrowind journal before quest grouping. Entries appear in a fixed order rather than chronologically. The most recently unlocked entry is highlighted in navy text.

### Trek-mode HUD

All trek-mode UI sits in feathered, semi-transparent black boxes. The pre-release HUD was more elaborate; the final one is pared down.

- **Compass**, always visible. N, E, S, W rotate around the outside. The centre ring is a clock: a sun circles it by day and becomes a dim moon at night.
- **Minimap**, only in a few areas such as inside Gwernia castle and some dungeons.
- **Party portraits** appear at the bottom centre when the player stands still for a moment or presses B. Only portraits are shown. The minimap also grows at this point.

### Camera

The camera is a floating third-person camera, similar to Super Mario 64. Holding R lets the player move it. It's the same in trek mode and battle mode.

### Controls

| Input | Function |
| --- | --- |
| Analogue stick | Move (walk under 80% tilt, run above); switch panel tabs in menus |
| A | Activate / confirm |
| B | Back in menus; show party portraits in trek mode; cancel a selected action in battle |
| Z | Hold to sneak; previous pause menu tab; skip turn or cancel an action in battle |
| L | Same as Z in menus and battle |
| R | Hold to move camera; next pause menu tab |
| Start | Pause menu (System and Party tabs only in battle) |
| C-left / C-right | Cycle party members in the pause menu |
| C-down | Item/entry info in menus |
| C-up in battle (hold) | Potions and item spells |
| C-down in battle (hold) | Spells and skills |

**(decomp)** Confirmed in the decomp so far: the walk/run threshold, the Z, L, R, and Start rows, B cancelling in battle, and both battle C-button rows.

### Saving

The game allows saving anywhere, and each save stores a small screenshot, which is unusual on the N64. A reimplementation that reads original saves will need to account for the image data. Saves are unrelated to the checkpoints used when fleeing battle (see Battle mode).

**(decomp)** The screenshot is 64×32 pixels at 16-bit colour. A save also holds the party, player position and map, the event flags, game state, a minimap-reveal block, and per-map object state, followed by a checksum. A save-point object type exists in the map format but is never used.

### Camping

Camping is started from the World tab and is allowed anywhere outside towns. It:

- Advances time by eight hours.
- Fully heals the party.
- Regenerates spell battery by the amount eight in-game hours would, which may not be a full recharge.
- Has a chance of triggering an ambush battle. Ambush encounters vary by area, and the Ranger skill lowers the chance.

## Open questions

These are unresolved from observation and are good targets for ROM inspection or a focused playthrough.

Answered items were resolved from the decomp. Details are in the relevant sections.

- [x] Is loading done at chokepoints, or is there some streaming? **Streaming: a 3×3 block of map chunks around the player.**
- [x] Do all interiors physically exist somewhere in the worldspace? **No. Many live in separate map sets, including three shared interior sets.**
- [x] Does the calendar drive the weather? **Partly. The season and terrain type set the chance of precipitation.**
- [x] Do wands have their own equipment slot? **Yes.**
- [x] Is there a legwear slot, or is that bracers? **Neither. There's a belt slot, and gauntlets go in the gloves slot.**
- [x] Is Z a hold or a toggle for sneaking? **Hold.**
- [x] Do Z and R map to previous/next pause menu tab, or the reverse? **Z (or L) is previous, and R is next.**
- [x] Which of C-up and C-down opens potions in battle? **C-up.**
- [x] Does the party tab header show XP? **No.**
- [x] What goes into the stat soup for turn order, field of influence radius, and spell radius? **Base Dexterity bands for turns; modified Dexterity for movement; effective spell level × spell range for spells.**
- [x] How does the number of caster-centred area targets scale? **Half the effective spell level, rounded up.**
- [x] Do obstacles block ranged attacks or spells? **Yes, including other combatants' bodies.**
- [ ] What does Wind do when self-cast? *Partly answered: it applies a timed enchantment that likely clears fog and storms. Needs checking in play.*
- [x] How is the enemy party leader determined? **Highest base Intelligence.**
- [x] Where are the flee checkpoints, and are they related to anything else? **The farthest of the last 15 recorded positions. Unrelated to saves.**
- [x] What's the full list of battle actions? **See the battle control list in "A turn". Whether equipping a new weapon ends the turn is unconfirmed.**
- [ ] Does the status effect cycle have fixed slots? *Not found in the decomp yet.*
- [ ] Can scrolls be equipped and cast from, as the three scroll equipment slots suggest?
- [ ] Do town exteriors also exist in the overworld map set, or only in their own sets?
