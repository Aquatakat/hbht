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
- a trapped container's trap let go off is an action of its own (`Spring`), which sets its exploded flag; it and opening the container each rule the other out;
- camping in a camp scene's chunk is an action per way through the scene's script (`Camp`), which the scene's object stands for ([camping](camping.md)): the scene's flag holds only while the camp looks for a scene (the check flag 4240), so the object has no other action;
- reading the help menu from the System tab is an action per way through its script (`Help`, no place);
- a new game's opening (`game.started`'s triggers) is the first step of any plan from one.

An action needs its object to be there (its gates, as `MapObjectPresence` reads them), its place reached (`Reaches`), and whatever its script's conditions need to go its way. Its effects, in order:

- flags set and cleared;
- members joining and leaving, with their flags;
- key items gained, with their held and region flags;
- after a battle won: the encounter flag, the rules' end flag, the bosses' flags of the enemy list's first four, and the party's loot.

A battle with the Shadow while the Horn of Kynon is held has the merge as a second outcome (the combat record's `shadow`). It's never won: without the Horn it ends in the Shadow vanishing, with nothing won and no flag set ([combat](combat.md)), so the model gives its battle no win (`QuestModel.NeverWon`) and its encounter's beaten flag (Overworld H08/12's 950) is no target.

**What code decides** is a `CodeGate`: skill checks, gold, stats, locks' and traps' levels, a trap going off, a camp not ambushed, a shop's price, drop chances, and winning. The planner takes each to hold, and lists it with the step for the pilot to meet. Flags the engine writes (the calendar, rain, terrain) can be either value, since the pilot can wait or walk for them. Derived flags follow from their formulas over what each input can be (`FlagModel`), with no correlation kept between inputs.

**A script's ways come from the real runner**, not a model of it. `ScriptWays` runs the script again and again with a probe (`IScriptProbe`, an optional parameter of `ScriptRunner`) answering each condition and taking each effect that changes the state:

- each condition the way so far doesn't settle is a branch, and so is each page of choices;
- the runner's own walk decides the rest (menus going back, cases, choices hidden once taken), so the ways can't drift from what plays;
- ways with the same effects are merged, keeping each branch's choices (`OtherChoices`), and the pilot takes whichever the page offers;
- a cut-scene's ways go on through its `script.ended` triggers and the cinematics they play.

