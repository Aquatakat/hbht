# Cameras

`HBHT.Core.Cameras`.

## Why the original behaves as it does

The user timed the outdoor camera in 1.0. After running left or right and stopping, it takes 500–1000 ms to settle. Standing still, it turns slowly toward Alaron's back and decelerates as it gets there: 45 s from about 5° off head-on to 90°, then 70 s more to settle behind him. Facing it head-on, it stays put.

The shape of `Camera::Mode0` (`0x800a6e64`) explains that. The aim chases Alaron at 1/12 of the gap per 60 Hz tick. Standing still, `Actor::Move` calls `SetFeild70` every frame, so the goal behind him is approached at 1/144 a tick, and the camera at 1/16 of that. The turn is then proportional to the sine of the angle off his back, which stalls head-on. Both steps loop `delta` times against a goal fixed for the frame, so the original's turn rate goes with its frame rate: the user's timings fit a `delta` of about 2.6 ticks. The port steps it at the 30 fps cadence ([ADR 0017](../adr/0017-the-core-steps-every-tick.md)); the trek `delta` distribution is ADR 0006's follow-up.

## The follow camera (`Camera::Mode0`)

Ported as `HBHT.Core.Cameras.FollowCamera`, from `Camera::Mode0` and the R orbit (`0x800a72bc`, the decomp's `FUN_800b0fac`), which `Mode0` and `Mode1` both call.

- With R held, the orbit keeps the camera between 0 and 5 above the aim, its reach across the ground easing toward 5 by 1/32 of the gap a tick, so it tops out at 45° once eased out and is steeper while nearer, and never goes below level with Alaron. The user found the port's orbit matches 1.0's.
- **Port addition: moving while orbiting.** In the original the stick orbits with R held and he stands still. On a dual-stick pad the right stick, Q and E, and the mouse turn the camera while the left stick or WASD still walk him (`PlayerInput.Free`, in `HBHT.Core.Trek`); only R with the stick holds him, as on the N64. The orbit's goal is a fixed point it turns about, so a walking player would leave it behind and swing the view. While he walks, `FollowCamera.Orbit(…, moving: true)` carries that point by how far the aim moved each tick, so the view stays as it was turned. Letting go of the stick returns the camera behind him as it does after R. The user's choice.
- **Port addition: steering it.** What turns the camera is the player's to bind on the options' Controls page ([ui](pause-menu.md#controls), [ADR 0026](../adr/0026-controls-are-bound-by-situation.md)), and `Game.ReadInput` makes the `PlayerInput` from it:
  - **Camera's own sources** (by default the right stick) and Camera Left and Right (Q and E) turn it while he walks, as above.
  - **Camera Hold** (RB, or the right mouse button) is R. A source on both Move and Camera (by default the left stick) moves him, and while Camera Hold is held steers the camera instead, and he stands still (`PlayerInput.Free` false, as R with the stick). A source on Move alone still walks him under the hold, so WASD walks while the mouse turns the camera with the right button held. He stands still only while what would have moved him steers.
  - **The mouse** can't move him. Off the Camera row it steers while Camera Hold is held, a drag: about 0.3° of orbit and 0.02 of height a pixel, times Mouse Sensitivity. On the Camera row it's mouse look: it always steers, and the runtime captures it in trek and a battle's turn and lets it go elsewhere. The orbit is then held every tick (`PlayerInput.Orbit`), so the camera stays where the mouse leaves it and is carried with him as he walks, rather than returning behind him when the mouse stops, and the stick walks him the way the camera looks.
  - **Stick look:** the right stick on Camera, the default, turns it as Q and E do; pushed up it raises the camera, as R with the stick does. The mouse's up lowers it, as mouse look does in most games. There's no Invert row: the stick keeps the N64's sense, and the mouse the PC's.
- `Mode0` doesn't avoid walls at all. Only the R orbit pulls in. It moves an eighth of the way a frame toward a point 0.25 *past* the wall (the ROM has the −0.25), while still easing out toward 5. The two settle about 3.1 from the player, so with a wall any closer, the camera ends up behind it. The user found the port's matches 1.0's at walls.
- Interiors use `Mode1`, which does keep the camera off walls (below).
- Loading and dialogue set a quick swing (`unk80`), but the first tick after a load cancels it, because the anchor starts at the origin.

## Which mode

The camera's mode is chosen only when the player goes through a teleporter (`TeleportPlayer`), by the chunk he arrives in: `Mode1` where its terrain doesn't draw the sky (the original's sky type 4: terrains 6, 8, 9, 11 and 13–16, the importer's `TerrainRecord.SkyDrawn` false), `Mode0` elsewhere. Walking from chunk to chunk keeps it, so Gwernia castle's courtyard (terrain 25) keeps whatever mode the door into it gave. Loading a save from the game's menu teleports too, so it chooses the same way. Loading from the title screen doesn't: the zone engine starts in `Mode0` and keeps it until the next door, so the outdoor camera shows inside a house until a camera object takes over. That's a bug, and the port fixes it, as the user chose (ADR 0018): however the game starts, the chunk chooses. `FollowCamera.Mode` holds it, and `FollowCamera.ModeFor` chooses it.

A teleport places the camera (`FollowCamera.Arrive`): first it moves with the player and stops orbiting (`ApplyZoneVelocity`, `GiveCameraToPlayer`), then it goes to the teleporter's camera point if it names one. Otherwise, in an interior it's 4 *in front of* the player and 3 up, looking back at him: the original subtracts its facing, which points behind him. Outdoors it's behind him, as a map load puts it, and swings round quickly for 5 frames (`unk80`).

Camera objects work through a teleport's fade as well, as the original's zone engine runs on under it, so the room's camera has the camera as the screen comes up.

## The interiors' camera (`Camera::Mode1`)

Ported as `FollowCamera.Interior.cs`, with the arguments `ProcessGameCamera` passes: 2, 4, 0.5, 2.5, 1.5, 3, 16, 32, 0.25 and 0.1. The jump behind the player sweeps a sphere of 0.25.

- It hangs back on a leash rather than swinging behind. Its goal is 1.5 above the aim's goal, and only dragged along to stay 2 to 4 from it in X and Z. So walking away, it follows 4 behind; walking toward it, it backs away 2 ahead.
- It eases toward its goal at 1/32 of the way a tick, but no more than 0.1 (6 a second), and turns round the aim toward it by a degree a tick. It aims 1.05 above the player, chasing that at 1/16 a tick, at most 0.25.
- It's never nearer than 2 in X and Z, and it can't pass through a wall: the sphere swept from where it was stops 0.25 off it.
- When it has caught up with its goal and the aim is out of sight from there, its goal jumps to 3 behind the player, or to the wall in the way, and it doesn't do that again for 5 seconds.
- R orbits as outdoors, at a distance easing to 3, between 0.5 and 2.5 above the aim. Letting go zeroes the pitch, which only the orbit uses here.
- Every part loops over a frame's ticks or acts on a goal that only it moves, so stepping it each tick matches the original at any frame rate.
- It never wanders past a wall on its own, and running round a corner it snaps back behind Alaron. R's orbit can push it through a wall; letting go, it turns until it's back inside, with the same snap.

## The combat camera (`ProcessCombatCamera`)

Ported as `HBHT.Core.Combat.CombatCamera`, from `ProcessCombatCamera` (`0x8008D8BC`, camera mode 4). It's the interiors' camera with other numbers, which `FollowCamera.StepLeashed` takes (`LeashNumbers`; the interiors' are `FollowCamera.Interior`): the goal leashed 4 to 10 from the aim in X and Z (8 to 16 while aiming a flask or placing an area), 2 above it, the aim 1.05 above the body that owns the camera and chasing it at 1/16 a tick, at most 1, the eye at 1/32, at most 0.5. It's put 8 behind a party member away from the nearest enemy, 1.5 up, at once on the battle's first frame and as a goal when a member's turn takes the camera; through an enemy's turn the eye holds where its goal was, at least 4 from the aim; and it's kept a unit above the arena's floor. [Combat](combat.md#the-rest-by-entry-point) has the addresses.

## The enhanced battle camera (Camera: Enhanced)

A port enhancement, the options' **Camera: Original / Enhanced** on the Enhancements page ([pause menu](pause-menu.md#options)), for battles played turn by turn (Combat Turns: Original). Original is the default, and under it nothing changes. It's piece B of Start Early's plan in the [roadmap](../roadmap.md#planned); the user set what it must do, and its design is here rather than in an ADR, their choice. Under Start Early the camera is Start Early's, for now the newest action's ([combat](combat.md#overlapping-the-ais-turns)); piece C will make it this one's.

**It's in the core**, not the drawing. A turn's stick drives the body by the camera's look (`Battle.InputTurn`), and the free cursor and a placed area move by it (`MoveAimCursor`, `MoveArea`), so the camera the player sees has to be the one they're read against. `CombatCamera` steps the original's camera every tick, as it always has (`StepOriginal`), and then, under Enhanced, `EnhancedCamera` over it: `CombatCamera.Camera` is still the original's, which R's orbit and a turn's start move, and `CombatCamera.Shown`, which `Battle.Camera` is, is the enhanced camera's. The recording, the drawing, the keys' listener and the radar read `Battle.Camera`. Under Original, `Shown` is the original's camera itself.

**What it shows**, by the battle's state (`EnhancedCamera.Decide`):

- **Events** (enemies' turns, attacks, casts, results): the original's shot, moved to with the motion below, so its lead-ins are slower and it never jumps. Every 15 ticks it looks whether the original's shot hides a subject it holds, behind the arena's collision or another body drawn across the sight line: the camera's owner, whose turn it is and the glide's two bodies (`ShotFinder.Clearing`). If it does, the least turn, raise and draw-back that clears it (of 25° to 180° turns, 0° to 30° raises, 1 or 1.3 times as far) is put on the original's shot and kept, following it, until the original's has been clear three looks in a row.
- **The player's turn** (state 1): whom they point at. A cone 40° either side of the way the character points takes every living body on the field, enemies and party alike, each scored by how near the cone's middle it is and by its distance: nearer with a melee weapon (`e^(−d/6)`), farther with a ranged weapon or a staff (`d/(d+8)`; a weapon with a range or a missile, a flask, or weapon skill 5, the staves'). A party member weighs 0.75 of an enemy. The pick is held while it stays within 60°, and another takes its place only once it has scored 0.3 better for 20 ticks in a row (`TargetCone`), so the stick's wobble doesn't flick the camera between two bandits. The shot holds the character and the target (`ShotFinder.Best`): of 10 turns from the view now and pitches of 25° to 70°, the one hiding neither, turning least, nearest 40° of pitch and nearest, with a bonus for an eye on the character's side, away from the target; as near as holds both, 7 to 60 away, so wide and high shots come when they're needed. The framing (its yaw and pitch in the world) is held and looked at again every 15 ticks, changing only for one 0.3 better; it moves with the bodies between. With no one in the cone it's the original's shot, kept clear. This is what shows reserves coming on over a hill, which the original's camera behind the character never does.
- **A ranged aim** (state 18, a throw's free cursor; state 10, a spell aimed at targets; state 9 with a ranged weapon and a ranged action): a shot from 55° above, at the yaw the view had as the aim began, holding the caster and 16 points round its reach (the weapon's range, a flask's 400 or the spell's reach about the caster), kept on the grid, on the ground there (`AimFraming`). At most 70 away. The reticle (the cursor, or the body aimed at) moves freely in it: only once it nears the screen's edge (90% of the way) does the shot follow, by as much as keeps it in. Melee aiming is the original's.
- **An area placed** (state 11, Fireball, Teleportation and the dispels): the same from 80° above, near enough straight down, holding the caster and every place the area can reach: the spell's range times its level about the caster, and the area's radius beyond. The stick's up is the screen's up, since the look across the ground comes from the yaw (`Shot.LookXZ`), not the eye, which from straight above would be no way at all. 80° rather than 90° keeps it readable as ground.
- **The player's hand on the camera.** Turning the camera with its stick (R with the stick, the right stick, Q and E, mouse look moving) in the player's turn lets go of the director until the turn ends (`CombatCamera.TakeHand`, `EnhancedCamera.Released`; a turn's end, `CombatCamera.Reset`, takes it back). The original's camera is put where the shot shown is, so the orbit starts from what the player sees, and its leash swoops it in from a wide shot. It's moved to with the motion's caps four times as high (`HandAcceleration`), so the view follows the hand closely.

**Motion: acceleration capped, not speed** (`CappedMotion`). A shot is an aim point and a yaw, pitch and distance about it (`Shot`), so a swing round a subject keeps its distance and the view's turn is the yaw and pitch alone. Each of the four is moved toward the goal with its acceleration and its deceleration capped, its speed not: the aim 30 units a second squared, the turn 240° a second squared, the distance 40. Each tick a channel wants the goal's own speed plus `min(√(2a|e|), 4|e|)` toward it, the fastest from which the cap still stops it there, and its velocity changes toward that by at most the cap's share of a tick. So a move across the field (72 units) takes about three seconds, peaking near 45 a second, and a short one is quick; nothing whips into a move or stops dead. A goal that moves (a walking body) is followed at its own speed, read from how it moved, unless it jumped more than a unit in a tick. A goal more than 80 from where the camera aims is cut to; in the test battles that never happens. The eye is kept a unit above the ground under it (a sphere swept down from 30 above), and a shot from inside a hill isn't chosen at all, since a sight line out through the back of a hill's triangles meets nothing.

**Sight lines** (`SightLines`): a sphere of 0.1 swept from the eye to just inside a subject's sphere meets the arena's collision facing it, see-through triangles passed over (`SpherePhysics.Blocked`'s `skipSeeThrough`), or a body is drawn across it, taken as 1.2 times its radius for limbs and wings. Collision faces one way, so a wall seen from behind doesn't hide anything, as it isn't drawn either. Where it may go: anywhere. The arenas are ordinary world chunks, and the indoor ones have no ceilings and a black sky.

**Every number** is in `CameraTuning`, each set by name from the command line: `--battle-camera enhanced,AimAcceleration=20,ConeAngle=30` (lists joined with `;`, `Pitches=30;60`). F3's overlay has its line in a battle: what it shows, the target, whether the player has it, the shot's yaw, pitch and distance, the aim's speed and the cuts. `--battle-plan "Name=step/step;*=step"` plays the party's turns by script (`BattlePlan`: `wait:n`, `stick:x:y:n`, `orbit:x:y:n`, `accept`, `back`, `skip`, `spell:Name`), for movies and tests comparing settings with the same inputs.

**Rules.** It reads the battle and draws no random numbers, and neither does `CombatCamera`. The player's stick read against a different camera is their own input, so a walk may go elsewhere. With the same presses the battle plays the same, tick for tick (`EnhancedCameraBattleTests`, on the rats and the skeleton archers). The cosmetic stream may differ: the bodies' sounds are heard from the camera shown, so which of a key's sounds plays, and whether one is heard at all, goes with where it looks, as it does when the player orbits under Original.

**What piece C can ask of it.** `ShotFinder` holds no shot of its own: given the subjects (`ShotSubject`: a sphere and a weight) and the yaw the view has, `Best(subjects, from, behind)` is the best shot holding them all and hiding none, with its score; `Framed(subjects, yaw, pitch, most)` the shot from a way; `Score` how well a shot does; `HiddenWeight` what it can't see; `ShotFraming.DistanceToHold` and `Holds` the lens's arithmetic. `SightLines.Bodies` is what may stand in the way, which a drawing fills with where its bodies are drawn. `CappedMotion.Step(goal)` moves under the caps toward any goal shot a tick at a time, and `Cut` jumps, so a director that knows its next shot early can start moving early and arrive on time.

**Port's classes:** `CameraStyle`, `Shot`, `Lens`, `ShotSubject`, `ShotFraming`, `SightLines`, `CappedMotion` and `MotionCaps` in `HBHT.Core.Cameras`; `EnhancedCamera`, `ShotFinder`, `TargetCone`, `AimFraming`, `CameraTuning` and `BattlePlan` in `HBHT.Core.Combat`.

**Left:** trek's camera isn't enhanced yet; the setting is named for that. The user hasn't played it.

## Camera objects (`processVoxelCamera`)

Ported as `CameraMove`. A camera object takes the camera while the player is in its reach ([map-objects](map-objects.md#camera-objects) has how he reaches it). While it moves the camera or its aim, the follow camera does nothing the next tick (`camera_mode` 2, then `RevertMode`), and when it lets go, the follow camera carries on from where the camera is, in its own mode.

- **Its first tick** starts the move from wherever the camera is now, toward its reference point (or, without one, the player's sphere's bottom plus 1.05, or 0.5 in a conversation). A cut that tracks aims straight at the player then. The original does that for every tracking move, which jolts the view when a pan or an ease takes over (below).
- **The moves,** each over its duration (the values' Z) in ticks: a cut (0x1) jumps there at once; a pan (0x2) goes at a steady speed; a sine (0x4) eases in and out, (1 − cos) / 2 over half a turn; 0x8 eases in, 1 − cos over a quarter turn; 0x10 eases out, sin over a quarter turn. Flag 0x4000 moves the aim and 0x8000 the camera; no object does both. Each writes the camera or its aim, not their goals.
- **Tracking** (0x2000) chases 1.05 above the player at 1/24 of the way a tick, at most 0.25. Running, at 0.06 a tick, that leaves the aim about 1.4 behind him, so he runs ahead of the middle of the screen and the camera catches up when he stops. In the original, a camera taking over snaps the aim onto him: a jump of 4° to 8° in one tick at 60 Hz, which jolts the view. The user chose to treat it as a bug, so the port snaps the aim only on a cut, where the view jumps anyway, and the runtime doesn't blend a cut across frames.
- **A finished move stays done** while the player stays in reach. The original marks it to start again, but the timestamp it writes at the end of the same call overwrites the mark. Out of reach for a tick, it starts afresh next time.
- **The timestamp** is taken from the original's frame counter (`GameFrame.Counter`, `gGlobals` + 8, `0x800d8090`), which `AppProc` counts once a frame whatever the game is doing (`0x8001ab0c`), and which camera objects and cut-scenes' cameras both read (`ProcessVoxelCamera`, `0x800160d4`: it starts the move again where the timestamp is below the counter, and writes the counter + 2). It isn't the time played a save keeps (`TerrainStruct.PlayTime`). The port counts it once a tick in every mode, beside the time played (`GameState.Ticker`, `CountTick`), and the move keeps the tick + 1.
- **Flags 0x100 and 0x200,** on the objects that don't move the camera, set how far behind the outdoor camera follows (the values' Y, at least 3) for the next 30 ticks, and its pitch (X), anchoring it at the player. The interiors' camera reads neither.
- Cut-scenes' cameras are the same moves (`AllocScriptCamera`): see [below](#cut-scenes-cameras).
- In a conversation the original counts the cameras holding it, and while any does, R doesn't orbit; the player has no input in a conversation anyway ([the conversation camera](#the-conversation-camera)).

## The conversation camera

From 1.0's machine code. Talking to a wanderer, both bodies are flagged `ACTOR_800`, and the player's camera focus is cleared (`ClearPlayerCamFocus`). Each frame, `ProcessPlayers` (`0x80012854`–`0x80012968`) gathers the flagged bodies and, in trek with no focus, frames them (`CreateFocusFromMultiple`, `0x800a77b4`):

- **The focus** is the midpoint of the two bodies farthest from their centroid: with two, the midpoint between them. It's anchored there (`SetFeild70`) and the camera aims at it as at the player.
- **The way it looks:** with h from the farthest body to the focus, the facing passed is (−|h.z|, |h.x|), normalised, in the original's reversed facing (`0x800a79cc`–`7a08`), so the camera sits that way from the focus: always toward −X and +Z, whoever stands where. That's across the line between the two when it runs from −X−Z to +X+Z, and along it, looking past one at the other, when it runs from −X+Z to +X−Z.
- **The distance** is (|h| + 4) / tan 33.5°, at least 5 (`0x800cdb14`): 33.5° is half of the 75° field of view (`0x800cdb10`) less 8. Two bodies 1.6 apart put it about 7.2 away.
- It's stepped as the follow camera with that distance (`unk5c`) for the frame, then the distance goes back to the default, and `unk80` is 1, a quick swing each frame.
- The port's framing matches the original's, the odd side included, the user's choice: it may be what keeps the camera out of walls somewhere. A better one, placed as cut-scenes' are, is for much later.
- **A camera object holding the camera** keeps it where it is: the conversation doesn't take over, which spares interiors' cameras. The port keeps camera objects and trigger triangles' cameras working through a conversation, and `FollowCamera.Frame` leaves a held camera be, as `Step` does.
- At the end the camera is given back to the player (`GiveCameraToPlayer`) with a quick swing of 5 frames (`unk80` 5), and its pitch back to the default.

## Cut-scenes' cameras

Framed as 1.0's: a cut-scene in the port looks wider than in the emulator only by 1.0's overscan bars, which the port doesn't draw ([ui](ui.md#the-screen)).

`ScriptCameras`, from 1.0's machine code. Each camera op of a script starts a move that holds the camera for its duration (`DialougCreateScriptCamera`, `AllocScriptCamera` at `0x800168f8`): a camera object's move (`CameraMove`) with the op's flags, aimed at a point the op chose as it ran.

- **The ops** (`DialoguePreCallback`, jump table `0x800d1d80`), each over the script's camera time (60 until set), or one tick for a cut:

  | Op | Moves | How | To |
  | --- | --- | --- | --- |
  | CutTo | camera | cut | a reference point |
  | CutToActor | camera | cut | the actor's eyes, and hides it |
  | On | aim | cut | a reference point |
  | OnActor | aim | cut | the actor's eyes |
  | AngleTo | aim | steady pan | a reference point |
  | AngleToActor | aim | ease (sine) | the actor's eyes |
  | PanTo | aim | ease (sine) | a reference point |
  | PanToActor | aim | steady pan | the actor's eyes |
  | SineTo | camera | ease (sine) | a reference point |
  | SineToActor | camera | ease (sine) | the actor's eyes |
  | TrackOn | aim | chase | where the speaker stood (no script uses it) |

  Angle-to and pan-to are crossed between the point and actor forms (AngleTo steady, AngleToActor eased, as the table has them).
- **An actor's eyes** are 0.15 in front of it, at its sphere's centre raised by its height × scale less 0.2 (`GetHeightMinPoint2`) and its scaled radius: Alaron's 1.62 above his feet. They're taken as the op runs, not followed. A point-of-view cut hides the actor (`ACTOR_2000`), but not its shadow or what it holds; a cut or sine to a point or an actor, or a spawn, shows every actor again (`0x80014dfc`). The flag is tested only where `Bodies.Render` (`0x80013dcc`) draws a body's own scene, and only while a cut-scene plays (`CutScene.Playing`, `0x800dfe14`, at `0x8001403C`–`0x80014058`); what it holds is drawn after that test (body `+0x730`), and the shadows (`Shadows.Render`, `0x80012358`, from `RenderZones` before the bodies) test only the body's slot, its distance from the aim and its own alpha (`+0x70C`), which a hide leaves alone. So the camera at an actor's eyes sees its shadow beneath it, as the port draws it (`CutSceneActorView`): the point-of-view shots of Pochanargat's (`aidyn:script/0x04FD`) and Phelan's (`0x04FE`) scenes have one, in footage of the original too.
- **A move with no reference point goes to the focus itself**: `processVoxelCamera` takes 0.5 off for the missing player and adds 0.5 back in a dialogue.
- **Several at once.** Up to seven (the eighth crashes), stepped in slot order, the last freed taken first, each writing the camera. A cut is done the step after it starts; an ease ends on its goal. The script's first nodes start their cuts together with any pan after them, which takes its first step in the same tick.
- **Nothing else moves the camera.** While a move holds it, the follow camera's mode is 2 and does nothing; once the last is freed, the camera's focus is cleared, so the follow camera doesn't run at all. The camera stays where the last move left it. Camera objects don't run in a cut-scene.
- **Waiting.** `counter1`, how many moves ran the step before, holds the player's presses and a `WaitForCamera` (`funcs1[1]`), so both carry on a step or two after the last move ends (`ScriptCameras.Busy`).
- **The planes** are 0.1 and 125 in the dialogue state (`RenderZones`, `0x8000d66c`). The field of view never changes. Retail crashes if a step leaves the camera within 0.05 of its aim.
