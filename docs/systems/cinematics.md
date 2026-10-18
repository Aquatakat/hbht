# Cinematics and the Theatre (roadmap M2)

How the port plays the four pre-animated cinematics, and the System tab's Theater, which plays them again. Addresses are 1.0's: `Cinematic.Load` `0x80053880`, `Cinematic.Tick` `0x80053934`, `Cinematic.StartShot` `0x80053e4c`, `Cinematic.EndCase` `0x80053c20`, the captions in `cinematictext.cpp` (`0x800545d0`–`0x80054ca0`), the box `TextBand` (`0x80055800`), and the menu in `theatermenu.cpp` (`Theatre.Build` `0x80044444`). The symbol map has the rest. The tables themselves are in [hardcoded-content](../hardcoded-content.md#scripts-and-cinematics-the-engine-starts).

`CinematicPlayer` (`Aidyn.Core.Cinematics`) steps one, `CinematicLayer` and `CinematicView` draw it in the runtime, and `TheatreMenu` is the page.

## The records

`aidyn:cinematic/0`–`3` are the opening ("Gabrion's Search"), the Shadow's ("Shadow Self Merge"), Roog's ("Rooughah Naming") and the ending ("Coronation of Alaron"). Each has its Theatre title, its done flag (3, 569, 590, 601), and its shots: the scene, its Borg6 and the animation's length, the music, the near and far planes, and the shot's captions and fades. The table stores the captions' and fades' times halved; the importer doubles them into ticks from the shot's start, as the game does (each shot's last fade ends on its animation's last tick). There are 26 shots and 15 caption lists, each over one shot.

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
- **The scene's own lights** light it, as `BorgAnimDrawSceneRaw` does for a scene without the engine-lit bit (see [rendering](rendering.md#a-scenes-own-lights)): their colours tinted by their transforms' scales, which their animation drives as it turns them, with the last as the ambient. Nothing else brightens or dims a shot: its prim is white and its tint 1 (`BorgAnimLoadScene` sets it, and no cinematic calls `Scene.SetTint`). Nothing of the world applies: no sky, weather, tint or fog.
- **Billboards** face the camera, and **scrolling textures** move a step each picture, as each draw of a scene moves them.
- **Environment-mapped lists**, such as the opening's door hinges, look from the shot's camera toward each node, since a cinematic's scene keeps its own camera as its eye ([rendering](rendering.md#environment-maps)).
- **Nodes both turned and scaled** are posed from their tracks a whole transform at a time (`TransformTracks`), not by Godot's player alone, which sets a node's rotation and scale apart. The opening's spell needs it: its hemisphere, scaled from nothing to (5, 0.01, 5) as it spins in shot 2, is a flat red disc face-on to the camera, a ring round Alaron's hands and then a wall sweeping the room. Actors and the editor's model view are posed the same way.
- **Mirrored models show their insides,** as on the N64, which culls a triangle by how it winds on screen whatever its matrix ([rendering](rendering.md#mirrored-models)). The Shadow in Shadow Self Merge is Alaron scaled by −1 across (Borg5 0x24C9's transform 10, and the same in 0x23F6 and 0x2412), so the original draws the inside of its far side, which reads as a darker Shadow facing the camera (Godot's own culling would draw the outside, the back of its head toward the camera).
- **The blood in Shadow Self Merge's shot 2** (0x248A's transform 17, IA16 texture 0x2417 whose intensity is its alpha) darkens the floor toward its edge in the original too: it's combined `TEXEL0 × SHADE × PRIM` and blended by the texel's alpha, so an edge texel of 0.5 gives half the floor and a quarter of the pool's near-black red. The port draws it the same, with the material allowing for the RDP's texel centres being half a texel from the GPU's ([rendering](rendering.md#the-n64-material)).
- **Particle emitters** don't run: the pool is stepped only from the zone engine's frame.

### The caption box

It's the conversation box's widget with its grow flag (`TextBand.Create`, made by `Cinematic.CaptionsCreate` `0x800545d0`): the band `0x1C4` from (0, 200) to (300, 230) and the gradient `0x1C3` 11 tall above it, with the text at 25 across and 5 down, in the main font, (225, 225, 225, 200), wrapping at 295. It grows upward (`TextBand.SetText`, `0x80055a80`): when the text with 5 above and below is taller than 30, the top is 230 less that, so two lines put it at 198, three at 187 and four at 176. On the console the left bar hides its left end and the bottom bar its foot, so it touches the screen's bottom and left. It stops 4 short of the right bar because `SetText` lays it out again to the rectangle's right, 300, where the constructor had 320.

**The port** (`CaptionBox`) takes the right edge for a slip and doesn't keep it. It lays the box out in the original's frame at the foot of the UI screen, as the conversation box is, so the text wraps as authored at any of the UI's sizes, and anchors it to the screen's bottom: the band runs from inside where the side bars would be (16 to 304) down to the screen's foot, feathered beside it and above it with the popups' pieces and not at all below, six pieces in all. The user chose this shape, and combat's text box, the same widget at another size, will take it too.

## The Theatre

The System tab's Theater opens the Theatre's page (`OptionsMenu.ShowTheatre`, `0x8003fe34`). It's laid out in the 320 × 240 frame (`Theatre.Build`):

- **The title** `0x0100` ("Theater", 87 × 24), centred on x 160 at y 50.
- **The rows**, one a cinematic in the DB's order, each by its flag (`Theatre.AddRows`, `0x800447e8`): its title once its done flag is set, and 16 dashes until then. They're 16 apart from y 90, centred on x 160 in (103, 70, 60), a title with the space the DB ends it with. The cursor's row pulses from (103, 64, 64) toward (197, 174, 176) by 15ths, a step a frame, back and forth over 0 to 14; it starts on the first row and wraps.
- **A on a title** (`Theatre.Play`, `0x80044aa4`) rolls the menu up as closing does, and the cinematic plays; on the dashes it plays only A's sound. Up and down play the cursor's sound (0x073B), A 0x0740 and B 0x0741, as on the rest of the menu.
- **B or Start** go back to the list, the cursor still on Theater.
- **After it** the done flag is set again, and nothing else of its end happens: the opening's script, Roog's True Name and the ending's credits are for the story's own playing. Trek comes back where the player stood, rebuilt, its music starting over, fading in from black as after a cut-scene, and the menu reopens on the Theatre's page (`Theatre.Reopen`, `0x800d955c`, which trek's state 8 reads). The port reopens it once the fade-in has finished (`Game.ReopenTheatre`), as the original does: a frame of trek shows, and then the menu reopens on the page. The original also runs ten frames of trek after the menu rolls up, before the cinematic; the port starts it at once.

## What starts them

`Cinematic.Load`'s callers, with what `EndCase` does after each:

| Cinematic | Started by | After it |
| --- | --- | --- |
| Opening | The title screen's New Game | Script 866 starts from its dialogue object (id `0x362`) in Borg9 `0x0FDB`; trek |
| Shadow | Combat's state 0x11, as the Shadow merges with Alaron | Nothing more; trek |
| Roog | The end of the Rooughah scene (Borg13 1268) with flag 3442 set | Alaron gains his True Name; trek |
| Ending | The end of the last scene (Borg13 1278) | [The credits](ui.md#the-credits), which can't be skipped and end with "The End", and then the title |

**Not ported yet:** only the Theatre plays them. A new game starts in trek without the opening, the scripts' ends don't lead to theirs, and the Shadow's waits for combat. They're triggers on the [2.10 events](../roadmap.md), `cinematic.ended` among them. The runtime plays any of them from the command line ([runtime](runtime.md)): `--cinematic 0` plays the opening, `--cinematic 0,5,300` starts at shot 5's time 300, and `,hold` stands still there for a screenshot.