From a new game on NA 1.0 that is 6,960 actions over the 429 scripts something starts, with 3,266 ways: 28 camps, 82 traps let go off and the help menu among them. A flag a way writes again with the value it last wrote isn't an effect again (`ScriptWays.Run.Write`): each pass through a backdrop's chapter wrote its flag again, so 0x036F's contents never looked seen and the explorer read its chapter 19 four hundred times, in 802 ways. An if node whose holding leads only through single-linked lines with no op changing the state, back to where its not holding leads, is a detour (`ScriptWays.Detours`): it goes the short way unasked, rather than being walked as a branch. Branches are walked shallowest first. Two scripts with 23 member checks each, `0x039F` (Becan's scene) and `0x0404` (Arturo's at the bridge), are still cut at 50,000 runs, in their deep full-party branches. Before detours, every combination of their eleven spawning checks came before the ways that mattered, and Arturo's ways (3460, 3461, joining) were missing. Every one of the 162 ways open after the opening, played from the same save on the real runner, does to the flags and the party what its action says.

**The static pass** relaxes every action from a state, with nothing ever undone (FF's relaxation, `Relaxation`). What never comes true in the relaxation can't come true in any order of steps. From a new game on NA 1.0:

- 1,967 of the 4,972 plain flags can be set;
- 466 of the 555 derived flags can be set;
- all 13 members can join;
- 417 of the 429 started scripts can run;
- the ending can roll, and the merge with the Shadow can happen.

Each plain flag that can't be set is given its reason:

| Reason | Flags | Of them read |
| --- | --- | --- |
| Nothing sets them (the 17 "Has Map" flags and the Skull Key's among them) | 2,927 | 138 |
| Only scripts nothing starts set them | 23 | 10 |
| Their setters need something that never comes true | 20 | 15 |
| Only the engine sets them (the members' died flags) | 11 | 11 |
| A container's trap going off, at a container the relaxation never gets to | 11 | 11 |
| No way through their script reaches the setter: done flags, the death scenes', the Campsite's (which only a camp with no scene plays) and four backdrops' whose objects are gated off on flag 0 | 8 | 0 |
| Their setters are map objects gated off on flag 0 | 4 | 4 |
| Only a battle that's never won sets them: the Shadow's encounter at Overworld H08/12 (950) | 1 | 1 |

Before stage 5's actions ([what the planner doesn't model](#what-the-planner-doesnt-model)) it was 1,881 plain and 458 derived, with every trap's flag and the camp scenes' out of reach.

**Planning.** `QuestPlanner.Plan(state, goal)` works in three stages:

1. It chains back from the goal through what makes it true, and through those actions' preconditions' setters, to the actions that matter.
2. It searches forward over only those: enforced hill-climbing on the relaxed plan's length, trying the helpful actions first, falling back to best-first where that stalls.
3. It drops every step the plan works without, so a plan doesn't wander.

A goal the relaxation can't reach comes back with why: what sets it, and the first thing each setter needs that never comes true.

**Places.** On its own, the planner counts every place as reachable (`Everywhere`), so its plans follow the story's flag gates alone, though most of the game's progression is geography. The run puts the world graph behind `IPlaceReach`, which turns the gates on a place's way into preconditions (see [the run](#the-run)). Without it, from a new game:

- the first member is three steps: the opening, script 0x0362, then the companions' scene at Overworld I13 (0x0527), where Abrecan joins;
- the ending is seven: the opening, 0x0362, Roog's scene (0x04F4), the tripwire at M06, and the last three scenes at Gwernia castle (0x04FC to 0x04FE), with the two battles won.

**What it doesn't model** ([what the planner doesn't model](#what-the-planner-doesnt-model) has why):

- deaths, the cheats, and the party's best stats, which 1.0 never writes;
- buying anything but key items;
- camp ambushes (a camp's step is played again from the save before one) and training;
- wanderers catching the party;
- encounters' random extras;
- the `item.gained` and `spell.trained` triggers;
- the cut-scene chain at a named place.

A tangible script object or tripwire goes off as the player walks past it. The planner treats it as optional, so the pilot routes around the ones it doesn't want, and one that turns the player back is got past first ([story triggers](#story-triggers)). A condition on a derived flag, read after the same script wrote one of its inputs, is read against the state the script started with.

## The world graph

`WorldGraph` (`WorldGraph.cs`, `WalkGraph.cs`, `RouteTypes.cs`) is where the player can walk, and how he gets to the place to stand to use a map object.

**Walk graphs.** Each map set has one, made from `Reach`'s exploration at the original's movement and pace (`Reach.Options.Edges`). A node is a 1×1×1 cell his feet passed over on the ground. An edge is a step he made from one cell to the next in a run, so it goes one way and is something the real `PlayerMotion` did: a ledge dropped off has a way down and none up unless some run climbed it. Each cell keeps the mean place of his feet in it (`Reach.Feet`), which is what a pilot steers for, since steering at cells' middles runs into walls' corners. A graph is made when its map set is first asked for and cached under the data folder's `cache/playthrough`, keyed by a fingerprint of the graph's format, the explorer's options and the plugins. The base plugin's file times are left out of it, so importing the same ROM again keeps the cache. A graph is written to a file of its own name and renamed over the cache's, and read sharing delete, so world graphs in one process making the same map set's at once (runs side by side, the tests' classes) don't collide; the last rename wins, and they're the same graph. From a cold cache, all 17 map sets take about 86 s (the Overworld's 1,007,448 cells are 8 MB of the 9.5 MB, before its toggles' ground). A warm read takes milliseconds.

**Teleporters** join the map sets. There are 894 outside the Battle arenas, and 96 carry a gate that stops the way: a visibility or enabled flag, or a lock its flag opens. A tangible teleporter's lock, trap and secret are skipped, as `MapObjectUse.Touch` skips them. Traps, and secret doors needing no skill, never block, but they are reported. 42 teleporters have no reached ground within their radius, and one (`0/C09/14`) has no arrival ground, so they aren't edges. A teleporter puts the player in the cell his feet are in once he's set down at its arrival, as trek's teleport sets him down (`Reach.SetDownAt`), each map set's made once (`WorldGraph.SetDowns`). The arrival point snapped alone can pick the twin of a cell a unit below a floor at a whole height, where runs ended against a wall and none set off: so the bridge's door back into Gwernia castle's courtyard (4/M05/0) once arrived in a pocket of five cells, and the library, the lab and their stairwell (4/K07) had no way out but the Erromon portal behind 2136. The gates are data (`RouteGate`), read against the `GameState` when a route is asked for, so one graph serves every moment of the game.

**Routes.** `Route(state, from, mapObject)` runs A* across the graphs and the teleporters, to the nearest ground within the object's use range by trek's rule:

- a container's reach plus 0.5;
- the radius of a teleporter, tripwire, script object or save point;
- about 1.5 for an npc or encounter, which wanders.

An npc or encounter is used where its body is set down as it spawns (`WorldGraph.TargetOf`), not where it's placed: near its own height and then from on high, as trek's wanderers are ([wanderers](wanderers.md#bodies-as-their-chunk-loads)), with each map set's collision streamed in once under a lock of its own. Placed, eight of them had no ground the player reached within 1.5, though their bodies stand on it: Batrin on the Port Saiid bridge (Overworld D14/6, 1.8 above it), the encounters at C14/10, Q25/2, Q16/3, R16/2 and H08/11, and Damek at Q16/4, 2.1 under the bog. The Dust Devil at O26/0 is placed over no floor at all and keeps its placement.

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
- where a route is refused only by gates the hour opens (each gate's flag closed now, and derived, and the planner, which takes the calendar's flags either way, says it can open), on the way or on the object itself, it waits where it stands a game hour at a time, up to a game day, and asks again: the Burial Chamber's door in Ugarit2 A05 is locked behind derived 5022, which night (5283, the time of day) opens, and the Talewok commoner at Overworld H13/18 is there only at night; the waiting isn't counted against the step's budget. An npc's or encounter's gates are read, and its body made, as its chunk loads, so when the wait ends with its chunk loaded and no body, trek's world is built again where he stands (`TrekLoop.Rebuild`), as after a backdrop, a sign or the help menu;
- where avoiding 1.5 about the waypoint it stuck short of cuts every way, as in a room's doorway, the route is asked for again avoiding 0.5 about it, and then avoiding nothing: in MiscInteriors A05 a run stepped across the wall beside the door into the portal room (6/A05/1), which the stick can't, and the 1.5 took in the cell the player stood in, so the same way came back each time; avoiding 0.5, the route takes the door;
- it gives up after 10 stalls or 40 replans.

**Keeping clear.** By default a route keeps out of the radius of every other enabled, tangible script object in its chunks (`RouteOptions.KeepClear`), so a step plays what it was asked to and not the story's cut-scenes on the way. Where it can't, it lets the triggers the plain route crosses be.

**Teleporters and use.** At the end of a leg, a tangible teleporter is walked into, and any other gets an A once trek's own weighing (`MapObjectUse.Reach`) would pick it. The step's object is used the same way:

- a container, sign, door or save point by A once A would reach it (`NotUsed` after 5 s in range);
- a tangible object by walking to its middle; a tripwire counts as used once trek's touch takes it (`MapObjectUse.Touches`: the grid cell under the player lists it, he's within its radius, it's active and enabled), since the edge of its radius may not be listed (Barrows B01/0). Its flag says nothing: 77 tripwires clear their own flag as they set it, as 1.0's do (`MapObjects.WriteUsedQuietly`, `0x80017acc`, then `MapObjects.WriteUsed`), so Gwernia castle's 4/E03/0 and 4/G07/1 flash 33 and 35 for an instant, which the ledger sees; a done rule on the flag called them used before the player had moved;
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

- Script objects that only trigger triangles name aren't kept clear of, nor are tripwires.

- Jundar's library stair (JundarInteriors A01, about 117.7, 7.8, 107.9) is a spiral whose steps a run climbed and the stick doesn't: steering from one cell's middle to the next, the player slides off the inside of the turn, every replan the same.
- Only running is used, never sneaking.
- A consequence starting more than a second after trek gets the controls back counts in the next step.
- A door's lock and trap popups are left to the menu policy.

## Battles

`BattlePilot` (`BattlePilot.cs`) plays the party as the original's combat AI plays the enemies. As a battle begins, it hands every party-side combatant to the AI (`Battle.PlayByAi`). Each member gets an AI record from their sheet, as Sholeh is given one, and chooses targets, spells and walks by the same styles and scores. As with Sholeh, the AI's casts take no reagent, and a thrown flask isn't taken from the inventory. The bot never flees: the AI's flight is the enemies' alone.

**No blasts.** The members' blast spells are taken from their AI's action slots (`CastsBlasts` gives them back). The AI aims a Fireball at the opponents' midst, whoever of its own side stands there, and a count that catches more of its own side wraps the score byte to the best of all ([combat-ai](combat-ai.md)). A member's Fireball took the whole party with it.

**Losing.** A battle is lost as the original loses one: every member down, or Alaron dead (`TurnOrder.Advance`, `Combat.Defeat`), with the game over's script following. A non-lethal battle ends `Spared` and isn't a loss. To rewind:

1. As trek cues a battle, on its last tick before `BeginBattle` (`IBattlePolicy.Cued`), the pilot notes the player's place and keeps a save.
2. On a loss, the host starts again on that save once the tick is over (`PlaythroughHost.Restart`), and cues the same setup.
3. The replay is won on the first turn anyone takes, through the game's own victory: on the party's by `Battle.ForceWin`; on an AI-played one, as it begins, by routing the enemies (`Battle.Rout`) and ending the turn, whose change wins it as the party's would (`BattlePilot.WinOnTheirTurn`).

So the flag, the gold, the experience, the post-battle windows and trek where the battle began each come once, and anyone who died in the lost battle is alive again. The replay's `BattleFought` is marked `LostFirst`, the report's sign that the party is under-levelled there. The enemies' turns count because some never let the party have one: the dryads' four, whom the male dryad's `0x03FA` sets on the party at Overworld H12/1 or G12/8, take the first three turns, and their Lightning, which strikes every member, kills a party at level 14 before its first. The user says these fights are technically winnable but meant as the story's deaths, and that winning them artificially is fine; waiting for the party's turn lost the replay again, ending the game in the cover jobs that met it ([dryads](#the-dryads-fight)). Only the Shadow's battle, which is never won, can still be lost again, and is noted.

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
| Rogue | Thief 3 or more, and above Warrior | the weapon, Thief, Dexterity, Stamina, then Mechanic | A rogue striking from behind, where Thief adds ×10 to the chance and ×2 to the damage; Dexterity is their defence. Mechanic to 5 disarms the traps that would destroy chests: a level 6 trap needs Intelligence × 4 + Mechanic × 10 over 96. Stealth is left: the AI doesn't sneak, and the scripts' Stealth check is a stub. |
| Fighter | anyone | the weapon, the shield, Stamina, Strength to its tiers, Warrior, Endurance | The front line: the cheapest chance to hit, the cheapest defence, the cheapest hit points, then the damage tiers and Warrior, which adds to chance, defence and protection at once. |

The first build that fits is chosen afresh each time from the member's own skills, so any recruit gets one. Each build is in tiers of lines such as "Wizard to 4". In the lowest tier with anything buyable there, the cheapest affordable thing is bought, and a tier whose wants can't yet be afforded is saved for. No one learns more than five spells, since the AI fights with only the first five. `Levelling.Train(host, trainer)` buys at a trainer what the builds want, within the gold less `GoldReserve`. Everything bought is in `Bought`, and anything a window refused in `Refused`.

**Gear.** Loot goes into the inventory, and a player puts on what's better. `Outfitting` (`Outfitting.cs`), a menu policy under `Levelling`, does so through the Party tab's presses: every 300 ticks with the controls in trek, the pilot asks it (`PilotOptions.Dress`), and once the inventory holds something that gains a member more than half a point, it opens the tab, and presses C-left to the member, the stick to the inventory, the row and A, until nothing better is left. The game's rules equip it and refuse what the member can't use, which isn't asked again. It weighs by the rules' terms (`Outfitting.Worth`):

- a melee weapon by its chance to hit against a middling foe (the member's Strength, Endurance, Level, Warrior and skill with it, plus its hit, less 50, from 5 to 95%) times its damage ((its dice + Strength's tier) × 3.5 + Level);
- body armor and gear by 4 × their protection, which comes off every blow, and the attributes they add, Dexterity twice;
- a shield by its defence × the member's shield skill, and 4 × its protection.

Weapons that fly, rings, scrolls and wands are left as they are. What it put on is in the report's purchases as `wore`.

An item is weighed on the member as they'd be wearing it: their stats and skills less what the item it replaces adds, and with what it adds itself; a stat the formula reads (a weapon's Strength and Endurance) isn't counted again in the flat term. Weighed on the member as they stood, Warfang's Warrior +2 raised the Chaos Sword's chance while Warfang was worn, so each looked better while the other was on, and Abrecan swapped them for ever, every step running out of ticks in the Party tab. As a guard, a member who puts the same item on a third time doesn't put back what it replaced (`MostPutOn`).

**Shops.** After a step that's done, as with trainers, a shopkeeper is visited if they're within 150 of the player (225 of walking: the shops are in the towns' interiors, where nothing wanders), their gates are open, a way through their conversation that opens the shop can be taken now, and their stock has something that gains the party more than half a point over the best of its kind it wears or carries (`Outfitting.Gain`) for one and a half times its price, within the gold less a reserve of 1,000. The pilot talks to them by that way, and `Outfitting`, with `Shopping` set, buys in the shop by its own presses (the stick to the stock, the row, A), the best gain first, one at a time, until nothing more is worth it; the dressing puts it on after. A shop isn't visited again until the party has more gold than when it last was. What was bought is in the report's purchases as `bought`. Before it, the party fought the whole game in what it joined with: at the Lighthouse Alaron had a Short Sword and Leather Armor, with Warfang, Partial Platemail and a Scorpion Shield in the inventory and 63,000 gold.

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

A run's wall time, in a Release build, goes in about three equal parts:

- **The places' rounds** (`WorldReach.Layered`, [the run](#the-run)): about 20 floods of the walk graphs a step, each some 60 ms with the Overworld's million cells.
- **The pilot's routes** (`WorldGraph.Route`'s A*): asked again at every replan, and up to three times a plan while keeping clear of triggers. A search that finds nothing goes over everything it can reach, about 250,000 cells, and is then run again to say why.
- **The host's ticks**, most of them battles. Half a battle's time is its recording of each tick for Start Early (`BattleDrawing`, `BattleRecorder.Sample`), which a headless run draws nothing from.

The planner's own search is a few percent. Loading the model takes about 4 s of each process's start. Everything below is kept so that a run plays the same steps it would without it:

- The world graph keeps where each map object is used from (`WorldGraph.Standing`, the cells `StandNear` finds) and each chunk's cells (`ChunkCells`). The floods ask both of every place in every round; found afresh each time, they were three quarters of a run.
- A reach keeps only its last round's flood. Each round looks up every place the planner asks of, so a place keeps the way the first round to get there found, and a place not got to keeps how many rounds were looked through. An earlier round is flooded again, as it flooded before, only for a place first asked of later. A flood is 8 MB of the Overworld. Keeping all of them put a run's peak at 2 GB, mostly garbage for the collector.
- A round whose every way costs what the last round's did reuses its flood, which would come out the same.
- The walk graphs mark the cells with gated steps and portals with a bit each, so a flood or a search doesn't look each cell up in a dictionary.
- The route search keeps its costs, parents and avoided cells in arrays per walk graph and thread, stamped with the search's number, rather than in dictionaries.

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
hbht playthrough run --goal flag:<n>|member|party:<entities>|ending [--save <save>] [--saves <dir>] [--report <path>] [--most-ticks <n>] [--most-minutes <n>]
```

It prints a line a step and a line a milestone, then the report. It exits 0 if the goal was reached, else 2.

**Runs side by side.** Several runs can play on threads of one process. They share what nothing changes once it's built:

- the plugins, whose lazily read records are read once under a lock (ADR 0036);
- the quest model's actions, ways and facts. Each run plays on a model of its own (`QuestModel.ForRun`, which the `Playthrough` makes), since the one thing a run changes is the model's place reach and its cache of what places need;
- the world graph: its walk graphs (each read once, with its lazily built indexes), and the standing, portal and chunk cells and `CanReach`'s closures, each made once under a lock or a concurrent dictionary. The route search's arrays and the floods' spare arrays are each thread's own.

Everything else is the run's own: its host and game, planner, story triggers, battle pilot, levelling, outfitting, resting, reach and report. `hbht playthrough run … --copies <n>`, left out of the usage, plays n copies of a run on threads, each with its saves in `copy-<i>` and its report at `<path>-<i>`. It prints the process's peak memory and its collections. Each copy plays as one alone does (`PlaythroughSideBySideTests`).

Runs on threads need the server collector, which the CLI's project sets (`ServerGarbageCollection`; the property ending in `Collector` does nothing). With the workstation collector's one heap, collections stop every run: 16 copies spent 116 s paused and took twice as long as 16 processes. With the server collector, 24 copies of a 41 s slice took 106 s in 7 GB, where 24 processes took 166 s in 21 GB. On the i9-14900K's 32 threads, threads stop gaining at about nine times one run's speed, by 24 of them, and processes at about six, by 16.

**Each step:**

1. It plans from the real state (`PlanState.From`), with the world graph behind the planner's places. The rest of the last plan is kept while it still holds from there.
2. It turns the plan's first action into the pilot's step and plays it.
3. It compares what the game did with what the step expected.

**Failure.** A step that fails (no route, stuck, not used, out of ticks, or an expectation missed) is reported. A step stuck the first time isn't banned: the game is saved and loaded where the player stands, as a player whose party is wedged does from the System tab, and the step may be tried again from the fresh start. Its action is banned in the state it was tried from, and for good after three failures. A step whose object the pilot never used still counts as done if what it was for came about, as when an encounter catches the party first. The run gives up on a goal after 8 failed steps in a row (`MostFailures`), or when the game's ticks or the wall time run out. It also stops when the planner has no step, saying what the relaxed plan's next steps are and what keeps each from being done.

**Places in plans.** One flood of the walk graphs from where the player stands (`WorldGraph.Flood`) finds, for every cell, the fewest closed gates on a way to it. A teleporter costs its closed gates, read against the game's flags, derived ones included:

- a lock the party's thief picks (level under 10, thief skill above it) is open;
- a gate with no flag, or whose literal nothing can make true, is a wall.

The places are found in rounds (`WorldReach.Layered`), so that no gate is opened from beyond it. The first round floods with only the gates open now. Each round's relaxation takes the places that round reached as reached, and says which literals come true; the next round's flood opens those gates too. It stops when a round adds nothing: 19 rounds and about 1.4 s from Rheda joining. A place's way is the one the first round to get there found. Without the rounds, the shortest way to Overworld A22 went through the door 11/A01/8, locked behind 320, which only the Cradawgh's scene at A22 sets; now it goes round by D14's toggle.

A place's gates (`WorldReach.Gates`) are the literals of the closed gates on its way, such as `flag 50 set` for the lock on Oriana's hut's door (6/E03/2). The relaxation and the backward chain take them as preconditions of the actions there (`QuestModel.Needs`), and the search checks them in each plan state. So a plan to a place behind a closed door first makes its flag true. From inside the hut, every place outside needs 50, and only Oriana's conversation (0x0394) sets it.

A place ways reach only through walls is walled, and nothing gets there. A place no way reaches at all is off foot. The relaxation takes it as reached, since the story moves the player itself (the ambush's battle ends in the hut), but the search never steps there. A gate open now is taken to stay open.

**With no plan**, the run plays the relaxed plan's first helpful step it can do where it stands, with an `Unreachable` entry saying so: from Jundar's H13 after 0x03F2 the search found no state nearer the ending, though the walk into 11/A01/4 needed nothing. Such a probe step that leaves the plan's state as it was is banned where it was played. With no probe either, the steps banned in the state the run stands in are tried once more before it gives up, since what failed may have failed for where the player stood.

**Choices across merged ways.** The planner merges ways that do the same under conditions it can't tell apart, keeping one way's choices. So a step carries its script's ways (`PilotStep.Ways`), and on each page `WantedChoices` follows the first way whose choices so far are the ones taken: Brenna's 0x03A3 goes by node 33's menu once 18 is set, and by node 2's before.

**Ticks.** A step's budget counts only the ticks the player has the controls, and the step may take four times it in all: the way from Gwernia to Erromon crosses five wanderers' battles, the first about 23,000 ticks.

**Camping.** Hit points come back only by camping, inns, spells, potions and a few conversations. While a living member is below 60% of their most, the pilot opens the World tab where the player stands in trek on ground that lets the party camp, and `Resting`, a menu policy over the others, chooses Camp: the game's own camp follows, its half heal and eight hours, or its ambush. Without it, each battle's replay started the party as hurt as the last left it, until one was lost before its first turn. The flood takes a few seconds the first time, while it loads every graph, and about 0.1 s after.

**Translation.** An action's place becomes the map object to use. A chunk (a world map region) becomes the nearest object that walking up to sets off nothing, used with `UseKind.Arrive`.

| Action kind | Use |
| --- | --- |
| Talk, Fight | Talk |
| Open, FindSecret, Disarm, PickLock, Spring | Press |
| Enter | Arrive |
| Camp | Camp: walk into the object's chunk, whatever its own gates, and camp from the World tab once there (`Resting.CampNow`) |
| Play, Trip | as the object is (Auto) |
| Help | no object: the System tab's Help by the menu's presses where the player stands (`Resting.ReadHelp`), and the help read through by the host's conversation policy |

- **Choices.** Each choice becomes the page that offers it: the first menu or case of the script linking its node.
- **Effects.** They become what the step should leave so: the last word on each plain flag, member (while there's room) and item, less items a drop chance or a shop decides.
- **Code gates** are weighed against the party (`CodeCheck`). Gold, stats and locks are read, and one not met keeps the step from being played. A lock on the way to the place sets the route's `PickLocks`. Skill checks, traps, drop chances and the merge are left to the game, and reported if the step misses. A trap the party always disarms never goes off, so a step to let it is a gate not met. A key item a shop sells (Map 7 at Talewok's, 3411, the only one a target hangs on) is bought by the way to the shop, the shop policy buying it first (`Outfitting.Buy`) and then what's better; its gate is the most the shop can ask, the item's price times its stock slots' multipliers and the keeper's Merchant times the step again (`CodeGates.MostPrice`).
- **Events** (the opening) are played by running the host until the player has the controls.

**Trainers.** After a step that's done, a trainer is visited if all of these hold:

- they are within 40 of the player (60 of walking);
- their gates are open;
- they teach something a member's build wants that the party's experience and gold, less a reserve of 300, can buy.

The pilot walks up (`Arrive`), and `Levelling.Train` buys. A trainer isn't visited again until the party has more experience than when it last was. What a trainer teaches is the game's own (`Trainer.Of`): one with no sheet and none it trains as is no trainer, as Fyrsil (0x140D, Overworld Q27/9, whose 0x04D4 has a way to training) isn't.

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

## Story triggers

`StoryTriggers` (`StoryTriggers.cs`) are the 239 tangible script objects in the base game whose script the model has ways for: each plays its cut-scene as the player walks within its radius, wherever he was going. Most play once and close their own gates, and the pilot keeps clear of the rest where it can. Some can't be kept clear of and play again every time: the dryads' scene at Overworld G12/5 (`0x0531`, radius 15, enabled by derived 5540) stands in the way out of their wood. With the Amaranth taken (222) and no task from them (229 and 3647 clear), it sends the player to their queen: 3646 opens the teleporter G12/3 about it, and the tripwire G12/1 where he arrives clears the scene's done flag 661, so walking out plays it again.

**Seen to turn back.** The pilot (`PilotOptions.Triggers`) notes, as each story trigger other than the step's own is cued, the ways its script would take as the game stands, before the script runs. A trigger cued a second time on one step with the same ways has turned the player back. The triggers learn those ways (`StoryTrigger.Blocking`), and the step fails `Blocked`, naming the trigger. A trigger that plays once and lets him by, or sends him somewhere that suits the step, isn't caught: only the same way twice is.

**Getting past.** The run then puts a `PassGoal` for the trigger ahead of its goals, until the trigger no longer turns the player back as the game stands: its gates are closed, or the ways it would take are none of those learned. `QuestPlanner` plans for it as each of its options in turn (`StoryTriggers.PassOptions`), and takes the shortest plan found:

- one of the trigger's gates closed, as a single fact (derived ones by their formulas);
- for each way not learned to turn back, the conditions it needs that don't hold yet, all of them (`FactsGoal`).

The trigger's own plays of a learned way are banned from these plans, since walking into it that way is what the goal is to get past. A partial plan isn't one: with no option found, the pass goal is given up on until the next milestone, and the run's goals go on.

From the herb, the dryads' scene sends the player to the queen twice, and the step fails `Blocked`. The plan to get past it is one step: the male dryad's conversation (G12/7, `0x03F9`) by its choice 10, which offends them (229) and ends in a battle. Walking out after it, the scene's way for 229 sets the Gorgon's task (3647) and sends him to the cave's mouth at I10 (3648 opens G12/2), outside the wood, and the step to Jundar's 11/K01/2 goes on from there. The queen's way, her Troubadour check for 231 and her scene `0x03FC` (233, which closes the trigger for good), is the other option.

**Which ways send the player elsewhere** is also found from the data (`StoryTrigger.Sends`): a way whose effects open a tangible teleporter whose radius takes in the trigger. Seven triggers have such ways, the dryads' three among them. It isn't taken as turning back, since being sent to the Gorgon's cave is the way out of the wood, and the planner doesn't know where the player stands.

**Opened by a step.** A step's way can open a story trigger about where it ends, which then plays at once as its consequence, by the host's default choices unless the step wants others. The queen's conversation (`0x03FB` at Overworld G12/16) by the way whose Troubadour check wins her trust (231) opens her scene at G12/4 (`0x03FC`, enabled by derived 5068), and by the default the scene closes without Pandara's amulet (234), which its way by choices 26, 62 and 83 gives. In a cover run, the triggers a step opens (`StoryTriggers.Opened`: none of whose plays can be done before the step, and some after it) are each played by their way that sets the most of the targets live before the step, if any does, and the pilot takes those choices where the step's own want nothing (`PilotStep.Consequences`). The story run keeps the default, so the trunk is as it was.

**Rolls tried again.** A Troubadour check isn't kept by its map object (`ScriptOps.SkillCheck`), so a step that missed because one rolled against it is played again, up to 8 times, before it's banned, and these tries don't count against the goal's failures in a row. A skill check that can't come out in the range a way wants at all, as a Troubadour check with no one who has the skill, keeps the step from being played (`CodeGates.CanRoll`, 400 rolls of the party's real check).

**Rolls the game keeps** are played again from the save before the step, as a player reloads: a trap that went off, which destroys its container so that its Opened and Used flags never come, and a skill check its map object remembers. The game starts again on that save, the player stands 17 ticks a try so that the world's random stream, which the weather draws on every frame, rolls anew, and the step is played again, up to 8 times (`Playthrough.LoadBefore`). A trap the party has no chance at keeps the step from being played (`CodeGates.DisarmChance`: the mechanic, the member with the most Mechanic who has the Stamina, rolls d100 under Intelligence × 4 + Mechanic × 10, and the step that leaves must reach the trap's level), so its container waits for a party that can. Before this, 59 of the targets a trunk tree gave up were a chest's Opened and Used flags, its trap gone off on the one try. The other way about is played again the same way: a step to let a trap go off that the party disarmed instead, and a camp meant to play its scene that was ambushed (the ambush's roll is on the world's stream too).

## Goals and the party

A goal is a flag to set (`flag:<n>`), someone joining (`member`), some members in the party at once and others out of it (`party:<entities>`, each a hex number such as `0x0248` or a record ID, one to have out after a `-`: `party:0x0242,-0x024C`), or `ending`: the ending's credits rolling. The run's goal is at the bottom of its stack; getting past a story trigger that turned the player back ([above](#story-triggers)) is at the top.

- **A party member.** While a slot is empty of a living member (`Party.HasEmptySlot()`), the member goal comes first, as long as the planner has a step towards it (a partial plan included). A new game has Alaron alone, so its first goal is a member, which leads into the story anyway. When the planner has no step towards a member, or 8 steps towards one fail in a row, the member goal rests until the next milestone wakes it, and the report says so.
- **Deaths.** A member who dies in battle leaves the party as it ends, as in the original. Their died flag keeps them out of the member goal, and their slot brings the goal back. From Before Rooughah with Sholeh gone, the next step is the companions' scene at I13 (0x0527). A lost battle is played again from its start, which undoes its deaths.
- **The cap.** The planner's search keeps to four: `PlanState.Apply` adds no one to a full party, and a joining way whose script checks for a slot (`CheckPartySize`, the `PartySizeIn` literal) can't be planned with none. So with the party full, a party goal (`PartyGoal`) is planned through a conversation that asks who leaves: from a new game with Abrecan, Brenna and Rheda in, Godric is one step, the companions' scene at Overworld H13 (0x0529) by a way that sends someone else away. The relaxation keeps no cap ([party jobs](#party-jobs)).

## The report

`RunReport` holds the entries, the milestones, every step and what was bought. Each entry has what was expected, what happened, the tick, where the player stood, and the save from just before its step. The game is kept before every step and written out once an entry needs it. The entries, by kind:

- `StepMissed`: a step used its object, but its effects didn't all happen.
- `NoRoute`, `Stuck`, `NotUsed`, `OutOfTicks`: the pilot's failures.
- `Blocked`: a story trigger turned the player back, and getting past it comes first.
- `ChoiceMissed`: a wanted choice not offered, or a page that never came.
- `CodeGate`: a gate the party doesn't meet, or a roll that went against the plan.
- `Unreachable`: no step towards a goal, with the relaxed plan's next steps and why.
- `LostFirst`, `DiedFirst`, `Stalemate`, `Death`: battles.
- `Warning`: the host's warnings and the battle pilot's problems.
- `Exception`.

**Milestones** are the start, a member joining or dying, a journal entry added (a flag naming one turning on), a cinematic played, a map set first entered, and the goal reached. Each writes a save in format 2, as the menu's save does, to `<saves>/playthrough/<nn>-<kind>-<what>.sav` (the data folder's saves, or `--saves`). The saves load in the runtime and the editor.

**The summary** gives the steps, the game's ticks and the wall time, the party's levels at each milestone, battles fought with the `LostFirst` and `Stalemate` counts, deaths, trainer visits, entries by kind, and the goal reached or why not. `--report <path>` writes `<path>.txt` and `<path>.json`.

## Flag coverage

`hbht playthrough cover` sets every flag the static pass can set, over a tree of saves, so that a flag no play sets is a finding rather than a guess. It's the run with a goal of many flags (`CoverGoal`), and a queue of jobs on disk that several processes work at once. The code is `Playthrough.Cover.cs`, `Coverage.cs`, `CoverQueue.cs`, `CoverWorker.cs`, `Trunk.cs`, `PartyNeeds.cs` and `PartyJobs.cs`, the CLI's `CoverReport`, and the ledger in the core ([flags](flags.md#the-ledger)). The roadmap's [playthrough bot](../roadmap.md#the-playthrough-bot) table has its stages; this is stage 0, the machinery, stage 1, the trunk the jobs start from, stage 3, the party jobs, stage 4, what's left uncovered, stage 5, what the planner didn't model, and stage 6, the parties kept small or without someone.

```
hbht playthrough cover init --root <dir> [--save <save>] [--targets all|<file>] [--job-minutes <n>] [--most-failures <n>]
                            [--trunk [<trunk dir>]] [--threads <n>]
hbht playthrough cover work --root <dir> [--most-minutes <n>] [--most-jobs <n>] [--threads <n>]
hbht playthrough cover retry --root <dir>
hbht playthrough cover party --root <dir> [--threads <n>]
hbht playthrough cover status --root <dir> [--all]
```

**Workers.** `work --threads <n>` plays up to n jobs at once on threads of one process, sharing one quest model and world graph ([runs side by side](#the-run)); about 20 suits a 32-thread machine. A worker that finds nothing pending waits while another is still playing, since that job may make branches, and they all stop once none is. A job that throws is left claimed and the others go on; it's played again when a later `work` finds its process gone. `work` exits 0 once no job is pending, and 3 when its minutes or jobs ran out first.

**Overnight**, `tools/cover.ps1 -Root <dir> [-Hours 10] [-Threads 20] [-Retries 2]` builds the CLI and starts `work` again whenever its process dies, until no job is left or the hours run out, appending every process's output to `<root>/work.log`. With 16 workers a process once died of an access violation inside `FlagModel.Possible`, which reads only shared arrays and its caller's own, as the runtime dispatched an exception thrown there; the same jobs played again cleanly. Nothing in the core is unsafe, so it's taken for the runtime or the machine, and the script is the answer to it.

**The targets.** By default, every flag, plain or derived, that the relaxation over every action from the start sets and isn't set at the start (`FactLayer` above 0), and every flag over what the engine writes, its own or derived from them, that the start hasn't set and the relaxation can make so ([what the planner doesn't model](#what-the-planner-doesnt-model)): from a new game on NA 1.0, 2,354 (1,967 plain, 367 derived of which 11 over the engine's, and 20 the engine's own). Stage 4's were the first kind alone, 2,229 (1,881 plain, 348 derived). A file names others, by number or record ID.

**What the story's order closes** isn't a target (`Closings`, from stage 7): the relaxation keeps no order, so an action that needs a flag set that nothing clears can be done, by its say, at a place the flag's setting shuts for good. The player stands at the flag's setter as it's set, so `Closings.Find` floods the world graph from each setter's place with every gate that wants the flag clear shut, and every gate wanting a plain flag's value that no action writes and the game doesn't hold shut, the rest open; an action needing the flag at a place those floods don't reach, where floods with every gate open do, is never done, and the static targets leave it out. It counts only flags plain, clear in the game, cleared by no action and set only at map objects that don't wander; only teleporters move the player in trek. From a new game it closes four actions, all in Shamsuk's Tower after he's defeated ([what's left uncovered](#whats-left-uncovered)), and one target, 1425: 2,353 targets. It runs with `init` (about 20 s, from a warm cache) and prints what it closed.

**The tree.** `init` writes the root (`cover.json`) and one pending job, `0`, from a new game or a copy of the save; with `--trunk`, a job for each trunk save given targets ([the trunk](#the-trunk), below). A job is played as a run whose goal is its targets less those any job has seen. Where a step loses targets not yet covered, the job records a **branch**: a job from the save before that step, over the targets it lost, named after its parent (`0.3`, `0.3.1`). A branch avoids the exact way that lost them, and every way its parents avoided (`CoverGoal.Avoid`, the job's `avoid`: `#<action> <description>`, the description checked against the action, since a script's ways that differ only in its conditions describe alike). Taken again from the same save, that way lost them again. No branch is made deeper than 6 (`CoverWorker.MostDepth`): a target lost again eight steps into each branch made a chain thirteen deep, playing on long after the rest of the tree was done; its targets are given up there, for a retry round to start elsewhere. A job the worker's own time cuts short with targets still live is **carried on** from a save of where it stopped (`0+1`). A job's own cap (`--job-minutes`, 120) gives its live targets up instead.

**The ledger** is every flag any job has seen set, with the job, the step, the tick and the save from before the step: the union of the jobs' results, read on demand (`CoverQueue.Ledger`), so no two processes write one file. A run attaches a `FlagLedger` to its host as it starts, and after each step puts the flags first seen down to that step, trainers and shops after it included. A target already set as the run begins (a new game's start sets a few before anything can watch) is covered at step 0.

**The root folder:**

| Path | What |
| --- | --- |
| `cover.json` | The targets, the root's save, the caps, and the trunk's folder if there is one |
| `trunk/` | The trunk: `trunk.json` (the manifest), `saves/steps/t<step>.sav`, its milestones' saves, its report, `assignment.json` (each target's job and its window, each save's step and distance), `retries.json` and `party.json` (what each target was tried from) |
| `jobs/pending`, `jobs/claimed`, `jobs/done` | A file a job; a claimed one is named `<id>@<machine>@<pid>@<process start>.json` |
| `jobs/locks` | A job's lock while it's claimed |
| `results/<id>.json` | The flags the job saw first, each target's end and why, the jobs it made, where its seconds went; `trunk.json` is the trunk's |
| `saves/<id>/` | The job's saves: the run's milestones, the save before each step that saw a flag first or lost one, `carried-on.sav` |
| `reports/<id>.txt`, `.json` | The job's run report |

**Claims.** The shallowest pending job is claimed first, and of those the one with the most targets, so the trunk's longest jobs start first. A claim makes the job's lock new (`FileMode.CreateNew`, which one maker wins), then moves the job into `claimed` under the claimer's name. Every claim first returns to pending the claims whose process is gone: on this machine, a process of that ID with that start time isn't running (another machine's are taken to be alive). Files are read sharing delete, since `File.Move` copies where a rename meets a reader's handle, which once left a job in two places. A finished job writes its result, adds the jobs it made, and moves to `done`; a process that dies before that leaves the job claimed, to be played again, its result and branches written over.

**Each step** of a cover run:

1. **The look.** A relaxation over every action from the real state, with the places' reach the run makes before every step ([places in plans](#the-run)), says which targets are still live. A covered target is one the ledger or another job saw. A target over the flags the engine writes, an engine flag itself or a derived one whose fact holds in the plan's state without the game setting it, waits; once nothing else is live, the run waits for those of the clock where the player stands ([what the planner doesn't model](#what-the-planner-doesnt-model)), and gives up the rest. A target live before the last step and not now is lost (below). Banned actions are in the relaxation: a ban is the bot's.
2. Getting past a story trigger comes first, as in any run.
3. **The nearest step that sets a target and loses none.** Every action that can be done now, sets a live target for certain and whose code gates the party meets, is sorted by its distance from the player as the crow flies in his map set (another map set's is 10,000 on). In that order the loss check weighs up to 60, until four lose nothing; of those, the shortest world graph route is played.
4. **Someone joins**, when no step nearby is safe and the party has a slot, if the member plan's first step loses nothing. The member goal doesn't come first, as it does in a story run: from a new game it played the whole Goblin Ambush and filled the party before the chest beside the start. Once the run must go further anyway, a fuller party fares better on the way.
5. **A plan to a target**, the live ones by their layer in the look's relaxation and then the distance of their first setter: up to three plans found, or twelve tried. The first plan whose first step loses nothing is played. A plan whose first step's code gate isn't met (a lock the party can't pick) is left until the run has moved on; a target with no plan twice, at different steps, is given up with the planner's why.
6. With nothing safe, the step that loses fewest, of the direct steps and the plans' first steps.
7. With nothing at all, the targets planned to are struck for that step. Once every live target is struck where the run stands, though the relaxation still sets them, the step just played has **stranded** the run: where it left the player, the world graph has no way on. The run ends, and the live targets go to a branch from the save before that step, which avoids it (`CoverGoal.Avoid`, the job's `avoid`, by the step's description, and every step its parents avoided). A stranded branch stranded again on its first step gives its targets up. With no step played, or no save before it, they're given up where they stand.

**The loss check** (`LossCheck.Lost`): the step's action applied to the plan's state, and the relaxation run again; the live targets it no longer sets, less those the step sets itself, are lost. A place-free relaxation takes about 15 ms from a new game, and a loss check 3 to 4 ms; a look with the places' reach about 10 ms, once the run's rounds of floods (seconds) have made it. Either way it's the relaxation's say, which can't see an order of steps fail, only a fact never come true: from a new game with the dryads' wood entered (218), telling the male dryad "Ardra sent me" (0x03F9 by choice 45, setting 226) loses their fight's flag 3663, and a chest by the start loses nothing. Offending them (choice 10, setting 229) loses nothing it can see, since their scene at G12/5 makes peace once the Gorgon's task is done. Opening a trapped container would lose its exploded flag by the relaxation's say, and letting its trap go off its opened and Used flags; the check leaves out the first (`Playthrough.Springs`), so the run opens the container as it did before the trap's flag was a target, and the look after the step sends the exploded flag to a branch from the save before, where the opened flag is covered and letting the trap go off loses nothing.

**The check after.** Each look compares the live targets with those before the step just played, so a step that lost more than predicted, as a story trigger on the way does, is caught as well: the lost targets go to a branch from the save before the step. A loss the place-free relaxation doesn't confirm is noted as **by the places alone**: where the step left the player walls them off. A lost target that comes back is live again (its branch is still played, and skips it if it's been covered). A step with no save before it (the player hadn't the controls) branches nowhere, and its targets say so. Every branch avoids the step that lost its targets (`avoid`), and what its parent avoided: taken again from the same save, it would only lose them again. A branch that loses the same targets on its own first step isn't branched again: whatever its first step is after, it loses them, as walking anywhere from the start plays Edur's scene.

**Shunned triggers.** A loss the check didn't foresee is often a story trigger the step walked into on its way: the pilot notes each one cued other than the step's own (`StepOutcome.Crossed`), and the branch shuns them (`CoverGoal.Shun`, the job's `shun`), so that no route of it may cross them (`PilotOptions.Shun`: a step with no way round fails `NoRoute`). Tripwires aren't noted: stage 7 tried noting each that wrote a flag as crossed too, for the tower's tripwire at Overworld P17/3 (438), which closes StageH's cut-scenes beside it (P17/8, `0x047A`, their radii of 25 eight apart). A tree with it covered none of StageH's, and its branches shunned tripwires as wide as Overworld I13/7's radius of 50, so it was taken out. Walking out of Gwernia castle plays the story on, which closes the conversations there (Trahern's 86, Sheridan's 29, Gabrion's 32): a branch that shuns the way out has them first, and gives up only what lies beyond it, which its parent went on to. A branch that loses its targets again on its first step is branched once more if that step crossed a trigger it didn't yet shun, up to the depth's cap.

**Failures.** A target is given up after `--most-failures` steps towards it fail (3). A step that should have set its target by its own effects and played out without the ledger seeing it counts as failed, and is banned where it was played, so a step that sets nothing isn't played again and again. A step towards someone joining that plays out with no one joining is banned where it was played too, and after two running the member goal rests until the next milestone: the companions' scenes at Overworld H13 and I13 (0x0529 by choice 94, 0x0527 by 110) each played out so, and a job went from one to the other 3,543 times. As a guard against any such loop, a run that covers, loses or gives up no target in 60 steps gives up the rest.

**Retry rounds.** In a trunk tree, once no job is pending, `hbht playthrough cover retry --root <dir>` gives each target no job saw set a job from another trunk save in its window: the latest it hasn't been tried from in odd rounds, where the party is furthest on, the nearest in even ones (`Trunk.Retry`), named `r<round>-t<step>`. Another start brings another party, place and story, which gets past most of what gave a target up where it was first tried: a branch with no plan from its save, a fight lost again, a loss on the way to it. What each target was tried from is kept in `trunk/retries.json`. `tools/cover.ps1` runs `-Retries` rounds (2) as the queue empties.

**The game ending** in a step (a battle lost again on its replay) sends the job's live targets to a branch from the save before the step, which avoids it, as a stranding does. Without it, Oriana's hut's job (`t003`, Alaron alone at level 1) lost a battle at MiscInteriors A09/1 twice and ended with 105 targets live.

### The trunk

The story run to the ending is the trunk: each target is given to the trunk save it's best started from, and each save with targets is a job, so that the jobs play at the story's own pace and level, all at once, rather than one sweep from a new game whose party does the whole world's optional content at level 2.

**The run.** `Trunk.Run` plays `EndingGoal` with `RunOptions.SaveEveryStep`: before every step the player has the controls for, the save the run keeps anyway is written to `saves/steps/t<step>.sav` (`Playthrough.StepSaves`). A `FlagLedger` on the host puts every flag first seen down to the step it was seen in, with that step's save (`StepSeen`; the opening's are step 0, with none). `trunk.json` lists each save's step, the step's action, the tick, the map set and the player's place, and the party's levels, then every flag seen. In the tree its flags are the trunk's own result, `results/trunk.json`, so the ledger, the jobs' open targets and `status` count them as covered. Neither the saves nor the ledger change the run: from a new game it's the same 87 steps to the same tick as the plain story run, in 135 s, its saves 13 MB.

**The looks.** From each save, and from the trunk's start, `Trunk.Look` makes the look a cover run makes before a step: `PlanState.From`, the places' reach in rounds (`WorldReach.Layered`) from where the save stands, and a relaxation over every action. A target is live from a save when the relaxation sets it and it isn't set there. The saves are looked at side by side on threads, each on a model of its own (`ForRun`) over the shared world graph: 88 games on 20 threads in 4 s, about 0.9 s each. With each live target goes its distance: from the player to the nearest action that sets it and that the relaxation gets to, by the cover run's measure (`Playthrough.Distance`: straight in his map set, 10,000 on for another). Every setter is weighed, not the relaxation's achiever, which is only the first action the relaxation came to and can be anywhere; for a derived target, the actions of its relaxed plan that write one of its inputs.

**The assignment** (`Trunk.Assign`, `Trunk.Choose`). A target's window is the saves it's live from. Of those, the nearest by distance, and of the saves within 50 of that or half as far again, whichever is more (`Slack`, `Ratio`), the latest: started from near where it is, with the party as far on in the story as that allows. The relaxation keeps nearly everything live from nearly everywhere (three in four of the windows are the whole trunk), so the distance decides more than the window does, and the slack keeps a save the trunk passes again later from losing to an earlier one a few units nearer. A target the trunk saw set goes to none; one live from the start but from no save goes to `t000`, a job from the start; one live from nowhere goes nowhere and is counted.

From a new game over the 2,229 targets: the trunk covered 637 itself, and 1,591 went to 62 of its 86 saves, none nowhere, and 1 to `t000`: 788, which a new game sets as the title begins it, before a ledger can watch, and which a job from the start counts as covered at once. A job has 1 to 152, the median 9; the first save's has 53 and the last's (before the castle's last scene) 62, most of them targets in map sets the trunk never stands in, which are as far from every save and go to the latest. The biggest are the Overworld's places where the trunk passes nearest the most: `t076` (152, before the Barrows' B02/0), `t012` (120, before the Jundar gate), `t082` (117, before the Shadow at B15), `t046` (111, at I13 before the companions' scene); and `t003` (112), Oriana's hut, the only save in MiscInteriors, whose rooms across the world are all nearer it in their map set's grid than any Overworld save is.

**The jobs** are named by their save's step (`t007`, `t087`), from `trunk/saves/steps/t007.sav`, at depth 0; their branches and carried-on jobs hang off them as off the root job (`t007.1`, `t007+1`). `status` shows each trunk job compactly: where it is, its targets covered of its own, and how many jobs hang off it, starred while any is open. `--trunk <dir>` copies a trunk folder already played, rather than playing it again.

**How it fares.** From a new game over the 2,229 targets, in a Release build: `init --trunk` takes about two and a half minutes (the trunk, 86 steps to the ending, about 145 s with its saves), and `tools/cover.ps1` with 20 workers in one process, two retry rounds and a party round works the tree in about 23 minutes. 2,111 targets are covered, 637 of them by the trunk itself, and 118 are given up: 333 jobs, 203 of them branches, at most 6 deep, and 3,223 steps; no game ends, and no process died. The first pass, 7 minutes, gave 207 up; the first retry round brought that to 130, the second and the party round to 118. The one sweep from a new game before the trunk covered 976 in 15 minutes, with 1,005 still live in jobs not played. What the 118 are is [what's left uncovered](#whats-left-uncovered). Stage 5's tree, over 2,354 targets, covers 2,230 ([what the planner doesn't model](#what-the-planner-doesnt-model)).

Stage 4's fixes, against the 2,047 covered and 183 given up before them, covered 77 of those 183 and gave up 12 others the runs had covered:

- a lost battle's replay is won on the first turn anyone takes, so the dryads' fight no longer ends the game ([the dryads' fight](#the-dryads-fight)): the targets of the jobs it ended, Overworld A22's sea-cave chests among them;
- rolls the game keeps are played again from the save before (a trap gone off destroyed its chest, whose two flags never came), traps the party can't disarm wait, and the Rogue build learns Mechanic ([rolls tried again](#story-triggers)): most of the 59 chests' flags that had gone with their traps;
- branches shun the story triggers that lost their parents' targets: Trahern's 86 in Gwernia castle, and its derived 5470 and 5516;
- the Shadow's battle is no longer modelled as won, so its encounter's flag (950) is no target.

**Left:**

- The distance is straight within a map set, which an interiors map set's rooms make poor: they're unconnected rooms laid out in one grid, so Oriana's hut's save takes MiscInteriors' targets across the world, and its job walks to them with Alaron alone at level 1. The walk from the save, through the teleporters, would weigh them truly.
- Weighing a target's distance by the relaxation's layer as well, so that a conversation a step away from one save goes there rather than to a later save where it waits on someone leaving, gains nothing: what keeps the castle's conversations ([what's left](#whats-left-uncovered)) is the party, not the save.
- Waiting for the date: a job waits for the time of day, the moon and the rain, but not months for the season, which only the trunk's own game moves on.
- A loss by the places alone may be the world graph's, not the game's, as the castle's was ([teleporters](#the-world-graph)): look at the cells where the step left the player before taking it as the game's.
- Branches aren't capped by depth or count, and a branch whose targets are covered elsewhere is still claimed, to finish at once.
- The ledger puts what a trainer or a shop visit sets down to the step before it.

### Party jobs

The relaxation keeps no cap on the party: anyone joins whenever their joining action can be done. The planner's search keeps to four ([goals and the party](#goals-and-the-party)). So a target that only someone outside a full party leads to looks live in every look, and the job given it has to find the party on the way, or doesn't. Party jobs start such targets with that party.

**Who leaves.** 1.0 has no menu to dismiss a member ([characters](characters.md)): members leave only by scripts. The joining conversations after Gwernia castle ask who leaves when the party is full, with a way per member who could go: the companions' scenes at Overworld I13 and H13 (Abrecan's 0x0527, Godric's 0x0529), Arturo's at the bridge (0x0404), Becan's (0x039F), Keelin's (0x0410), Dougal's (0x04EA), Niesen's (0x0472), and those in Terminor's C05, where members sent away wait ([wanderers](wanderers.md)): Arturo's 0x0383, Baird's 0x045E, Donovan's 0x045F, Keelin's 0x0382 and Rheda's 0x0518. The castle's own (Abrecan's 0x03A0, Brenna's 0x03A3, Godric's 0x03A1, Rheda's 0x039E) and Brenna's at Overworld M06 (0x03AD) check nothing, and add no one to a full party. Niesen's in Terminor (0x045A), Sholeh's (0x04AD, 0x04C4) and Farris's (0x0490) need a slot, which from a full party only a script that takes someone out with no one joining makes: Niesen's at 12/G13/3 (0x048F), Farris's at Overworld I08/5 (0x0496) and Dougal's at 6/K03/26 (0x04EF).

**What needs whom** (`PartyNeeds`). For each member, the relaxation is run again twice: with the member gone for good (out of the party, their joining actions left out), and kept for good (the actions whose last word on them is a leave left out). A target lost the first way and not the second needs them in the party. One lost both ways needs them to leave, and isn't theirs: Arturo's conversation on the bridge (0x0402 at Overworld D14/6) needs his joined flag clear. From a new game, 143 of the 2,229 targets need someone: Abrecan 26, Brenna 21, Becan 20, Godric 20, Rheda 19, Keelin 16, Arturo 14, Niesen 14, Baird 5, Donovan 4, Dougal 3, Farris 3 and Sholeh 1, some more than one. A trunk tree covers 124 of them without party jobs, since the story's own joins and the planner's who-leaves ways bring most members in; what's left of them is in [what's left uncovered](#whats-left-uncovered).

**Who's kept out** (`PartyNeeds.Out`). A target can need someone out of the party, and kept out, which the relaxation can't see for itself: it lets anyone leave whenever a leaving way can be done, in no order. Three tests, each against the relaxation from the save:

- A member in the party: with every action that takes them out left out, even for a moment (Arturo's scene on the bridge, `0x0404`, has ways that take him out and back in, and the relaxation takes all of an action's adds), the target is lost, and with them out of the party, their joined flag clear and their left flag set as a leave writes them, and their joins left out, it isn't. Rheda's talk in Port Saiid (`0x040F` at Port Saiid A07/10, 262 and 1001) wants derived 5245, which wants her joined flag 91 clear: from a save where she's in, she must leave.
- A member out of it: put in, with the whole party kept (every member's leaving actions left out), the target is lost, where the party kept as it is without them still sets it. From before Rheda joins, she's kept out for 262; from before Arturo joins, he is for Batrin's task (244, derived 5247 wants his joined flag 247 clear).
- Two out of it together, where neither alone loses the target: with Alaron alone, any two of Abrecan, Godric and Rheda set derived 5004, which closes Gwernia castle's conversations before Erromon (Sheridan's 29, Gabrion's 32, Godric's 39, Gechina's 41 and their gates). Of each target's pairs, the member in most is kept out, those whose joins the relaxation comes to last first, until none is left: two of the three are kept out and the third may join.

A member a target needs both in and out, as Godric's agreeing to join (73) needs him out until the conversation that sets it brings him in, is kept out, and the target's own setters may still take him (`PartyJobs.Joining`).

**Making the jobs** (`cover party`), once no job is pending in a trunk tree:

1. Every trunk save is looked at as `init --trunk` looks at them (`Trunk.Look` with the party targets), with, for each target no job saw set, the members it needs in the party from there (`TrunkLook.Needs`) and out of it (`TrunkLook.Out`), how far each member's nearest joining action is (`Joins`, 0 for one in the party), and how far each member in the party is from a way that takes them out (`Leaves`).
2. A target that needs only members in that the trunk itself brings in after that save (`PartyJobs.Brought`) has no start there: that's the story's own order. From before Rheda joins, most of what's uncovered needs her, as the story does, and a job's planner brings her in as the trunk did. Needing anyone else, the party is all it needs, three at most: Abrecan and Becan together for 416 at Overworld O17/1 (0x0474), though the trunk brings Abrecan in at the castle. Needing someone out, it's a party whatever the trunk does, since the trunk takes them in.
3. A target's starts (`PartyJobs.Starts`) are those saves, weighed by its distance, its members' to join and those to keep out's to leave, best first by the trunk's rule: the nearest, and of those within its slack the latest.
4. Each target takes the first of its best four whose party the planner reaches from the save, with the world graph's places as they stand there and the job's budget, never leaving a member out nor bringing in one kept out, by a way after which the relaxation without those steps still sets the target (`PartyJobs.PlanParty`). Each start is planned once, side by side. A target none of whose best four would do rides along with a start chosen for another further down its own list, if the plan there keeps it: 262 and 1001 are nearest the saves in Port Saiid with Rheda in, from which every way to take her out is after Shamsuk (458, which 5245 wants clear), and go with 5245 from the trunk's first save.
5. Each start with targets is a job at depth 0, `p<round>-t<step>-<members>-out-<members>` (`p1-t003-0242`, `p1-t003-out-023E_0248`), with its party (`CoverJob.Party`, `CoverJob.Out`). `trunk/party.json` keeps each target's starts tried, so a later round tries others.

**In the job** (`CoverGoal.Party`, `CoverGoal.Out`). Once no step nearby sets a target, the party comes next, before anyone else joins or a target is planned to (`PartyGoal`, with the members in and those out). A plan whose first step the loss check says loses targets is planned again without it, up to three plans, and the one that loses fewest is taken. With no plan, three steps towards it failing in a row, or two meant to bring a member in or take one out that did neither, the job stops trying and sweeps with the party it has. It never takes a step that leaves one of the party out (`PartyJobs.Leaving`), since none of its targets needs them to leave, nor one that brings in a member kept out, even for a moment, but for its targets' own setters (`PartyJobs.Joining`). A job that keeps someone out doesn't fill the party either: the loss check can't see what a join closes behind the places. From Oriana's hut with Abrecan and Godric kept out, the member goal took Brenna and Rheda in at Gwernia castle, whose way out only two of the three open (5004), and the questioning outside it (`0x039B` at Overworld M06/3, 63) that the castle's conversations wait on was walled off; without it, the job walks out of the hut (51), is questioned at M06/3, and has the castle's conversations in turn. Once nothing is live, a job that keeps members out lets them in for the rest of its run, and plays on the targets it gave up for no plan: Gabrion's second talk in the kitchen (1522, derived 5366) wants his first (32) and then the party gathered, and with Abrecan and Godric kept out it had no plan. Branches and carried-on jobs keep the party.

**Riding the story** (stage 7). A job with members to have in keeps only those the trunk doesn't bring in itself after its save (`PartyJobs.Starts`); the rest come with the story. Niesen's joining in Terminor is the story's way on, so every target past it looks as if it needs him, and he dies in Shamsuk's Tower; Abrecan leaves at Erromon and comes back at Overworld I13. Kept from the start, they stopped a job taking the story past them. Once its party is whole and no target has a plan, such a job rides the story on as the trunk does: the first step of a plan to the ending (`EndingGoal`), never one that takes a kept member out, its losses branched as any step's are. Its targets with no plan aren't given up while it rides, and each is planned to again only every 4 steps (`RideReplan`), since a search that finds nothing runs to its budget. It stops riding when there's no step towards the ending or 8 fail in a row (`StopRiding`), and gives up what had no plan twice. Becan joins only between meeting him at Erromon (131) and the return from the goblin camp (194, derived 5003 at MiscInteriors I07/4), and his targets want him at the Barrows' campfire (528 at Overworld O25/18) and after Lugash (5180): no plan reaches them from where he joins.

`status` lists the party jobs, each with the step its party was whole after, or why it wasn't. `tools/cover.ps1` runs `-PartyRounds` rounds (1) after the retry rounds.

**How it fares, stage 6.** Over a fresh trunk tree from a new game (2,354 targets), after its two retry rounds, a party round takes about 41 s. 43 of the 124 uncovered need a party from a save they're live from; 35 get a start in 12 jobs, 8 none. The kept-out jobs make their party at once (it's already so, or one step takes someone out) and cover 20 of their 28 targets: the castle's window in full from Oriana's hut (`p1-t003-out-023E_0248`, Abrecan and Godric kept out, 13 of 13; `p1-t003-out-0248`, Godric kept out, 3 of 4; `p1-t004-out-023E_0248`, 1 of 1) and Rheda's three in Port Saiid from the trunk's first save (`p1-t001-out-024C`, 3 of 3), and on the way the castle's others that need no party (18, 1520, 5362, 5367). Batrin's (`p1-t022-out-0240`, `p1-t028-out-0240`) keep Arturo out but never reach the npc at D14/6, which isn't the party's doing ([after stage 6](#after-stage-6)). The members-in jobs (Becan's, Keelin's) cover none, as below: their parties are made after long plans the story's triggers break, or not at all. The tree covers 2,258 targets, 28 more than stage 5's and none fewer.

**How it fared, stage 3.** Over the trunk tree above, after its two retry rounds, a party round takes about 35 s: the looks at the 86 trunk saves about 11 s, and the party plans from 15 starts the rest. 15 of the 118 uncovered need someone the trunk doesn't bring in, from a save they're live from; 9 get a start whose party the planner reaches keeping them, in 6 jobs, and the rest none. The party is made in 2 of the 6 (Becan's after six steps from Oriana's hut, Keelin's at once from `t028`), and none of the 9 targets is covered.

The cap isn't what keeps these targets: the planner's search already plans who leaves, and what the party jobs meet is what the other jobs met.

- Becan's targets (528, 553, 5156, 5172, 5180) are lost walking into the story's tripwires at Overworld I13/0 and P17/3 on the way, with Becan in the party or not.
- 416 and 5141 need Abrecan and Becan together. From the castle the plan to them is four steps, but Erromon's 0x03C3 on the way brings Brenna in as well, which fills the party, and the plan from there gives up.
- Godric's 73 and 5431 are lost to Erromon's story. 73 is set by his conversation in the castle (0x03A1 at Gwernia castle O07/4), which sets it even with the party full, so it needs the conversation in time, not Godric: Erromon's story (0x03C3, 0x03CA) closes it, and the plan to Godric goes through Erromon. Stage 6 covers 73 with Godric kept out but for that conversation.
- 5410 strands the run after Becan's scene, and 199 after 0x03E4 at Overworld H10/3.

Others that need someone need only members the trunk brings in (Niesen's 422, 423 and 5134, Farris's 565, Abrecan's 5409), and are given up for the story's order or a plan the search gave up on.

**Left:**

- What needs whom is the relaxation's say, which can't see an order: a joining conversation that also sets flags counts its flags as needing the member (73), and the members-in jobs it makes ask for more than the target needs (Becan's campfires from the trunk's first saves with Godric and Niesen too). Who's kept out is a sufficient party, not the only one: of a pair, the one the relaxation brings in last is kept out.
- The members-in targets, 14 (Becan's with Abrecan or Godric, his campfires, Keelin's, Farris's staff), are where the party is still the gap: Becan joins only at Oriana's hut (`0x039F`), the targets want him with the story far on (at Ugarit's gate, after Lugash, after Shamsuk), and the job carries him there through the story's own joins and tripwires, which fill the party or close the targets (the Jundar Gate's P24/3, Talewok's I13/0 and I13/5). A start from a late save with a way to bring him in there would serve them better than one from the castle.
- A party plan is long when the story must move first (Becan and Niesen from before the castle's companions join, 37 steps), and the story's triggers on the way change the party it planned with.
- Who's kept out is kept out until nothing is live, not until the targets that need it are done: a job whose last targets want the party gathered sweeps everything else first.
- The castle's targets that want the party gathered after a conversation with it small and need no one kept out themselves (Sheridan's goodbye in the barracks, 82 and 1523) get no party job, and are covered only as a job with others passes them.
- Niesen's join in Terminor, Sholeh's and Farris's need a slot, which only a death or the story's own leaving makes.

### The dryads' fight

The male dryad's conversation (`0x03FA`, Overworld H12/1, and G12/8 once he's moved) can end in a battle with four Female Dryads. They take the first three turns, and each casts Lightning, which strikes every member: a party at level 14 is dead before its first turn. The user says these fights are technically winnable but are the story's deaths, not meant to be won, and that winning them artificially is fine. So the battle pilot wins a lost battle's replay on the first turn anyone takes, theirs included ([battles](#battles)); waiting for the party's turn lost the replay again and ended the game, in six jobs of a trunk tree and more of its branches.

The trunk itself gets past the dryads' scene at G12/5 this way: the planner's shortest way past is the male dryad's fight, won on its replay ([story triggers](#story-triggers)). A player who isn't meant to win it gets past by the queen's way instead (her Troubadour check for 231 and her scene `0x03FC`), which the relaxation keeps open, so nothing after the dryads needs the artificial win. Only the fight's own flags do: 3654 (H12/1's) and 3655 (G12/8's), the battles' flags, which the relaxation with every action but those two wins no longer sets. They're covered, and counted apart: in play they follow a death the story means to happen.

### What's left uncovered

Each of the 118 targets the tree above gives up is in one of three classes. The evidence is the run's own: each target's last reason, with the relaxed plan's next steps and what keeps each from being done (the cover run says them with every target it has no plan to), the relaxation run again from the save of the job that gave it up without the steps that job avoids, and the conditions the target's setters rest on, through derived flags.

**(b) The relaxation's overestimate, 6.** It has no geometry and keeps no order, so it sets these, which no play can:

- The Barrows' five dragon flags, 5509 to 5513: each is a dragon's cut-scene done (`0x18F6`–`0x18FC`, Barrows D01/7–11) with its tripwire not walked into (D01/1–5, "animates"). Each script object, radius 2, stands about 1 from its tripwire's middle, radius 3, so its whole radius lies inside the tripwire's, which goes off first. Nothing reads them.
- Edur's npc talked to (1349) needs his cut-scene's flag 45 clear (Overworld I08/19), but every way from where the opening leaves the player to him passes within the cut-scene's radius (I08/4, 14), which sets it.

Before stage 4's fixes, the Shadow's encounter's flag (950) was one too: its battle is never won ([the model](#the-planner)).

**(c) Game findings, 5.** Gwernia castle's farewells: Brenna's (`0x04FF`, A01/11, 600), Arturo's (`0x0501`, A01/13, 602), Rheda's (`0x0502`, A01/12, 603), Keelin's (`0x0505`, A01/15, 606) and the throne's "Farewell." (`0x0508`, A01/14, 609) are enabled only once 599 is set (their gates 5202–5210). Only the coronation's scene sets 599 (`0x04FE`, A01/10), on the same way that rolls the ending's credits, and the credits' end goes back to the title in 1.0 ([ui](ui.md#coming-back-to-it)). So they never play: content cut from 1.0's ending, it seems. The user confirmed 1.0 never reaches them, and the port leaves them unplayed as 1.0 does (Nov 12). The dryads' battles' own flags, 3654 and 3655, are covered but only by an artificial win ([the dryads' fight](#the-dryads-fight)).

**(a) The bot's gaps, 107**, as stage 4 left them ([after stage 6](#after-stage-6), below, for what's left now):

| Gap | Targets | Examples |
| --- | --- | --- |
| A smaller party at the time: at most one of Abrecan, Godric and Rheda joined (derived 5004 clear), after Abrecan's scene at Overworld M06/3 (63) and before the goblin camp (122) | 23 | Sheridan's 29, Gabrion's 32, Godric's 39 and 73, Gechina's 41 in Gwernia castle, and their gates 5366–5431. The trunk takes all three at the castle before it leaves, and a job's member goal fills the party first; the relaxation keeps them live from later saves only behind someone leaving at Erromon (`0x03C3`), which closes them |
| Other windows the trunk passes without stopping | 10 | 18, 1520 and 5362 after M06/3 and before the goblin camp; Zurene's 948 and Gotzone's 1339 at Overworld I08, and 1507 at the castle; Erromon's 1310 and 1311 |
| Someone kept out of the party | 9 | Rheda's talk in Port Saiid (262, 1001, 5245) needs her never taken at the castle (91 clear); Batrin's at the bridge (244, 289, 1007, 1008, 3462, 5246) needs Arturo out. A party job is only of members in |
| Members the trunk doesn't bring together | 15 | Becan with Abrecan (416, 5141, 5409), Becan's campfires (528, 553, 5156, 5172, 5180, 5410), Keelin's (486, 557, 558, 5162, 5185), Farris's staff (565) ([party jobs](#party-jobs)) |
| A consequence's choices left to the host's default | 4 | Pandara's amulet (234) is lost when the queen's conversation (`0x03FB`) goes the way that enables her scene (`0x03FC`, G12/4): the scene then plays as its consequence, by the host's default choices rather than the way that gives the amulet (26, 62, 83), and closes; then the riddle that needs it (479, 4239, 5158) |
| The story's order at Overworld P17 and after Shamsuk | 10 | StageH's cut-scenes (422, 423, 424, 5134, 5135), which the tripwire P17/3 closes; EhudUnder's 1425, 2287, 2288 once Shamsuk is dead |
| Traps, locks, gold and purchases beyond the party | 7 | the chest at EhudUnder K01/12 (a trap of 6 and a lock of 7), MiscInteriors K05/51's trap of 6, rolled against on every try (2129, 2130, 5381), a way that wants 9 gold or less (3143, 3150), the shop's 0x010A (3411) |
| The pilot stuck, or a step that strands the run | 14 | Jundar's backdrops (346, 349, 350) stall at JundarInteriors A01 (117.8, 6.9, 106.8); MiscInteriors A05's tripwire and chest (2132, 2180, 1690, 5273); EhudUnder E05's chests (2297, 2298, 3219, 3220); Arturo's bridge scene `0x0404` stranding (4271, 4282) |
| Encounters and npcs with no ground near | 8 | Overworld C14/10, Q25/2, O26/0, P26/2, Q16/3 and Q16/4, F23/9, E23/6: the world graph refuses a wanderer whose placement has no reached ground ([the pilot](#the-pilot)) |
| Waiting for the hour | 2 | H13/18 and F23/3, enabled by night (5283) |
| Other conversations a branch had no plan to | 5 | 3151, 3193, 3209, the Chaos Mauler (1221), Lugash (1011) |

So of the 2,229 targets the static pass calls reachable, play reaches 2,111, 6 are its overestimates, 5 are 1.0's cut content, and 107 are the bot's. The biggest gap is the party: 47 need a party the trunk and the jobs don't make, smaller, different or kept apart.

#### After stage 6

A fresh trunk tree over stage 5's 2,354 targets (`tools/cover.ps1 -Threads 20 -Retries 2 -PartyRounds 1`, about 26 minutes) leaves 96: the 6 overestimates and 5 game findings above, and 85 of the bot's.

| Gap | Targets | Examples |
| --- | --- | --- |
| Members the story doesn't bring together | 14 | Becan with Abrecan (416, 5409), Becan's campfires (528, 553, 5180, 5410), Keelin's (486, 557, 558, 5162, 5185), Farris's staff (565), Godric and Becan's camp after Shamsuk (468, 5150) ([party jobs](#party-jobs)) |
| Places the world graph doesn't reach on foot | 14 | Batrin's task at the bridge (244, 289, 1007, 1008, 3462, 5246): the npc at Overworld D14/6 (`0x0402`) has no ground the player reaches within 1.5 (from every trunk save about the bridge, before Arturo joins and after); the encounters and npcs at C14/10, Q25/2, O26/0, P26/2, Q16/3, Q16/4 and R16/2; Amann on his ship (987) |
| Traps, locks and gold beyond the party | 13 | chests whose traps went off on every try, whose branches strand at Overworld A22/0 (2126, 2127, 2192, 2193, 2198, 2199, 5381); EhudUnder K03/10's lock of 7 (2287, 2288); E05/24's trap of 4, which the party always disarms (2292); E13/14's trap with no plan (2034); ways that want 9 gold or less (3143, 3150) |
| The pilot stuck, or a step that strands the run | 12 | Jundar's backdrops (346, 349, 350); MiscInteriors A05 (2132, 2180, 1690, 5273, 5542); Arturo's bridge scene `0x0404` (4271, 4282); Lugash (1011); M06/8 (1513) |
| Shamsuk's Tower, behind a way the planner gives up on | 10 | 1425, 2285, 2286, 2297, 2298, 3218–3221, 3224: each plan runs through `0x0494`'s battle at Overworld I08/13 |
| Other conversations a branch had no plan to | 6 | 3151, 3193, 3209, 3516, 3728, the Chaos Mauler (1221) |
| The story's order at Overworld P17 | 5 | StageH's cut-scenes (422, 423, 424, 5134, 5135), which the tripwire P17/3 closes |
| Other windows the trunk passes without stopping | 5 | Zurene's 948 and Gotzone's 1339 at Overworld I08, 1507 at the castle, Erromon's 1310 and 1311 |
| Gwernia castle's window, what's left | 4 | Sheridan's goodbye in the barracks (82, 1523), which needs no one kept out and gets no party job; Brenna outside the castle (1515), whose job strands at M06/9; Godric agreed but out of the party (5431), which wants him to leave again later with 5004 clear |
| The seasons | 2 | 2311 and 2312, seasons 2 to 5, which no job waits months for |

The kept-out parties and the consequence's choices cover 28 of stage 5's 124: the castle's window (18, 29, 32, 39, 41, 73, 83, 1520, 1521, 1522, 1527, 3124, 3125, 3130, 3131, 5362, 5366, 5367, 5371, 5391, 5392), Rheda's talk in Port Saiid (262, 1001, 5245), and Pandara's amulet and the riddle that wants it (234, 479, 4239, 5158). None that stage 5's tree covered is lost. Keelin's 5162 is covered in some runs and not others: its job from `t028` runs out of ticks on the way to the Lighthouse (`0x04F1` at 3/A05/4).

### What the planner doesn't model

Stage 5. The 2,229 targets above are only the flags the model's actions set. Every other flag was classified by what can set it, by a script over the plugins and stage 4's tree (`census.cs` in the stage's scratchpad, `C:\Users\aquat\AppData\Local\Temp\claude\4b1e3b4f7846419b\bdc87af7-6f2d-44c1-aadd-59996ef153eb\scratchpad\census\`, run as `dotnet run census.cs -- <stage 4 tree> [<stage 5 tree>]`). The 5,627 flags are 5,556 records and the 71 journal triggers the importer leaves out ([flags](flags.md#the-records)). Of the 3,327 records that weren't targets:

| Class | Flags | Targets now | Stage 4's tree saw set | Stage 5's tree saw set |
| --- | --- | --- | --- | --- |
| **(a) Set by nothing** | | | | |
| Unused | 2,768 | | | |
| Only read (3717, the random skill checks, among them: no code in 1.0 or the debug build writes it) | 82 | | | |
| Only cleared (containers' Used flags) | 9 | | | |
| A key item's held flag that nothing gives (the "Has Map" flags) | 17 | | | |
| Only scripts nothing starts | 23 | | | |
| Only map objects gated off on flag 0 (four backdrops at EhudUnder G09 among them) | 8 | | | |
| **(b) Set only by the original's code** | | | | |
| A trap going off | 87 | 76 | 40 | 70 |
| Camping: the eight scenes' flags, their scripts' done flags, the check flag 4240 and the Campsite's 4285 | 18 | 17 | 11 | 16 |
| The help menu's done flag (3759) | 1 | 1 | 0 | 1 |
| The clock: the time of day, the moon and the rain, and what's derived from them | 35 | 17 | 26 | 26 |
| The clock: the date and the season | 15 | 9 | 13 | 13 |
| The terrain walked on, and the journal's notice (3617) | 5 | 5 | 5 | 5 |
| The triggers on a spell trained and a potion gained (3611, 3612) | 2 | | 2 | 2 |
| A member's death, and the death scenes' done flags | 14 | | 10 | 10 |
| The cheats, and the two containers only a cheat's flag shows | 6 | | | |
| The party's best stats, which 1.0 never writes, and what's derived from them | 109 | | | |
| **(c) Set by modelled ways the relaxation can't reach** | | | | |
| Setters that need a flag nothing sets (64, 52, 1536, 4297, 402, 3677), one never true (5353, 5130), or themselves (4280) | 15 | | | |
| Derived and never true: over flags nothing sets (the A05 goblins' leaving, 1694–1697; Pandara's reward), or a formula no inputs meet (5353 wants a fourth bit of three) | 19 | | | |
| The Shadow's encounter (950), whose battle is never won | 1 | | | |
| **(d) Derived and true at a new game** | 91 | | 5 | 5 |
| Flags 0 and 1 | 2 | | | |

**(d) comes along for free.** Every derived flag the relaxation can set from a new game is a target already; those that aren't hold at a new game, and the ledger sees one only as it turns on again. Of stage 4's 348 derived targets, its tree saw 320. 14 of the 28 it didn't have every input their formula wants set seen set somewhere in the tree, but not in one game with the inputs it wants clear still clear: they're "bits =" formulas over a party at once (Becan with Abrecan, 5141, 5409, 5410; Becan's and Keelin's campfires, 5156, 5172, 5180, 5162, 5185), Rheda never taken (5245), and the Barrows' dragon flags (5509–5513), each its cut-scene done with its tripwire not yet walked into, which no play makes ([what's left](#whats-left-uncovered)).

**Modelled** (`PlaythroughEngineActionsTests`):

- **Traps going off** (`Spring`): try the trap and let it go off, which destroys the container with what it held and sets its exploded flag (`Container.Disarm` failing, [map objects](map-objects.md#containers)). The host's popup policy takes the trap's menu's first choice, Disarm; a member with no Mechanic always fails. A trap the party disarms every time is a gate not met, and one it disarmed against the plan is played again from the save before. The trap's damage leaves each member at least 1 Endurance (`Containers.TrapDamage`), so it kills no one. 11 trapped containers' flags stay out of reach, where the relaxation never gets to the container.
- **Camping** (`Camp`): the camp scenes ([camping](camping.md)) are each a script object in a chunk, enabled by a derived flag that's "bits =" over the check flag 4240 and the scene's own conditions; the action needs the conditions, sets 4240, plays the script's way and clears 4240, so the relaxation sees the scene's flag. The pilot walks into the chunk (the object's own gates unasked, `RouteOptions.IgnoreOwnGates`) and camps there from the World tab. An ambush, while two of Abrecan, Godric and Rheda have joined (5004), is a roll played again from the save before. The Campsite's scene, which plays when no scene holds, comes of the bot's own camps when it's hurt ([camping](#the-run)).
- **The help menu** (`Help`): the System tab's Help by the menu's presses, wherever the player stands in trek, which plays script `0x054B` and sets its done flag ([pause menu](pause-menu.md#the-system-tab)).
- **Buying a key item** at a shop, which stage 4 left out: Map 7 at Talewok's (3411) is the one target only a purchase sets ([translation](#the-run)).
- **Waiting for the clock.** Given the game, the targets take in the engine's own flags and the derived flags over them that a new game hasn't set (`CoverGoal.StaticTargets`; the planner takes the engine's flags as either value, so these hold in its state from the start): the time of day, the moon's phase, the rain and snow, the date, the season, the terrain, the journal's notice, and night (5283), the Burial Chamber's door (5022) and the Troll west of Darkling Bog (5255) among the derived. Once nothing else is live, a cover run waits for them where the player stands, an hour at a time (`WaitForTheClock`), as long as the longest of those still waiting can take (`FlagModel.Horizon`): a day for the time of day, four weeks, the moon's cycle, for the moon and the rain. It doesn't wait for the date or the season (the trunk's own game, some 70 days, sees most of them), the terrain or the notice. Trek brings the network up to date every tick nothing fades, so the ledger sees a derived flag as soon as it holds. The pilot waits for an object only the hour opens ([the pilot](#the-pilot)).

**Left out:**

- **Deaths.** A member's died flag, Sholeh's (622) and the death scenes' done flags (630–632) come of a member or the party dying, which the bot undoes as a player reloads ([battles](#battles)); covering them means losing someone on purpose, after which every later target is played with a party short. The story's own deaths (Niesen's) set theirs, and stage 4's tree saw 10.
- **The cheats** (3673, 3675, 3693, 3694, and the Hockey Stick's and the Lightreaver's containers, 3674 and 3676, which only they show): 1.0 offers them in ordinary play, typed as Alaron's name in Options' Rename, but the port hasn't ported them ([hardcoded content](../hardcoded-content.md#cheats)), and modelling them waits on the roadmap's cheat codes row; once the name entry takes them, an action per cheat would be one more like the help menu's.
- **The party's best stats** (2325–2369, the derived flags over them, and the three EhudUnder G03 objects they gate): 1.0 never writes them ([flags](flags.md#what-the-engine-writes)).
- **3611 and 3612**, the first spell trained and the first potion gained: the trainer visits and the loot set them anyway, and stage 4's tree saw both.
- **3717**, the random skill checks: nothing in 1.0's or the debug build's code writes it (every literal flag passed to `FlagNetwork.Set`, `0x8001fda8`, is another; its one read is `Dialogue.Condition`'s case 0x19, `0x8004f680`), so only a mod or a save edit sets it.
- **(a) and (c)** are the data's: nothing sets them, or what sets them never holds.

**The run.** A fresh trunk tree from a new game over the 2,354 targets (`tools/cover.ps1 -Threads 20 -Retries 2 -PartyRounds 1`), in about 25 minutes: the trunk, the same 86 steps in 2.2 minutes, covered 672 itself; the first pass gave 221 up, the retry rounds 148, and the party round left 124. **2,230 are covered**, in 399 jobs (267 branches, at most 6 deep); no process died. The tree saw 2,263 flags set in all, 33 of them no target.

- **Stage 4's 2,229: 2,115 covered**, against 2,111. New: the Talewok commoner who's out only at night (985, the pilot's wait), Map 7 bought (3411), three Chaos Isle encounters (1500, 1501, 3756), a chest in Elisheva's Tomb (2129, 2130) and three of Becan's flags with Abrecan (5141, 5156, 5172). Gone: three chests, in Elisheva's Tomb (MiscInteriors K05/49, 2126 and 2127) and a Jundar tomb (2192, 2193, 2198, 2199), whose traps went off on each of the eight tries of every job that tried them, and whose branches stranded at Overworld A22/0; stage 4's tree had a party that disarmed them. The rest of the 118 stage 4 gave up are as [what's left uncovered](#whats-left-uncovered) has them.
- **The 125 new: 115 covered.** 70 of the 76 traps; 15 of the 17 camping flags, the check flag 4240 among them; the help menu; and 29 of the 31 over the engine's, most of them by the trunk's own game. Left: six traps (the chest in the canyon near Port Saiid, E13/14's 2034, with no plan; four in Shamsuk's Tower, 2286, 3218, 3221 and 3224, behind a way to the tower the planner gives up on; and E05/24's level 4, 2292, which the party there always disarms); Godric and Becan's camp after Shamsuk (468 and 5150), lost walking into Overworld I13/5, the story's order with Becan as at [party jobs](#party-jobs); and the seasons' bits 1 and 2 (2311, 2312), seasons 2 to 5, which the trunk's game, some 70 days from week 6 of the first season, doesn't reach and no job waits months for.

## Where it stands

From a new game the run reaches the ending: the Coronation of Alaron plays and the ending's credits roll. It plays 86 steps, 3,915,237 ticks (about 1,088 game minutes), in about 2 minutes of wall time (the run is deterministic: the same plugins play the same run, and a change to where routes start, as the set-down arrivals and `SnapStart` were, sends it another way).

- It fills the party in 6 steps (Abrecan, Brenna, Rheda), and reaches Erromon (step 7), the goblin camp (8), Jundar (16), Arturo joining (26), Port Saiid and the Lighthouse (27, 28), Terminor and Niesen (36, 37), EhudUnder (38), Farris and Niesen's death (44), Abrecan back (46), Ugarit (49), the Barrows (52), the Shadow's merge (82), Roog's cave and Rooughah's naming (83, 84), and the last scenes at Gwernia castle (86).
- It fights 273 battles: 59 lost first and 27 won with a member dead, each played again and won outright, the dryads' fight at H12/1 among them; 3 stalemates; no deaths in battle stand; Niesen's death in EhudUnder is the story's. It camps 50 times and trains twice.
- The party at its milestones: Alaron 2 as Rheda joins; 11 at Jundar; 12 at Terminor as Niesen joins; 16 at Ugarit; 19 at the Shadow's merge and the ending, with Brenna and Rheda 18 and Abrecan 16. The user's own game had Alaron at 14 before Shamsuk's Tower and 20 before Rooughah.
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