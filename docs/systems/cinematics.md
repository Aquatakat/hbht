# Cinematics and the Theatre (roadmap M2)

How the port plays the four pre-animated cinematics, and the System tab's Theater, which plays them again. Addresses are 1.0's: `Cinematic.Load` `0x80053880`, `Cinematic.Tick` `0x80053934`, `Cinematic.StartShot` `0x80053e4c`, `Cinematic.EndCase` `0x80053c20`, the captions in `cinematictext.cpp` (`0x800545d0`–`0x80054ca0`), the box `TextBand` (`0x80055800`), and the menu in `theatermenu.cpp` (`Theatre.Build` `0x80044444`). The symbol map has the rest. The tables themselves are in [hardcoded-content](../hardcoded-content.md#scripts-and-cinematics-the-engine-starts).

`CinematicPlayer` (`Aidyn.Core.Cinematics`) is one playing, which the session steps a tick at a time and ends itself (`GameSession.Cinematic`, its `EndCinematic` transition going out before what follows); `CinematicLayer` and `CinematicView` draw what each tick left in the runtime and play each shot's music; and `TheatreMenu` is the page.

## The records

`aidyn:cinematic/0`–`3` are the opening ("Gabrion's Search"), the Shadow's ("Shadow Self Merge"), Roog's ("Rooughah Naming") and the ending ("Coronation of Alaron"). Each has its Theatre title, its done flag (3, 569, 590, 601), and its shots: the scene, its Borg6 and the animation's length, the music, the near and far planes, and the shot's captions and fades. The table stores the captions' and fades' times halved; the importer doubles them into ticks from the shot's start, as the game does (each shot's last fade ends on its animation's last tick). A caption list is always one shot's.

## Playing

The original runs a cinematic as game mode 0xE, stepped by the frame's ticks, uncapped. The port steps it a tick at a time, so it plays at 60 frames a second.

- **It starts** (`Cinematic.Load`, `Cinematic.Begin` `0x80053b74`) by clearing every sound and stopping trek's music, asking for it again so it starts over afterwards, copying the leader's name for the captions, freeing the HUD and making the caption box. Presses made before it are drained.
- **A shot** (`Cinematic.StartShot`) loads its scene with the clock at 1 and the table's planes on its camera, and plays its music. The scene steps before it draws, so the first picture is time 2, and it draws while the time it would step to is under the length − 1 (`0x80053a28`): the last picture is length − 2. The tick that fails draws nothing and frees the shot and its music (`Cinematic.FreeShot`, `0x80053ad8`); the next starts the next shot and draws it. So there's one tick between shots with nothing drawn, which the port shows as the last picture held, and nothing else between them: no fade but the table's.
- **The end:** after the last shot, two more undrawn ticks pass before `EndCase`.
- **B or Start**, newly pressed, skip the rest of it, from its first tick (`Cinematic.ReadInput`, `0x80053d18`); nothing else is taken. The press frees the shot at once, and the end comes the tick after.
- **The music** is the shot's sequence, looped and centred, at the louder of the sound and music volumes as they are when it starts, each × `255.0f` and truncated (`0x80053f48`), with the pan the literal 0x80 (`0x80053ed4`), the engine's centre. Each shot stops its own, so nothing plays across shots or after the end.

### Captions

- **A shot's list** replaces the last and starts its clock at 0 (`Cinematic.SelectCaptions`, `0x80054ca0`); a shot without one leaves the last list and its clock as they were. The clock counts the drawn ticks, so it's the scene's time less 1.
- **A caption** shows from the tick its start falls due, for its end less its start (`Cinematic.ShowDueCaption` `0x800549ac`, `ReadCaption` `0x80054a90`, `DrawCaption` `0x80054784`). One shows at a time, and a new one replaces it at once; none in the data overlap. There's no fade in or out, and the box shows only while a caption does.
- **"Alaron"** in a caption is the party leader's name, and "ALARON" the same in capitals (`Text.ReplaceAll`, `0x800548f0`).
- **The ending's last two captions never show:** its fourth shot (Borg6 `0x2098`, 1481 long) lists "Friends, here is your King." at 2160 and "Long live Gwernia!" at 2400, after the shot has ended. The port does the same.

### Fades

The table's fades (`Cinematic.StepFades`, `0x800541f0`) run on a clock from each shot's start, on the drawn ticks, with the overlay's colour carried across shots.

- **A fade starts** on the tick its start falls between the clock before and after. That tick takes the ticks since it was due as well as its own: a fade due at 0 starts two ticks in.
- **Its ramp** is its length less 4 ticks, and the overlay stays 64 ticks after its ramp and hold, then stops being drawn at all. A fade of under 10 ticks (the Shadow's lightning, 8) ramps for its whole length and stops at once.
- **Colours:** black (0, 0, 0), white and red (255, 0, 0) rise from nothing; a clear one keeps the colour showing and falls from full. The colour starts black, so a clear fade on a cinematic's first shot comes in from black; the opening's first shot has none, so it cuts in. The alpha is the progress × `255.0f`, truncated (`ColorBytes.Single`, `0x800543fc`), over the whole screen, captions too.

## Drawing

- **The picture** is the whole 320 × 240 screen, cleared to black, through the scene's own Borg3 camera (all 45° at 4:3) with the table's planes. The port draws it in a world of its own, framed to the cinematic's aspect ratio (`CinematicRecord.Aspect`, 4:3 for all four) with black beside or above and below it, captions and fades inside the frame. The user chose this: drawn across a wide window, the shots show past the sets' edges (the castle's outside in the opening, actors waiting off to the side in the Shadow's). A cinematic made for 16:9 would be letterboxed on a narrower window the same way.
- **The scene's own lights** light it, tinted by their animated transforms ([rendering](rendering.md#a-scenes-own-lights)). Nothing else brightens or dims a shot: its prim is white and its tint 1 (`BorgAnimLoadScene` sets it, and no cinematic calls `Scene.SetTint`), and nothing of the world applies: no sky, weather, tint or fog.
- **Billboards** face the camera, and **scrolling textures** move a step each picture, as each draw of a scene moves them.
- **Environment-mapped lists**, such as the opening's door hinges, look from the shot's camera toward each node, since a cinematic's scene keeps its own camera as its eye ([rendering](rendering.md#environment-maps)).
- **Nodes both turned and scaled** are posed from their tracks a whole transform at a time (`TransformTracks`), not by Godot's player alone, which sets a node's rotation and scale apart. The opening's spell in shot 2, a hemisphere flattened as it spins, needs it. Actors and the editor's model view are posed the same way.
- **Each tick stands on its own sample.** The original builds every node's matrix from its tracks' values each tick (scale, rotate about the pivot, translate: `Borg5Scene.PivotRotate`, `0x8009497c`), so a node turning about a pivot away from its origin keeps the pivot where it is however fast it turns. The port's clips have a sample a tick, and the views have Godot bake them at that rate as they load them (`GlbFile.BakeFps`, [animation](../formats/animation.md)). At Godot's default of 30 a second every other tick fell halfway between two samples, the translation and rotation interpolated apart, and such a node left its pivot: the purple cube on Ardra's sceptre in the ending's fourth shot (scene 0x2097's transform 145, turning 99.9966° a tick about the sceptre's axis for the whole shot) flew up to 1.5 off the sceptre on the even ticks. The same put the Shadow's rolling billboards (0x23F6, 0x24C9) and fast-moving nodes in the opening's third shot (0x2291), Roog's second (0x2568) and the ending's last (0x21D7) off by a tenth to two units on some ticks. Between ticks, Godot's physics interpolation did the same to the frames drawn faster than 60 a second: it blends each node's translation and rotation apart from one tick's pose to the next, so at 120 fps the cube circled the sceptre's tip about a unit out (the user's playthrough, Oct 24, 2026). So a shot's scene doesn't interpolate and stands on its ticks' poses, as the original's frames stand on theirs, and only its camera is smoothed (`CinematicView.Show`).
- **Mirrored models show their insides,** as on the N64, which culls a triangle by how it winds on screen whatever its matrix ([rendering](rendering.md#mirrored-models)). The Shadow in Shadow Self Merge is Alaron scaled by −1 across, so the original draws the inside of its far side, which reads as a darker Shadow facing the camera.
- **Particle emitters** don't run: the pool is stepped only from the zone engine's frame.

### The caption box

It's a `TextBand` with its grow flag (`TextBand.Create`, made by `Cinematic.CaptionsCreate` `0x800545d0`), a widget of its own that only the captions and the battle's log box use ([ui](ui.md#the-log-box)), not the conversation box's: the band `0x1C4` from (0, 200) to (300, 230) and the gradient `0x1C3` 11 tall above it, with the text at 25 across and 5 down, in the main font, (225, 225, 225, 200), wrapping at 295. It grows upward (`TextBand.SetText`, `0x80055a80`): when the text with 5 above and below is taller than 30, the top is 230 less that, so two lines put it at 198, three at 187 and four at 176. On the console the left bar hides its left end and the bottom bar its foot, so it touches the screen's bottom and left. It stops 4 short of the right bar because `SetText` lays it out again to the rectangle's right, 300, where the constructor had 320.

**The port** (`CaptionBox`) takes the right edge for a slip and doesn't keep it. It lays the box out in the original's frame at the foot of the UI screen, as the conversation box is, so the text wraps as authored at any of the UI's sizes, and anchors it to the screen's bottom: the band runs from inside where the side bars would be (16 to 304) down to the screen's foot, feathered beside it and above it with the popups' pieces and not at all below, six pieces in all. The user chose this shape. The battle's log box is the same widget, but the port floats it as the conversation box instead ([ui](ui.md#the-log-box)).

## The Theatre

The System tab's Theater opens the Theatre's page (`OptionsMenu.ShowTheatre`, `0x8003fe34`). It's laid out in the 320 × 240 frame (`Theatre.Build`):

- **The title** `0x0100` ("Theater", 87 × 24), centred on x 160 at y 50.
- **The rows**, one a cinematic in the DB's order, each by its flag (`Theatre.AddRows`, `0x800447e8`): its title once its done flag is set, and 16 dashes until then. They're 16 apart from y 90, centred on x 160 in (103, 70, 60), a title with the space the DB ends it with. The cursor's row pulses from (103, 64, 64) toward (197, 174, 176) by 15ths, a step a frame, back and forth over 0 to 14; it starts on the first row and wraps.
- **A on a title** (`Theatre.Play`, `0x80044aa4`) rolls the menu up as closing does, and the cinematic plays; on the dashes it plays only A's sound. Up and down play the cursor's sound (0x073B), A 0x0740 and B 0x0741, as on the rest of the menu.
- **B or Start** go back to the list, the cursor still on Theater.
- **After it** the done flag is set again, and nothing else of its end happens: the opening's script, Roog's True Name and the ending's credits are for the story's own playing. Trek comes back where the player stood, rebuilt, its music starting over, fading in from black as after a cut-scene, and the menu reopens on the Theatre's page (`Theatre.Reopen`, `0x800d955c`, which trek's state 8 reads). The port reopens it once the fade-in has finished (`GameSession.CinematicEnded`, then `Transition.ReopenMenu`), as the original does: a frame of trek shows, and then the menu reopens on the page. The original also runs ten frames of trek after the menu rolls up, before the cinematic; the port starts it at once.

## What starts them

`Cinematic.Load(index, kind, return mode)` has four callers, and `EndCase` does what the kind says after each (its table at `0x800D20B0`). [Events](events.md#the-base-plugins) has each call site and ending with its addresses, and the base plugin's trigger that carries it in the port: the opening from the title's New Game (`game.started`), Roog's and the ending from their cut-scenes' ends (`script.ended`), and what follows each from `cinematic.ended`.

- **Every kind sets the done flag first**, in `EndCase`'s common part, and from the Theatre (kind 0) nothing else happens.
- **A cinematic started from a cut-scene's end** plays straight from it, the cut-scene's return mode 0xE: no trek is built or drawn between them.
- **The Shadow's** is loaded by combat as the Shadow merges with Alaron (`Combat.StartShadowCinematic`, `0x80023984`, `Load(1, 4, 1)`): state 17 merges on its first frame and leaves combat, so it plays once the battle has gone, and `Cinematic.EndShadow` (`0x80054150`) brings trek back where the battle began. In the port it isn't a trigger: a battle that ends merged (`BattleOutcome.Merged`) has `GameSession.EndBattle` play the combat record's `shadow` cinematic once the battle has left ([combat](combat.md#the-special-battles)).
- **In the port** `GameSession.CinematicEnded` fires `cinematic.ended`, and `GameSession.StartNewGame` fires `game.started`, which the title's New Game calls once it has built trek's world where the player arrives ([ui](ui.md#the-menu)), and the runtime for `--opening on` ([runtime](runtime.md)). A save of a new game, which stands nowhere (`GameState.StartsAsNewGame`), fires it too when the menu's Load starts it; the original's Continue never plays the opening, as its saves are all made in trek ([saves](saves.md#the-format)). The port builds trek's world before the opening, where the original builds it after script 866; the player arrives at the same place.
- **The command line** plays any of them, and what follows it in the story (`--cinematic`, [runtime](runtime.md)).
