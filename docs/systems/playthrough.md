# The playthrough bot

`hbht playthrough` plays the game headless from a start (a new game, or a save) to a goal (a flag set, or the ending's credits), and reports every place where what it planned and what the game did part ways. It isn't part of `dotnet test`: it's run by hand, for one full-game pass before a release, or by a mod's author over their own plugins. It reads only the installed plugins, so a mod's content is played as the base game's is. Its saves along the way are checkpoints of a party levelled as a player's would be, for the user to play from by hand.

It finds softlocks, flags nothing can set, scripts that stop, routes that can't be walked, battles a sensibly levelled party can't win, and crashes. It doesn't judge anything drawn or heard, or whether the port plays as 1.0 does: a fault the port shares with 1.0 plays as 1.0's does.

The code is in `src/HBHT.Core/Playthrough` (namespace `HBHT.Core.Playthrough`), beside the other tools over the real rules (`Reach`, `MovementCompare`), and the CLI's report in `src/HBHT.Cli`. Its real-data tests are in `tests/HBHT.Rom.Tests/Playthrough*.cs`, slow ones marked `Slow`. The roadmap's [Phase 4](../roadmap.md#the-playthrough-bot) holds each slice's status.

## The layers

The hard part is deciding what to do next, not playing it: the core already plays every tick of the game headless. So the bot is a planner over an abstract model of the quest graph, and a pilot that plays each step of the plan in a real `GameSession`, checking after it that the game did what the plan said it would.

1. **The planner** (`QuestPlanner`). Its actions are what a player does: go to a map object and use it (talk to an npc, read a sign, open a container, take a teleporter, walk into a tripwire or a script object), pick a choice in a conversation, win a battle. Each has preconditions (the object's visibility and enabled gates, the flags its script's branches read, the key items a script takes, a teleporter's lock and trap flags, which map set and where the player is) and effects (the flags it sets and clears, the members it adds and removes, the items it gives). `QuestIndex` already holds who reads, sets and clears each flag, and which object starts which script. It answers two questions: from a state, which goals are reachable at all (the static pass, Phase 4's "every flag reachable"), and the next steps to a goal (by chaining back from the goal flag through what sets it, then searching forward over only the flags that matter).
2. **The world graph** (`WorldGraph`). Where the player can walk in each map set, from the cells `Reach` explores with the real `PlayerMotion`, joined into a graph and linked across map sets by teleporters, with their lock and trap flags as gates. It answers "a route on foot from here to that map object", as a list of waypoints in each map set and the teleporters between them.
3. **The headless host** (`PlaythroughHost`). What the runtime is to a `GameSession`, without drawing: it implements `ITrekHost`, `IPauseMenuHost` and `ICombatHost`, and carries out every `Transition` as `Game.cs` does (begins and ends cut-scenes, battles and cinematics, plays the credits out, takes loot, leaves shops and training), so the session plays from a new game to the credits with nobody at the controls but the pilot.
4. **The pilot** (`Pilot`). Plays one step of the plan: walks a route by the stick (`PlayerInput`), recovering when it's stuck, faces the object and presses A, steps through a conversation's pages and picks the choice the plan asked for, and reports what changed against what the plan expected.
5. **Battles** (`BattlePilot`). The party is played by the original's combat AI, as the enemies are. A battle it loses is played again from its start and won outright, and the report says so: it's a sign the party is under-levelled there. Experience goes up by a build per character (what each spends its points on at a level), and trainers on the way are used.
6. **The run** (`Playthrough`). Holds the goals, replans after every step, saves at each milestone, and writes the report.

## The planner

The planner is `QuestModel`, `ScriptWays`, `FlagModel`, `PlanState`, `Relaxation`, `StaticPass` and `QuestPlanner`. `hbht playthrough reachable` and `hbht playthrough plan --goal flag:<n>|member|ending` print it over the installed plugins (`PlaythroughReport`).

**Actions.** `QuestModel.Build` turns the plugins into actions in about 7 s. Each action is one way of doing one thing:

- talking to an npc, or using a script object, is an action per way through its script;
- a container, a tripwire, an encounter, a secret door, a door's lock and its trap, and entering each world map region are an action each;
- a new game's opening (`game.started`'s triggers) is the first step of any plan from one.

An action needs its object to be there (its gates, as `MapObjectPresence` reads them), its place reached (`Reaches`), and whatever its script's conditions need to go its way. Its effects, in order:

- flags set and cleared;
- members joining and leaving, with their flags;
- key items gained, with their held and region flags;
- after a battle won: the encounter flag, the rules' end flag, the bosses' flags of the enemy list's first four, and the party's loot.

A battle with the Shadow while the Horn of Kynon is held has the merge as a second outcome (the combat record's `shadow`).

**What code decides** is a `CodeGate`: skill checks, gold, stats, locks' and traps' levels, drop chances, and winning. The planner takes each to hold, and lists it with the step for the pilot to meet. Flags the engine writes (the calendar, rain, terrain) can be either value, since the pilot can wait or walk for them. Derived flags follow from their formulas over what each input can be (`FlagModel`), with no correlation kept between inputs.

**A script's ways come from the real runner**, not a model of it. `ScriptWays` runs the script again and again with a probe (`IScriptProbe`, an optional parameter of `ScriptRunner`) answering each condition and taking each effect that changes the state:

- each condition the way so far doesn't settle is a branch, and so is each page of choices;
- the runner's own walk decides the rest (menus going back, cases, choices hidden once taken), so the ways can't drift from what plays;
- ways with the same effects are merged, keeping each branch's choices (`OtherChoices`), and the pilot takes whichever the page offers;
- a cut-scene's ways go on through its `script.ended` triggers and the cinematics they play.

From a new game on NA 1.0 that is 7,705 actions over the 429 scripts something starts, with 4,086 ways. An if node whose holding leads only through single-linked lines with no op changing the state, back to where its not holding leads, is a detour (`ScriptWays.Detours`): it goes the short way unasked, rather than being walked as a branch. Branches are walked shallowest first. Two scripts with 23 member checks each, `0x039F` (Becan's scene) and `0x0404` (Arturo's at the bridge), are still cut at 50,000 runs, in their deep full-party branches. Before detours, every combination of their eleven spawning checks came before the ways that mattered, and Arturo's ways (3460, 3461, joining) were missing. Every one of the 162 ways open after the opening, played from the same save on the real runner, does to the flags and the party what its action says.

**The static pass** relaxes every action from a state, with nothing ever undone (FF's relaxation, `Relaxation`). What never comes true in the relaxation can't come true in any order of steps. From a new game on NA 1.0:

- 1,882 of the 4,972 plain flags can be set;
- 458 of the 555 derived flags can be set;
- all 13 members can join;
- 408 of the 429 started scripts can run;
- the ending can roll, and the merge with the Shadow can happen.

Each plain flag that can't be set is given its reason:

| Reason | Flags | Of them read |
| --- | --- | --- |
| Nothing sets them (the 17 "Has Map" flags and the Skull Key's among them) | 2,928 | 139 |
| A container's trap going off, which is never planned | 87 | 87 |
| Their setters need something that never comes true | 28 | 23 |
| Only scripts nothing starts set them | 24 | 10 |
| Only the engine sets them (the members' died flags) | 11 | 11 |
| No way through their script reaches the setter (the death scenes' done flags) | 8 | 0 |
| Their setters are map objects gated off on flag 0 | 4 | 4 |

**Planning.** `QuestPlanner.Plan(state, goal)` works in three stages:

1. It chains back from the goal through what makes it true, and through those actions' preconditions' setters, to the actions that matter.
2. It searches forward over only those: enforced hill-climbing on the relaxed plan's length, trying the helpful actions first, falling back to best-first where that stalls.
3. It drops every step the plan works without, so a plan doesn't wander.

A goal the relaxation can't reach comes back with why: what sets it, and the first thing each setter needs that never comes true.

**Places.** On its own, the planner counts every place as reachable (`Everywhere`), so its plans follow the story's flag gates alone, though most of the game's progression is geography. The run puts the world graph behind `IPlaceReach`, which turns the gates on a place's way into preconditions (see [the run](#the-run)). Without it, from a new game:

- the first member is three steps: the opening, script 0x0362, then the companions' scene at Overworld I13 (0x0527), where Abrecan joins;
- the ending is seven: the opening, 0x0362, Roog's scene (0x04F4), the tripwire at M06, and the last three scenes at Gwernia castle (0x04FC to 0x04FE), with the two battles won.

**What it doesn't model:**

- deaths and traps going off;
- buying anything but key items;
- camp ambushes and training;
- wanderers catching the party;
- encounters' random extras;
- the `item.gained` and `spell.trained` triggers;
- the cut-scene chain at a named place.

A tangible script object or tripwire goes off as the player walks past it. The planner treats it as optional, so the pilot routes around the ones it doesn't want, and one that turns the player back is got past first ([story triggers](#story-triggers)). A condition on a derived flag, read after the same script wrote one of its inputs, is read against the state the script started with.

## The world graph

`WorldGraph` (`WorldGraph.cs`, `WalkGraph.cs`, `RouteTypes.cs`) is where the player can walk, and how he gets to the place to stand to use a map object.

**Walk graphs.** Each map set has one, made from `Reach`'s exploration at the original's movement and pace (`Reach.Options.Edges`). A node is a 1×1×1 cell his feet passed over on the ground. An edge is a step he made from one cell to the next in a run, so it goes one way and is something the real `PlayerMotion` did: a ledge dropped off has a way down and none up unless some run climbed it. Each cell keeps the mean place of his feet in it (`Reach.Feet`), which is what a pilot steers for, since steering at cells' middles runs into walls' corners. A graph is made when its map set is first asked for and cached under the data folder's `cache/playthrough`, keyed by a fingerprint of the graph's format, the explorer's options and the plugins. The base plugin's file times are left out of it, so importing the same ROM again keeps the cache. From a cold cache, all 17 map sets take about 86 s (the Overworld's 1,007,448 cells are 8 MB of the 9.5 MB, before its toggles' ground). A warm read takes milliseconds.

**Teleporters** join the map sets. There are 894 outside the Battle arenas, and 96 carry a gate that stops the way: a visibility or enabled flag, or a lock its flag opens. A tangible teleporter's lock, trap and secret are skipped, as `MapObjectUse.Touch` skips them. Traps, and secret doors needing no skill, never block, but they are reported. 42 teleporters have no reached ground within their radius, and one (`0/C09/14`) has no arrival ground, so they aren't edges. A teleporter puts the player in the cell his feet are in once he's set down at its arrival, as trek's teleport sets him down (`Reach.SetDownAt`), each map set's made once (`WorldGraph.SetDowns`). The arrival point snapped alone can pick the twin of a cell a unit below a floor at a whole height, where runs ended against a wall and none set off: so the bridge's door back into Gwernia castle's courtyard (4/M05/0) once arrived in a pocket of five cells, and the library, the lab and their stairwell (4/K07) had no way out but the Erromon portal behind 2136. The gates are data (`RouteGate`), read against the `GameState` when a route is asked for, so one graph serves every moment of the game.

**Routes.** `Route(state, from, mapObject)` runs A* across the graphs and the teleporters, to the nearest ground within the object's use range by trek's rule:

- a container's reach plus 0.5;
- the radius of a teleporter, tripwire, script object or save point;
- about 1.5 for an npc or encounter, which wanders.

It returns waypoints per map set, the teleporters between them, and where to stand, in about 0.2 s. With no route, it says why:

- **`ObjectGated`**: the object's own gates are shut.
- **`Gated`**: a route exists only through closed gates. A second search lets gates through at a heavy cost, and names the fewest that block.
- **`NotOnFoot`**: the object isn't connected even with every gate open.

`CanReach(mapObject, state)` answers the planner from a flood cached per start and per state of the gates. 3,012 asks take 0.16 s. Routes, `CanReach` and the flood start where `WalkGraph.SnapStart` says: the cell the player's feet are in, if a way from it goes on (64 cells ahead, `Ahead`); else, of the cells within 1.5, the one most cells lie ahead of; else the nearest within 6 that some step leads on from. A run sets off again from only one rest in each 2-unit start cell, so a player left beside it, as after a battle, can stand on a cell steps lead into and few or none out of. And ground at a whole height splits into twin cells a unit apart, as the sphere's bob floors its feet to one or the other, and the lower twins by walls are such sinks: by Gwernia castle's observatory door (4/O05/1) the nearest cell to the feet was the lower of two that lead only into each other, and from it no plan went anywhere. Snapped that way, about 6,700 ordinary standing places across the map sets would have started in ground that can't get back to a new game's start, 6,336 of them in the Overworld; with the own cell first, none do.

**Collision toggles.** The nine tripwires that change their chunk's collision (`ChangeCollisionAction`) open walls the graphs would otherwise keep shut:

| Tripwire | Gate | Gated steps |
| --- | --- | --- |
| Overworld P24/1, the Jundar gate, the only way onto the south-east island | enabled by 480 | 32 |
| Overworld T27/0 | enabled by 3699 | 36 |
| Overworld D14/0 | enabled by 5533 (Arturo's scene) | 13 |
| Chaos Isle C01/0 | enabled by 3437 | 12 |
| Chaos Isle A07/3 | enabled by 5498 | 6 |
| Barrows G02/0 | visible by 533 | 10 |
| Barrows G03/5 | enabled by 533 clear | 5 |
| Barrows G03/6 | enabled by 4279 | 8 |
| Barrows H03/4 | enabled by 533 | 37 |

Each is explored about with its tripwire taken as gone off (`Reach.Explore`'s `tripped`, which changes the toggles and their neighbours' copies as each chunk loads). The exploration starts from the ground within 6 of the toggle's triangles, and sets off again only near them or from ground no exploration has reached; a toggle beyond another, as in the Barrows, is explored from the ground the first opened. A step it made from known ground into ground only it found, or between two known cells otherwise more than 8 steps apart, is a gated step of that tripwire (`WalkGraph.GatedSteps`); the ground only it leads to is joined by ordinary steps. `WorldGraph.Toggles` holds each tripwire's own visibility and enabled gates, read against the game as a teleporter's are (`IGatedWay`). Routes, `CanReach` and the flood take a gated step only while they hold, the flood counts a toggle once per way, and a route names the toggles it crosses (`RouteLeg.Toggles`). The pilot walks across as a player does: the tripwire goes off as he comes within its radius.

**Interiors** (every map set but the Overworld) are explored twice, with runs of 2 and of 10 seconds, and the two joined, since the original's sphere climbs some ramps and stairs only by pushing up them longer: Jundar's gatekeeper (11/A01/19) stands on a floor 5 above the one the doors put the player on. With both, every map set's graph is made in about 86 s.

**Following a route.** The graph promises that someone walked each step, not that steering from any approach works. The original's sphere physics carries speed over lips and into pockets, so a pilot must watch for a stall (no progress for about 1.5 s) and replan from where it stands, avoiding where it stuck (`RouteOptions.Avoid`). The tests steer the real `PlayerMotion` this way:

- to a chest in the next Overworld chunk from a new game's start: arrived, with no replans;
- to a chest through a teleporter in the Gwernia interiors: arrived, with two replans.

**Left out:**

- Story triggers on a way (a tangible script object that plays a cut-scene) aren't gates: a route walks through them as if they set off nothing that stops it, and the run finds the ones that turn the player back by walking into them ([story triggers](#story-triggers)).
- Teleporters reached by trigger triangles aren't edges.
- Only running is tried.
- Ground reached only some way other than `Reach.Starts` isn't in a graph.

## The pilot

`Pilot` (`Pilot.cs`, `PilotStep.cs`, `WantedChoices.cs`, `GameSnapshot.cs`) plays one step in a host's session, as its `IInputPolicy`. A step (`PilotStep`) is a map object to use, how to use it, the choices wanted in what it starts, and what it should leave so. It knows nothing of the planner's actions; the run translates between them.

**Steering.** The stick is read along the trek camera's look: `PlayerMotion.Control` turns it by `LookXZ`, a rotation that is its own inverse. So the way the pilot wants to go becomes a stick through the camera's current look (`StickFor`). It walks the world graph's route:

- a waypoint is passed within 0.8 across the ground, looking up to 3 ahead;
- a stall is 1.5 s without getting 0.2 nearer, after which it steps aside for half a second (to one side of the way it wanted, the other, back, and between, in turn) and replans from there, avoiding where it stuck: walking straight at a lip the sphere doesn't climb moves him nowhere at all, as in EhudUnder's C03 below the teleporter C03/3, where the walk graph's step up was made from another approach;
- it also replans whenever trek gets the controls back (after a cut-scene, a conversation, a battle a wanderer started, a menu or a teleport), when the map set changes, and when the player jumps more than 3 in a tick;
- where a route is refused only by gates the hour opens (each gate's flag closed now, and derived, and the planner, which takes the calendar's flags either way, says it can open), it waits where it stands a game hour at a time, up to a game day, and asks again: the Burial Chamber's door in Ugarit2 A05 is locked behind derived 5022, which night (5283, the time of day) opens; the waiting isn't counted against the step's budget;
- it gives up after 10 stalls or 40 replans.

**Keeping clear.** By default a route keeps out of the radius of every other enabled, tangible script object in its chunks (`RouteOptions.KeepClear`), so a step plays what it was asked to and not the story's cut-scenes on the way. Where it can't, it lets the triggers the plain route crosses be.

**Teleporters and use.** At the end of a leg, a tangible teleporter is walked into, and any other gets an A once trek's own weighing (`MapObjectUse.Reach`) would pick it. The step's object is used the same way:

- a container, sign, door or save point by A once A would reach it (`NotUsed` after 5 s in range);
- a tangible object by walking to its middle; a tripwire that writes a flag counts as used once the flag is written, since it goes off only where the chunk's grid lists it under the player, and the edge of its radius may not (Barrows B01/0);
- an npc or encounter by steering at its body once it's within 10, since it wanders, and pressing A once A reaches it.

The step is done once trek has had the controls for a second after the use, so whatever it started (a conversation, cut-scenes, a battle, loot) has played out by the host's policies.

**Choices and the check.** `WantedChoices` takes the choices wanted, keyed by script and node, and leaves every other page to the host's default:

- a wanted choice the page doesn't offer is recorded, with what it did offer;
- a wanted choice whose page never came up is listed as not reached.

`GameSnapshot` takes the plain flags, the members, the inventory and the gold before and after. The outcome lists what was expected and doesn't hold (`Misses`), and what changed that wasn't expected (`Unexpected`). The unexpected changes are information, not failure.

**Outcome.** A `StepOutcome` says the step was done, or why not: `NoRoute` (with the graph's refusal), `Stuck`, `NotUsed`, `OutOfTicks`, `GameOver`, or `Blocked` by a story trigger that turned the player back (`StepOutcome.Blocker`, [story triggers](#story-triggers)). It also carries the ticks, replans, stalls, changes, and the transitions, pages and battles it saw.

**How the story leaves I08.** The Goblin Ambush's starters are all enabled by derived flag 5466: flag 45 set and 11 clear. Only Edur's cut-scene (0x0391, script object I08/4) sets 45. The opening leaves the player on raised ground, and its only way off passes within about 6 of that object. Then every way on to the ambush crosses one of Alaron's cut-scene's objects (0x0364: I08/8, I08/11, H08/5), so nothing can be kept clear of from a new game, and the story plays as it does for a player. Played from a new game:

1. Edur's object: 1,884 ticks, no replans.
2. The ambush's object I08/16: 9,905 ticks, two replans. In order: Alaron's cut-scene, a wanderer's battle won outright, the ambush, its battle spared, and Oriana's two cut-scenes. It ends in trek in her hut, whose door (6/E03/2) is locked behind flag 50.

Elsewhere, an npc 122 away in Gwernia is talked to in 1,978 ticks, and a chest through a door is opened in 1,294 ticks with two replans and its loot taken. A step takes well under a second of wall time once the Overworld's graph is loaded.

**Left out:**

- Script objects that only trigger triangles name aren't kept clear of.
- An npc whose placement has no reached ground near it is refused by the graph, even where its body can be reached.
- Only running is used, never sneaking.
- A consequence starting more than a second after trek gets the controls back counts in the next step.
- A door's lock and trap popups are left to the menu policy.

## Battles

`BattlePilot` (`BattlePilot.cs`) plays the party as the original's combat AI plays the enemies. As a battle begins, it hands every party-side combatant to the AI (`Battle.PlayByAi`). Each member gets an AI record from their sheet, as Sholeh is given one, and chooses targets, spells and walks by the same styles and scores. As with Sholeh, the AI's casts take no reagent, and a thrown flask isn't taken from the inventory. The bot never flees: the AI's flight is the enemies' alone.

**No blasts.** The members' blast spells are taken from their AI's action slots (`CastsBlasts` gives them back). The AI aims a Fireball at the opponents' midst, whoever of its own side stands there, and a count that catches more of its own side wraps the score byte to the best of all ([combat-ai](combat-ai.md)). A member's Fireball took the whole party with it.

**Losing.** A battle is lost as the original loses one: every member down, or Alaron dead (`TurnOrder.Advance`, `Combat.Defeat`), with the game over's script following. A non-lethal battle ends `Spared` and isn't a loss. To rewind:

1. As trek cues a battle, on its last tick before `BeginBattle` (`IBattlePolicy.Cued`), the pilot notes the player's place and keeps a save.
2. On a loss, the host starts again on that save once the tick is over (`PlaythroughHost.Restart`), and cues the same setup.
3. The replay is won on the party's first turn, through the game's own victory (`Battle.ForceWin`).

So the flag, the gold, the experience, the post-battle windows and trek where the battle began each come once, and anyone who died in the lost battle is alive again. The replay's `BattleFought` is marked `LostFirst`, the report's sign that the party is under-levelled there. A replay lost again is left to end the game, and noted.

**A death is played again too** (`BattlePilot.RewindDeaths`, on by default), the bot's choice: a battle won with a member dead in it goes back to its start the same way and is won outright, as a player who loses a companion reloads. The replay is marked `DiedFirst`, and the battle undone (`BattleFought.Undone`) counts no deaths. The save keeps the game's random streams, so playing it again by the AI would go the same way. Deaths cost the run more than anything else: a party of four that loses two in one dungeon is left with Alaron alone, whose every battle is then lost first, until one is lost before the party's first turn and the game ends, as it did at the Lighthouse.

**Stalemates.** The AI walks by the original's greedy path (`ArenaGrid.FindPath`), which any pocket stops. Against a player that rarely matters, but two AIs who both find no way rest turn after turn: a new game's Alaron and B06's Ogre do, in arena 16/E03. A battle is routed and won outright, marked `Stalemate`, when either holds:

- nobody's hit points change for three minutes (`QuietTicks`);
- it goes on half an hour (`MostTicks`).

**Deaths.** With `RewindDeaths` off, or where a replay can't undo one, a member who dies holds their slot until the battle ends. Then they leave the party as in the original (`GameState.BringOutYourDead`, `Party.AfterBattle`): their died flag is set, their joined flag stays, and their gear goes to the inventory ([characters](characters.md)). `Party.HasEmptySlot()` counts only the living, so the run sees the slot at once. `BattleFought.Deaths` lists who died in each battle.

**Levelling.** Experience is spent only where the game lets it be ([characters](characters.md#training)):

- in the post-battle window, on attributes, skills above 0 and spells already known;
- at a trainer, on what they teach, for less experience and some gold.

`Levelling` (`Levelling.cs`), a menu policy over the host's default, does both through the windows' own presses, so the game's rules decide every purchase. What it buys is each member's build (`Builds.cs`, a table a mod can replace):

The builds are chosen as an experienced D&D player would by role, from the rules' own terms: a melee blow's chance is Strength + Endurance + Level + the weapon's hit + 6 × the weapon skill + 2 × Warrior, less the target's 2 × Dexterity, Level and shield defence (the shield's defence × the shield skill, + Warrior); its damage is (the weapon's dice + Strength's tier) d6, the tiers stepping at 4, 9, 17, 24, 28, 32, 36 and 39, less protection; hit points are Endurance + Stamina + Level, and spells spend Stamina. A point or level from n costs (n + 1)² × its cost: Stamina 4; Strength, Endurance, Intelligence and Willpower 12; Dexterity 24; a weapon skill 600, the shield 500, Warrior 1,000 and Wizard 1,500. So the weapon skill is the cheapest chance to hit, the shield skill the cheapest defence with a big shield (a Tower Shield's 6 a level, three times Dexterity's), Stamina the cheapest hit point, and Strength is worth its price at the tiers.

| Build | Fits | Leans on | Why |
| --- | --- | --- | --- |
| Mage | Wizard 2 or more, and above Warrior | Wizard and spells together, Intelligence, Stamina, Willpower | A wizard: a spell is cast at the lesser of its level and Wizard, Intelligence is the casting, Stamina pays for each cast and is the hit points, Willpower is the defence against magic; the weapon only late. |
| Spellsword | Wizard 1 or more | the weapon, the shield, Stamina, Strength, then Wizard and spells, Warrior | A paladin fronting the line: Alaron, whose death ends the game, is built to last as a fighter first, his magic alongside. |
| Rogue | Thief 3 or more, and above Warrior | the weapon, Thief, Dexterity, Stamina | A rogue striking from behind, where Thief adds ×10 to the chance and ×2 to the damage; Dexterity is their defence. Stealth is left: the AI doesn't sneak, and the scripts' Stealth check is a stub. |
| Fighter | anyone | the weapon, the shield, Stamina, Strength to its tiers, Warrior, Endurance | The front line: the cheapest chance to hit, the cheapest defence, the cheapest hit points, then the damage tiers and Warrior, which adds to chance, defence and protection at once. |

The first build that fits is chosen afresh each time from the member's own skills, so any recruit gets one. Each build is in tiers of lines such as "Wizard to 4". In the lowest tier with anything buyable there, the cheapest affordable thing is bought, and a tier whose wants can't yet be afforded is saved for. No one learns more than five spells, since the AI fights with only the first five. `Levelling.Train(host, trainer)` buys at a trainer what the builds want, within the gold less `GoldReserve`. Everything bought is in `Bought`, and anything a window refused in `Refused`.

**Gear.** Loot goes into the inventory, and a player puts on what's better. `Outfitting` (`Outfitting.cs`), a menu policy under `Levelling`, does so through the Party tab's presses: every 300 ticks with the controls in trek, the pilot asks it (`PilotOptions.Dress`), and once the inventory holds something that gains a member more than half a point, it opens the tab, and presses C-left to the member, the stick to the inventory, the row and A, until nothing better is left. The game's rules equip it and refuse what the member can't use, which isn't asked again. It weighs by the rules' terms (`Outfitting.Worth`):

- a melee weapon by its chance to hit against a middling foe (the member's Strength, Endurance, Level, Warrior and skill with it, plus its hit, less 50, from 5 to 95%) times its damage ((its dice + Strength's tier) × 3.5 + Level);
- body armor and gear by 4 × their protection, which comes off every blow, and the attributes they add, Dexterity twice;
- a shield by its defence × the member's shield skill, and 4 × its protection.

Weapons that fly, rings, scrolls and wands are left as they are. What it put on is in the report's purchases as `wore`.

An item is weighed on the member as they'd be wearing it: their stats and skills less what the item it replaces adds, and with what it adds itself; a stat the formula reads (a weapon's Strength and Endurance) isn't counted again in the flat term. Weighed on the member as they stood, Warfang's Warrior +2 raised the Chaos Sword's chance while Warfang was worn, so each looked better while the other was on, and Abrecan swapped them for ever, every step running out of ticks in the Party tab. As a guard, a member who puts the same item on a third time doesn't put back what it replaced (`MostPutOn`).

**Shops.** After a step that's done, as with trainers, a shopkeeper is visited if they're within 150 of the player (225 of walking: the shops are in the towns' interiors, where nothing wanders), their gates are open, a way through their conversation that opens the shop can be taken now, and their stock has something that gains the party more than half a point over the best of its kind it wears or carries (`Outfitting.Gain`) for one and a half times its price, within the gold less a reserve of 1,000. The pilot talks to them by that way, and `Outfitting`, with `Shopping` set, buys in the shop by its own presses (the stick to the stock, the row, A), the best gain first, one at a time, until nothing more is worth it; the dressing puts it on after. A shop isn't visited again until the party has more gold than when it last was. What was bought is in the report's purchases as `bought`. Before it, the party fought the whole game in what it joined with: at the Lighthouse Alaron had a Short Sword and Leather Armor, with Warfang, Partial Platemail and a Scorpion Shield in the inventory and 63,000 gold.

**How it fares.** `tools/cover.ps1` for 15 minutes from a new game over all 2,230 targets, 20 workers in one process, before branches avoided their losing step: 49 jobs and 905 steps (about 54 a minute), 976 targets covered (about 58 a minute), 249 given up, and 1,005 still live in the 5 jobs left pending when the time ran out; no process died and nothing stranded. The root job alone plays about a step a second; the rest of the throughput is the branches beside it. Given up, by reason:

- 150 no plan, mostly "n action(s) do it, none can be done": their setters banned after failing, or their code gates unmet where the run stood;
- 88 lost again on a branch's first step, which branches avoiding their losing step now answers;
- a handful after three failed steps: the tripwires 4/E03/0 and 4/G07/1, which play out without setting 33 and 35; 0x036F at 4/O09/6, whose way takes choice 19 over a hundred times; and the pilot stalling at Overworld (913.9, −17.6, 1090.3) on the way to Jundar's 11/C05/13 and to N17 and I13.

**Left:**

- Stage 1, the trunk: a story run with a save before every step, and each target given to the save where it's last reachable or nearest the player while it is, as the jobs' starts. Until then the root job sweeps from a new game, and the story moves on only when nothing near is left.
- Waiting for the hour: derived targets over the calendar wait and are given up.
- A loss by the places alone may be the world graph's, not the game's, as the castle's was ([teleporters](#the-world-graph)): look at the cells where the step left the player before taking it as the game's.
- Branches aren't capped by depth or count, and a branch whose targets are covered elsewhere is still claimed, to finish at once.
- The ledger puts what a trainer or a shop visit sets down to the step before it.

## Where it stands

From a new game the run reaches the ending: the Coronation of Alaron plays and the ending's credits roll. It plays 87 steps, 3,688,396 ticks (about 1,025 game minutes), in about 126 s of wall time (the run is deterministic: the same plugins play the same run, and a change to where routes start, as the set-down arrivals and `SnapStart` were, sends it another way).

- It fills the party in 6 steps (Abrecan, Brenna, Rheda), and reaches Erromon (step 7), the goblin camp, Jundar (17) and the herb (23), Arturo joining (27), Port Saiid and the Lighthouse (28, 29), Terminor and Niesen (37, 38), EhudUnder (39), Farris and Niesen's death (45), Abrecan back (47), Ugarit (50), the Barrows (53), the Shadow's merge (83), Roog's cave and Rooughah's naming (84, 85), and the last scenes at Gwernia castle (87).
- It fights 269 battles: 56 lost first and 24 won with a member dead, each played again and won outright; 2 stalemates; no deaths in battle stand; Niesen's death in EhudUnder is the story's. It camps 66 times, trains twice, buys 6 things and puts on better gear 52 times.
- The party at its milestones: Alaron 2 as Rheda joins; 12 at the herb; 13 at Terminor as Niesen joins; 16 at Ugarit; 19 at the Shadow's merge and the ending, with Brenna and Rheda 18 and Abrecan 17. The user's own game had Alaron at 14 before Shamsuk's Tower and 20 before Rooughah.
- The lost-first battles cluster on the long walks: 17 of them on the walk from the goblin camp's C09 to the Jundar gate (P24/3) at level 7, and most of the rest in EhudUnder.

**Blockers met, in order** (every one the bot's):

1. The dryads' scene at G12/5 turned the player back with the Amaranth ([story triggers](#story-triggers)).
2. The party fought the whole game in what it joined with, and deaths left Alaron alone, until a battle was lost before the party's first turn and the game ended at the Lighthouse (gear, shops and deaths played again, in [battles](#battles)).
3. The pilot walked straight at a lip below EhudUnder's teleporter C03/3 and moved nowhere (the step aside).
4. The Barrows' B01/0 tripwire, entered at the edge of its radius, didn't go off (used once its flag is written), and with it and Ugarit2's 14/C03/1 both banned the run found no step (the bans tried once more).
5. The Burial Chamber's door in Ugarit2 A05, which night opens (the pilot waits for the hour).

**No game-side findings.** Every blocker was the bot's. Two things seen once that didn't come back: in one run the player stalled two steps running beside Overworld P17's teleporter P17/4, where a save of the same moment walks on (the run now saves and loads after a step stuck); and a shop visit to Tamberlain in Terminor bought nothing in the run where the same visit from a save of the moment buys the Boots of Speed.

**Next for the bot:**

- Levelling on the way: the walk from C09 to the Jundar gate at level 7 is where the party is weakest; a grinding goal before it, or a planner that weighs the regions' danger, would cut the lost-first battles.
- The shops weigh gain alone, not its price: the Belt of Life was bought for 26,650 gold for a gain of 4.
- 0x0404 and 0x039F are still cut at 50,000 runs in their full-party branches; the rounds' flood keeps one way per place; camp ambushes' stalemates.