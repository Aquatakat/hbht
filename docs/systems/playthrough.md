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

A tangible script object or tripwire goes off as the player walks past it. The planner treats it as optional, so the pilot must route around the ones it doesn't want. A condition on a derived flag, read after the same script wrote one of its inputs, is read against the state the script started with.

## The world graph

`WorldGraph` (`WorldGraph.cs`, `WalkGraph.cs`, `RouteTypes.cs`) is where the player can walk, and how he gets to the place to stand to use a map object.

**Walk graphs.** Each map set has one, made from `Reach`'s exploration at the original's movement and pace (`Reach.Options.Edges`). A node is a 1×1×1 cell his feet passed over on the ground. An edge is a step he made from one cell to the next in a run, so it goes one way and is something the real `PlayerMotion` did: a ledge dropped off has a way down and none up unless some run climbed it. Each cell keeps the mean place of his feet in it (`Reach.Feet`), which is what a pilot steers for, since steering at cells' middles runs into walls' corners. A graph is made when its map set is first asked for and cached under the data folder's `cache/playthrough`, keyed by a fingerprint of the graph's format, the explorer's options and the plugins. The base plugin's file times are left out of it, so importing the same ROM again keeps the cache. From a cold cache, all 17 map sets take about 86 s (the Overworld's 1,007,448 cells are 8 MB of the 9.5 MB, before its toggles' ground). A warm read takes milliseconds.

**Teleporters** join the map sets. There are 894 outside the Battle arenas, and 96 carry a gate that stops the way: a visibility or enabled flag, or a lock its flag opens. A tangible teleporter's lock, trap and secret are skipped, as `MapObjectUse.Touch` skips them. Traps, and secret doors needing no skill, never block, but they are reported. 42 teleporters have no reached ground within their radius, and one (`0/C09/14`) has no arrival ground, so they aren't edges. The gates are data (`RouteGate`), read against the `GameState` when a route is asked for, so one graph serves every moment of the game.

**Routes.** `Route(state, from, mapObject)` runs A* across the graphs and the teleporters, to the nearest ground within the object's use range by trek's rule:

- a container's reach plus 0.5;
- the radius of a teleporter, tripwire, script object or save point;
- about 1.5 for an npc or encounter, which wanders.

It returns waypoints per map set, the teleporters between them, and where to stand, in about 0.2 s. With no route, it says why:

- **`ObjectGated`**: the object's own gates are shut.
- **`Gated`**: a route exists only through closed gates. A second search lets gates through at a heavy cost, and names the fewest that block.
- **`NotOnFoot`**: the object isn't connected even with every gate open.

`CanReach(mapObject, state)` answers the planner from a flood cached per start and per state of the gates. 3,012 asks take 0.16 s. Routes, `CanReach` and the flood start from the nearest cell within 6 that some step leads on from (`WalkGraph.SnapStart`): a run sets off again from only one rest in each 2-unit start cell, so a player left beside it, as after a battle, can stand on a cell steps lead into and none out of.

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

- Story triggers on a way (a tripwire or tangible script object that plays a cut-scene) aren't gates: a route walks through them as if they set off nothing that stops it.
- Teleporters reached by trigger triangles aren't edges.
- Only running is tried.
- Ground reached only some way other than `Reach.Starts` isn't in a graph.

## The pilot

`Pilot` (`Pilot.cs`, `PilotStep.cs`, `WantedChoices.cs`, `GameSnapshot.cs`) plays one step in a host's session, as its `IInputPolicy`. A step (`PilotStep`) is a map object to use, how to use it, the choices wanted in what it starts, and what it should leave so. It knows nothing of the planner's actions; the run translates between them.

**Steering.** The stick is read along the trek camera's look: `PlayerMotion.Control` turns it by `LookXZ`, a rotation that is its own inverse. So the way the pilot wants to go becomes a stick through the camera's current look (`StickFor`). It walks the world graph's route:

- a waypoint is passed within 0.8 across the ground, looking up to 3 ahead;
- a stall is 1.5 s without getting 0.2 nearer, after which it replans, avoiding where it stuck;
- it also replans whenever trek gets the controls back (after a cut-scene, a conversation, a battle a wanderer started, a menu or a teleport), when the map set changes, and when the player jumps more than 3 in a tick;
- it gives up after 10 stalls or 40 replans.

**Keeping clear.** By default a route keeps out of the radius of every other enabled, tangible script object in its chunks (`RouteOptions.KeepClear`), so a step plays what it was asked to and not the story's cut-scenes on the way. Where it can't, it lets the triggers the plain route crosses be.

**Teleporters and use.** At the end of a leg, a tangible teleporter is walked into, and any other gets an A once trek's own weighing (`MapObjectUse.Reach`) would pick it. The step's object is used the same way:

- a container, sign, door or save point by A once A would reach it (`NotUsed` after 5 s in range);
- a tangible object by walking to its middle;
- an npc or encounter by steering at its body once it's within 10, since it wanders, and pressing A once A reaches it.

The step is done once trek has had the controls for a second after the use, so whatever it started (a conversation, cut-scenes, a battle, loot) has played out by the host's policies.

**Choices and the check.** `WantedChoices` takes the choices wanted, keyed by script and node, and leaves every other page to the host's default:

- a wanted choice the page doesn't offer is recorded, with what it did offer;
- a wanted choice whose page never came up is listed as not reached.

`GameSnapshot` takes the plain flags, the members, the inventory and the gold before and after. The outcome lists what was expected and doesn't hold (`Misses`), and what changed that wasn't expected (`Unexpected`). The unexpected changes are information, not failure.

**Outcome.** A `StepOutcome` says the step was done, or why not: `NoRoute` (with the graph's refusal), `Stuck`, `NotUsed`, `OutOfTicks` or `GameOver`. It also carries the ticks, replans, stalls, changes, and the transitions, pages and battles it saw.

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

**Stalemates.** The AI walks by the original's greedy path (`ArenaGrid.FindPath`), which any pocket stops. Against a player that rarely matters, but two AIs who both find no way rest turn after turn: a new game's Alaron and B06's Ogre do, in arena 16/E03. A battle is routed and won outright, marked `Stalemate`, when either holds:

- nobody's hit points change for three minutes (`QuietTicks`);
- it goes on half an hour (`MostTicks`).

**Deaths.** A member who dies holds their slot until the battle ends. Then they leave the party as in the original (`GameState.BringOutYourDead`, `Party.AfterBattle`): their died flag is set, their joined flag stays, and their gear goes to the inventory ([characters](characters.md)). `Party.HasEmptySlot()` counts only the living, so the run sees the slot at once. `BattleFought.Deaths` lists who died in each battle.

**Levelling.** Experience is spent only where the game lets it be ([characters](characters.md#training)):

- in the post-battle window, on attributes, skills above 0 and spells already known;
- at a trainer, on what they teach, for less experience and some gold.

`Levelling` (`Levelling.cs`), a menu policy over the host's default, does both through the windows' own presses, so the game's rules decide every purchase. What it buys is each member's build (`Builds.cs`, a table a mod can replace):

| Build | Fits | Leans on |
| --- | --- | --- |
| Mage | Wizard 2 or more, and above Warrior | Wizard, spells, Intelligence, Stamina, Willpower |
| Spellsword | Wizard 1 or more | the weapon, Warrior, Strength, Stamina, then Wizard and spells, a shield |
| Rogue | Thief 3 or more, and above Warrior | the weapon, Dexterity, Thief, Stamina, Stealth |
| Fighter | anyone | the weapon, Warrior, Strength, Stamina, Endurance, a shield |

The first build that fits is chosen afresh each time from the member's own skills, so any recruit gets one. Each build is in tiers of lines such as "Wizard to 4". In the lowest tier with anything buyable there, the cheapest affordable thing is bought, and a tier whose wants can't yet be afforded is saved for. No one learns more than five spells, since the AI fights with only the first five. `Levelling.Train(host, trainer)` buys at a trainer what the builds want, within the gold less `GoldReserve`. Everything bought is in `Bought`, and anything a window refused in `Refused`.

**How it fares.** From the user's checkpoints, whose parties carry 400,000 to 800,000 unspent experience a member, the AI-played party wins the early Overworld's battles, but loses most mid-game ones. Before Shamsuk's Tower it lost all 9 mid-game encounters tried. It also spends many turns on resistance spells. A run that levels as it goes should lose fewer. Spending a large hoard takes about 2,000 ticks of the post-battle window, so the run raises the host's `MenuPatience`.

## The headless host

`PlaythroughHost` (`PlaythroughHost.cs`, `.Transitions.cs`, `.Presses.cs`) owns a `GameSession` and steps it as `Game.Tick` and `_Process` do, without drawing:

- the presses for the mode that runs go in before the tick;
- after the tick, a cut-scene or battle that faded in is begun (`BeginCutScene`, `BeginBattle`), and one whose screen has faded out is ended (`EndCutScene`, `EndBattle`);
- while the session streams trek (`StreamsTrek`), trek's chunks follow the player.

**Transitions.** Every `Transition` is carried out as `Game.cs`'s switch does, less what only draws, and is recorded with its tick (`Transitions`). So are the `Warn` and `Log` lines and what the game said (`Notes`). Game over, Quit and Return to Title end the run (`End`). A save loaded from the System tab starts a new session on it. The title and a change of renderer have nothing for a host to do, and an unknown transition warns.

**Motion.** Bodies move by their actors' own animations, made as the runtime's views make them (`ActorMotion.AsDrawn` on the game's animation stream): the player's, each wanderer's the first time trek asks, and each cut-scene actor's after the tick that spawned it. The runtime makes a wanderer's only while its view exists, so the stream is drawn on in a different order than in play.

**What a player would press**, the host asks of four policies (`PlaythroughPolicies.cs`):

- **`IInputPolicy`**: trek's stick and presses, the pilot's seam. By default nothing is held.
- **`IConversationPolicy`**: how long a page shows, and which choice to take. The host presses as a player does, one button a tick. By default, a page is pressed on at once, and a choice is the first not yet taken at that node in this run. A conversation hides the choices taken, so this is the first shown. A book or the help menu shows them again, so these are read through to their last choice. Every page is recorded with its script, its node and the choice taken (`Pages`).
- **`IBattlePolicy`**: called as a battle begins, before each of its ticks, and as it ends. The default, `WinOutright`, wins on the party's first turn the player plays (`Battle.ForceWin`), through the game's own victory, so the flag, the gold, the experience and the post-battle windows come as in play. It can't win the Shadow's battle, or one lost before the party has a turn. The Goblin Ambush is one of those, and ends `Spared`, as the story does.
- **`IMenuPolicy`**: the pause menu's windows and trek's popups. By default:
  - loot is taken stack by stack through the real `BarterMenu`, so `ItemGained` triggers fire;
  - a pile that won't shrink is left;
  - shops, training and the menu are left at once;
  - a popup's question takes its first option;
  - a menu left open a minute is closed with a warning.

**Speed.** The host plays by `SessionSettings.Original`, but nobody's watching: cinematics are skipped with B, skippable credits are ended with A, and the ending's are rolled 8 ticks a tick (`PlaythroughOptions`). Standing in trek it runs at about 34,000 ticks a second in a Release build, some 560 times the game's speed. A short battle in a Debug build runs at about 2,300 a second.

**With nobody at the controls**, a new game plays the opening cinematic and script 0x0362's thirteen pages. Then, 988 ticks in, trek has the player at Overworld I08, and nothing more comes to him. Everything after that is the pilot's.

**What every host repeats**, which could move into `GameSession`:

- beginning and ending what a step cued or finished;
- streaming trek's chunks while `StreamsTrek`, which lives in `Game._Process`;
- making actors' motions as the views do (`MovementCompare.Play` has the same lambda);
- reading a loaded save and playing its opening if it stands nowhere;
- the menu's save.

## The run

`Playthrough` (`Playthrough.cs`, `StepTranslation.cs`, `WorldReach.cs`, `WorldGraph.Flood.cs`, `RunReport.cs`) plays from a host's game to a goal:

```
hbht playthrough run --goal flag:<n>|member|ending [--save <save>] [--saves <dir>] [--report <path>] [--most-ticks <n>] [--most-minutes <n>]
```

It prints a line a step and a line a milestone, then the report. It exits 0 if the goal was reached, else 2.

**Each step:**

1. It plans from the real state (`PlanState.From`), with the world graph behind the planner's places. The rest of the last plan is kept while it still holds from there.
2. It turns the plan's first action into the pilot's step and plays it.
3. It compares what the game did with what the step expected.

**Failure.** A step that fails (no route, stuck, not used, out of ticks, or an expectation missed) is reported. Its action is banned in the state it was tried from, and for good after three failures. A step whose object the pilot never used still counts as done if what it was for came about, as when an encounter catches the party first. The run gives up on a goal after 8 failed steps in a row (`MostFailures`), or when the game's ticks or the wall time run out. It also stops when the planner has no step, saying what the relaxed plan's next steps are and what keeps each from being done.

**Places in plans.** One flood of the walk graphs from where the player stands (`WorldGraph.Flood`) finds, for every cell, the fewest closed gates on a way to it. A teleporter costs its closed gates, read against the game's flags, derived ones included:

- a lock the party's thief picks (level under 10, thief skill above it) is open;
- a gate with no flag, or whose literal nothing can make true, is a wall.

The places are found in rounds (`WorldReach.Layered`), so that no gate is opened from beyond it. The first round floods with only the gates open now. Each round's relaxation takes the places that round reached as reached, and says which literals come true; the next round's flood opens those gates too. It stops when a round adds nothing: about 18 rounds and 6 s from Rheda joining. A place's way is the one the first round to get there found. Without the rounds, the shortest way to Overworld A22 went through the door 11/A01/8, locked behind 320, which only the Cradawgh's scene at A22 sets; now it goes round by D14's toggle.

A place's gates (`WorldReach.Gates`) are the literals of the closed gates on its way, such as `flag 50 set` for the lock on Oriana's hut's door (6/E03/2). The relaxation and the backward chain take them as preconditions of the actions there (`QuestModel.Needs`), and the search checks them in each plan state. So a plan to a place behind a closed door first makes its flag true. From inside the hut, every place outside needs 50, and only Oriana's conversation (0x0394) sets it.

A place ways reach only through walls is walled, and nothing gets there. A place no way reaches at all is off foot. The relaxation takes it as reached, since the story moves the player itself (the ambush's battle ends in the hut), but the search never steps there. A gate open now is taken to stay open.

**With no plan**, the run plays the relaxed plan's first helpful step it can do where it stands, with an `Unreachable` entry saying so: from Jundar's H13 after 0x03F2 the search found no state nearer the ending, though the walk into 11/A01/4 needed nothing. Such a probe step that leaves the plan's state as it was is banned where it was played.

**Choices across merged ways.** The planner merges ways that do the same under conditions it can't tell apart, keeping one way's choices. So a step carries its script's ways (`PilotStep.Ways`), and on each page `WantedChoices` follows the first way whose choices so far are the ones taken: Brenna's 0x03A3 goes by node 33's menu once 18 is set, and by node 2's before.

**Ticks.** A step's budget counts only the ticks the player has the controls, and the step may take four times it in all: the way from Gwernia to Erromon crosses five wanderers' battles, the first about 23,000 ticks.

**Camping.** Hit points come back only by camping, inns, spells, potions and a few conversations. While a living member is below 60% of their most, the pilot opens the World tab where the player stands in trek on ground that lets the party camp, and `Resting`, a menu policy over the others, chooses Camp: the game's own camp follows, its half heal and eight hours, or its ambush. Without it, each battle's replay started the party as hurt as the last left it, until one was lost before its first turn. The flood takes a few seconds the first time, while it loads every graph, and about 0.1 s after.

**Translation.** An action's place becomes the map object to use. A chunk (a world map region) becomes the nearest object that walking up to sets off nothing, used with `UseKind.Arrive`.

| Action kind | Use |
| --- | --- |
| Talk, Fight | Talk |
| Open, FindSecret, Disarm, PickLock | Press |
| Enter | Arrive |
| Play, Trip | as the object is (Auto) |

- **Choices.** Each choice becomes the page that offers it: the first menu or case of the script linking its node.
- **Effects.** They become what the step should leave so: the last word on each plain flag, member (while there's room) and item, less items a drop chance or a shop decides.
- **Code gates** are weighed against the party (`CodeCheck`). Gold, stats and locks are read, and one not met keeps the step from being played. A lock on the way to the place sets the route's `PickLocks`. Skill checks, traps, drop chances and the merge are left to the game, and reported if the step misses. Shops' purchases aren't made.
- **Events** (the opening) are played by running the host until the player has the controls.

**Trainers.** After a step that's done, a trainer is visited if all of these hold:

- they are within 40 of the player (60 of walking);
- their gates are open;
- they teach something a member's build wants that the party's experience and gold, less a reserve of 300, can buy.

The pilot walks up (`Arrive`), and `Levelling.Train` buys. A trainer isn't visited again until the party has more experience than when it last was.

**From a new game**, the member goal takes 4 steps and 48,150 ticks, about 8 s of wall time:

1. The way to the castle's 4/I01/6 plays Edur's scene, Alaron's, the Goblin Ambush and Oriana's cut-scenes, and ends in her hut, whose door is shut.
2. Talking to Oriana sets 50, and her training opens. The builds buy Alaron Strength twice and Air Shield.
3. The castle, with a battle at K08 lost and won outright.
4. 0x03A0 at 4/G01/6: Abrecan joins.

Brenna (0x03A3) and Rheda (0x039E) follow, filling the party in six steps.

**Left out:**

- Transports the story makes aren't modelled, beyond the off-foot rule.
- A wanted choice not offered isn't retried with the way's other choices.
- Choices in scripts that a way runs on into (`script.ended` triggers) aren't mapped to pages.
- Map sets entered are checked at a step's end.
- The trainer rule looks only where a step leaves the player, not along its route.
- While off-foot places are free in the relaxation, progress on foot doesn't lower the heuristic, so a search that fails takes about a minute.
- The rounds' flood keeps each place's first-reached way; ways with other literals aren't kept as alternatives.
- 0x0404 (Arturo's scene at the bridge) and 0x039F (Becan's) are still cut short at 50,000 runs, in their deep full-party branches, where each member present may be asked to leave. Their member checks that only spawn actors are detours (`ScriptWays.Detours`), not branches, and branches are walked shallowest first, so the ways that matter come before the cut.

## Goals and the party

A goal is a flag to set (`flag:<n>`), someone joining (`member`), or `ending`: the ending's credits rolling. The run's goal is at the bottom of its stack.

- **A party member.** While a slot is empty of a living member (`Party.HasEmptySlot()`), the member goal comes first, as long as the planner has a step towards it (a partial plan included). A new game has Alaron alone, so its first goal is a member, which leads into the story anyway. When the planner has no step towards a member, or 8 steps towards one fail in a row, the member goal rests until the next milestone wakes it, and the report says so.
- **Deaths.** A member who dies in battle leaves the party as it ends, as in the original. Their died flag keeps them out of the member goal, and their slot brings the goal back. From Before Rooughah with Sholeh gone, the next step is the companions' scene at I13 (0x0527). A lost battle is played again from its start, which undoes its deaths.

## The report

`RunReport` holds the entries, the milestones, every step and what was bought. Each entry has what was expected, what happened, the tick, where the player stood, and the save from just before its step. The game is kept before every step and written out once an entry needs it. The entries, by kind:

- `StepMissed`: a step used its object, but its effects didn't all happen.
- `NoRoute`, `Stuck`, `NotUsed`, `OutOfTicks`: the pilot's failures.
- `ChoiceMissed`: a wanted choice not offered, or a page that never came.
- `CodeGate`: a gate the party doesn't meet, or a roll that went against the plan.
- `Unreachable`: no step towards a goal, with the relaxed plan's next steps and why.
- `LostFirst`, `Stalemate`, `Death`: battles.
- `Warning`: the host's warnings and the battle pilot's problems.
- `Exception`.

**Milestones** are the start, a member joining or dying, a journal entry added (a flag naming one turning on), a cinematic played, a map set first entered, and the goal reached. Each writes a save in format 2, as the menu's save does, to `<saves>/playthrough/<nn>-<kind>-<what>.sav` (the data folder's saves, or `--saves`). The saves load in the runtime and the editor.

**The summary** gives the steps, the game's ticks and the wall time, the party's levels at each milestone, battles fought with the `LostFirst` and `Stalemate` counts, deaths, trainer visits, entries by kind, and the goal reached or why not. `--report <path>` writes `<path>.txt` and `<path>.json`.

## Where it stands

From a new game toward the ending, the run plays 24 steps, 898,437 ticks (about 250 game minutes), in 340 s of wall time:

- It fills the party in 6 steps (Abrecan, Brenna, Rheda), reaches Erromon and the goblin camp, crosses D14's toggle as Arturo joins, and reaches Jundar, its gatekeeper and the herb (0/H12/62, 222).
- It fights 79 battles, losing 18 first, with 2 stalemates (camp ambushes where nobody was hit for 3 minutes) and 4 deaths (Brenna, Rheda, Godric, Abrecan), and camps 35 times.
- The party is under-levelled for the north and east: most lost-first battles and every death are there. At the herb Alaron is level 9 and Arturo 12. Walking across the world costs most: the walk from C09 to P24/3 crossed about 30 battles and ran out of its 216,000 ticks.

**Where it stops.** The way from the herb to Jundar's 11/K01/2 (0x03FE) crosses the dryads' trigger at Overworld G12/5 (cut-scene 0x0531, radius 15, enabled by 5540). The scene plays each time the player walks in until its task is taken on (3647, then 1160), the default first choice doesn't take it, and the planner treats story triggers on a way as optional, so the step runs out of ticks. This looks intended in the original, a story gate, not a softlock.

**No game-side findings yet.** Every blocker so far was the bot's.

**Next for the bot:**

- Story triggers on a way, as gates or actions.
- Levelling: grinding goals, better builds, or less wandering.
- The camp ambushes' stalemates, probably the AI's pathing (`ArenaGrid.FindPath`).
