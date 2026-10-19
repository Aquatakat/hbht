# Audio (roadmap 1D)

How the port plays sound, and what plays which sound when. [The audio format](../formats/audio.md) has the samples and the sequences' layout. Addresses are 1.0's.

## What's ported

- **The player** is `AudioEngine` in the core's `Audio`: the original's sequencer and voice manager, stepped on the original's 1000 µs clock at any output rate, mixing 32 voices to 16-bit stereo. It plays a `Sequence`, which `Sequence.Load` builds from a sequence record, its `.dcm` events and its samples' WAVs. It's tested against hand-built sequences for timing, tempo, pitch, volume, pan, loops, note-offs and the 32-sequence limit. `aidyn audio` renders with it.
- **The game's side** is the core's `GameAudio`, which tells the player what to play: the background music, one-shots and the volumes, from `Settings`. `SoundBank` loads sequences from the plugins as they're first asked for, and keeps them. Everything takes a lock, so the runtime's `AudioOutput` mixes on a thread of its own, about 60 ms ahead, into a Godot generator stream at Godot's mix rate, and the game's hitches as chunks load don't starve it. The runtime steps `GameAudio` every tick, in the pause menu too.
- **Trek** (`TrekLoop`) asks for each chunk's music as the player comes into it, and holds the change through fades. `MapSounds` plays the loaded chunks' sound objects, `AudioKeys` bodies' footsteps and voices, and map objects and scripts play one-shots. A cut-scene's stage asks for its own music and plays its chunks' sounds and its actors' keys.
- **The pause menu** plays the menus' sounds for the presses it takes (`PauseMenu.PressSound`), its tabs' as they slide, and the coins as the loot window opens on gold.
- **Data:** the map sets' music is on the map set and chunk records (`MapSetRecord.Music`, `ChunkEntry.Music`). The sounds engine code names are `aidyn:sounds/0` (`SoundsRecord`), with the body kinds' footsteps. Each entity's audio keys are its `EntityRecord.Sounds`, each weapon's sounds its `WeaponRecord.Sounds`, and each spell's cast its `SpellRecord.Cast`.

## The driver

Two players run on libultra's synthesizer, each called every 1000 µs of audio (`_timeToSamplesNoRound`: 44 samples at 44,095 Hz), so a sequencer millisecond is 0.998 ms. The game asks for 44,100 Hz (`Audio.Start`, `0x8008F680`), which NTSC's clock makes 44,095. The synthesizer has 32 voices, makes 16-bit stereo, and has an effects bus whose mix is always 0, so there's no reverb.

- **The sequencer** (`AudioEngine.StepPlayers`, `0x80090C94`) steps up to 32 sequences at once (`AudioEngine.Play`, `0x800907A4`, fails beyond that). Each counts calls up to its row period, then reads a row. It stops a sequence that has played past its duration (-1 for none), and at the stream's end either stops it or goes back to the loop position. When a looping sequence ends, every voice is cut and it starts again a row later.
- **The voice manager** (`AudioEngine.UpdateVoices`, `0x800900E4`) carries the sequencer's changes to the synthesizer on its next call, and frees a voice that plays once when it reaches its sample's end. A new note takes a free voice. With none free, a sequence started with a non-zero priority takes the oldest; otherwise the note is dropped.
- **Volume:** a note's volume byte is `trackVolume × sequenceVolume >> 4`, where the sequence's is the byte it was started with (and `AudioEngine.SetVolume` changes). The synthesizer gets `min(byte << 6, 0x7FFF)`, and its envelope mixer squares that (`v × v >> 15`). So full volume, 15 × 255 >> 4 = 239, is 0.218 of full scale on each voice. Changes glide over 1000 µs. There's no envelope: notes start with a 1000 µs glide up and stop with a hard cut.
- **Pan:** a sequence also has a pan, 0x80 the middle. A track's pan is scaled into a window around it, `255 - 2|P - 0x80|` wide, so at the middle the track's own pan holds and towards either end every track narrows to the sequence's. The synthesizer takes `pan >> 1`, at most 127, and pans by equal power (`eqpower`, `0x800E5010`, `32767 × cos(k × π / 254)`).
- **Where the port differs:** the sequencer's changes reach the voices at once rather than on the voice manager's next call, up to a millisecond sooner. libultra's four-tap resampler is approximated with cubic interpolation. And where the original reads past a sample's end (one instrument's loop runs past its sample), the port plays silence.

## Not played yet

What makes a sound that the port doesn't do yet, each waiting for what plays it:

- **The title screen's** music (0x0632 at volume 0xA5) and its menu's sounds (`IntroMenu`: the cursor, A, B and Start). There's no title screen yet.
- **Combat:** the battle music (on the battle arenas' map set record), weapons' sounds (`WeaponRecord.Sounds`), spells' casts (`SpellRecord.Cast`), and the post-battle window's 0x05E8 when a member gained a level. A fight stops the music, so it starts over after.
- **Cheat codes:** 0x0718 accepted, 0x0721 rejected. The cheat menu isn't ported.
- **Backdrops and the help menu**, which trek only names so far.
- **Dialogue entities' bodies:** a wanderer that's a dialogue entity has no audio keys in the port. The original looks its profile up by its position in its own list, into the entities' profiles, which isn't settled.
- **Menus the port doesn't have yet:** the theatre's and the Controller Pak's cursors (0x073B), the parchment popups' (0x074D opening, 0x074E as the crafting and healer lists close), and a button widget's A and B (`0x80048E18`, `0x8003FF30`) that isn't identified.

## Music

**One piece of background music plays at a time** (`Bgm.Update`, `0x8000E570`, every frame of trek, a conversation, a cut-scene or combat, but not through a fade, while `ScreenFade.State` isn't 0). The request (`Bgm.Requested`, `0x800D90A2`) is 0 for nothing new, −1 to stop, and otherwise a Borg12. When it's another piece, the old one stops and the new one starts from its beginning, looped, centred, at `trunc(music volume × 255)`; the same piece plays on. There's no crossfade and no ducking. The volume is sent again every frame, and the pause menu's frame (`PauseMenu.Frame`, `0x80048744`) sends it too, so the Options slider acts at once. The pause menu doesn't take requests.

**The request is made** by `Zones.SetMapSet` (`0x80026264`) on every teleport, a door's or a script's, and on every crossing into another chunk, which the original handles as a teleport without a fade (`ConfirmPlayerWithinZone`, `Zones.QueueChunkShift`). So walking out of the chunks with music cuts it, and walking back starts it over. A door's music changes as its fade ends. It asks for −1, then the set's rule:

| Map set | Music |
| --- | --- |
| Overworld | By the chunk: M23, N23–N25, O22–O25, P21–P24 and Q21 0x062C (0x062D without the Expansion Pak); N26–N28, O26–O28, P25–P27, Q25–Q27, R25–R28, S26–S28 and T26–T28 0x05EA (0x05EB); anywhere else none (lists of the chunks' scenes at `0x800CE71C` and `0x800CE738`, ROM `0xCF31C`). |
| Gwernia castle (4) | 0x05FD |
| Erromon (7) | 0x0790 |
| Jundar interiors (11) | 0x061C in A01, I07, I09, K01, K05, K09 and M01 (`Zones.JundarBgmList`, `0x800CE768`) |
| Ehud's underground (12) | 0x05DF |
| Ugarit (13) | 0x061B |
| Chaos Isle (15) | 0x05D7 if the chunk's column has bit 1 clear (columns 1, 5 and 9), else 0x05D5 (3 and 7) |
| Battle arenas (16) | One at random (`Rng.Below(5)`): 0x05A5, 0x05AB, 0x05B4, 0x05B9 or 0x05C3; without the Pak 0x38DE, 0x38DF, 0x05B8 or 0x05BD (`0x800CE778`, `0x800CE784`) |
| The rest | None |

- **The Overworld's** lists don't apply on the title screen (`Zones.Mode` 2, `0x800D90A6`), whose scenery is Overworld chunks.
- **Cut-scenes:** cueing one keeps the music playing (`FreeZoneEngine(1)`), and its stage asks for its own, which plays on if it's the same. Trek asks again as it comes back. Leaving trek any other way (loading, a fight, game over) stops it (`Bgm.Stop(1)`), so it starts over.
- **The title** plays 0x0632 (0x05D9 without the Pak) at volume 0xA5, looped (`Title.StartMusic`, `0x8001B0B0`). The credits play 0x05D9 at the music volume, looped, with the sound and music volumes at 0 under it, so trek's music and sounds go on silently, and stop it at once as they end ([ui](ui.md#the-credits); the port's is in `Game.Credits.cs`).
- **The cinematics** play a sequence per shot (the cinematic records name them), looped, at the louder of the two volumes (`Cinematic.StartShot`, `0x80053E4C`). Before one, the original clears every sound and stops the music, asking for it again after, so it starts over; each shot stops its own sequence as it ends. The port plays them so ([cinematics](cinematics.md#playing)).

## Sound map objects

A map's sound objects (`SoundObject`) are played by `ProcessAudioBubbles` (`0x8004ECA0`) every frame, for all nine loaded chunks, as each chunk's Borg9 loads them (`Audio.StartChunk`, `0x8004E58C`), into 82 slots (`Audio.Init`, `0x8004DF08`). `Audio.AddSound` doesn't check the count, and the 3×3 around the Overworld's H13 has 84, so the original writes past its slots there; the port drops the rest. `StartChunk` skips an object only for the Expansion Pak (flag 0x40 without it, 0x20 with it), and music objects on the title screen. The port's `MapSounds` follows it, and keeps each sound up as a `KeptSound`, which one-shots are too (`Audio.UpdateSound` is one function in the original); `AudioLevels` holds the loudness, volume and pan bytes (map sounds' and bodies', which differ only in clamping) and the pan's geometry (`Audio.Pan`).

- **Inside its radius** of the player (3D, `≤`), a sound's loudness is `1 − (distance from what the camera looks at) / radius`, unclamped (`Camera.Listener+0xC`; `+0` is the eye). A sound with falloff takes it as its fade; a `NoFalloff` one fades in instead, by `delta / 90` a frame up to 1. Its pan, unless it's `NoPan`, is `(f.x × d.z − f.z × d.x) × (1 − loudness)` across the ground, with `f` the camera's facing and `d` the way from the eye to the sound, both normalised: positive to the right (`Audio.Pan`, `0x8004EB1C`), unclamped. The user confirmed in the original that a sound on the left comes from the left speaker.
- **Outside it,** its fade falls by `delta / 90` a frame while it's above 0, and it's kept up meanwhile. At 0 a playing sound stops. Coming back starts it over.
- **Keeping it up** (`Audio.UpdateSound`, `0x8004E900`), every frame: the volume byte is `clamp(trunc(fade × the object's volume × 255), 0, 255)`, then `trunc(that × (Music ? music volume : sound volume))`, and the pan's `clamp(trunc((pan + 1) × 0.5 × 255), 0, 255)`, so a pan of 0 is 127. A playing sound gets them, and a duration of −1. One that isn't playing and needs starting starts, if its flags hold (the gate is checked at each start, not while it plays), looped if it's `Loop`, with a duration of 2000 ms, so one the game stops keeping up dies within two seconds. A `Loop` sound still needs starting after, so one that ends starts again; any other plays once each time the player comes into its radius.
- **`Repeat`:** a countdown falls by `delta` a frame inside the radius. At 0 a repeating sound is cut, needs starting, and waits `(short)trunc((MinWait + Float(MaxWait − MinWait)) × 60)` ticks again (`RandomExtensions.Float`, the main stream). It starts at 0, so a repeating sound starts at once.
- **The initial pan** (`+0x2C`) is 0 in every object.
- **Music objects** play across chunks: when a chunk unloads, its playing music becomes a one-shot of two ticks (`Audio.PersistMusic`), which the next chunk to load the same sequence takes over (`Audio.FindPersistentMusic`), so it plays on without a break. There are 14, in a few Overworld chunks and map sets 6, 8 and 12, and each needs the Expansion Pak.
- **Around them:** the pause menu keeps the sounds going with the player and the camera held still, a delta of 1 a frame, and frees none. A conversation is trek's own frame. Cueing a cut-scene frees every sound but music (`Audio.ClearSounds(1)`), and the stage's chunks load their own. A teleport moves the sounds with the chunks (`Audio.ShiftSounds`) and frees those of chunks left behind.

## Sound effects

One-shots go through `Audio.PlaySound` (`0x8004E150`): a sequence, a pan, a gain, a lifetime in ticks and a delay. Each is a sound object of its own in one of 16 slots, `NoFalloff` and `NoPan`, always there, with a radius of 1000 (`Audio.OneShotRadius`, `0x800D1D14`); with all 16 taken it's dropped. It keeps the sound volume at the call as its own volume and starts its fade at the gain, which rises to 1 over 90 ticks. It waits its delay, starts on the next `ProcessAudioBubbles`, plays once, and holds its slot until its lifetime is up (`lifetime + delay` from the call), when it's stopped. Its volume is `trunc(trunc(fade × the sound volume at the call × 255) × the sound volume now)`, clamped. In the pause menu, lifetimes count its frames. The port's are `GameAudio.Play`'s, each a `KeptSound`.

| Sequence | Lifetime | What |
| --- | --- | --- |
| 0x073B | 30 | Up or down in the pause menu (`PauseMenu.OnUp`, `0x8002FBFC`; `OnDown`), whether or not the cursor moves; the title menu's |
| 0x073D | 30 | A page's own cursor: left and right changing the Party tab's lists (`PartyMenu.ListsSlide`, `0x80031620`) and the save and load screens' saves (`SaveMenu.Left`, `0x8007D428`), up and down on the World tab besides 0x073B, the Options resolution row |
| 0x0740 | 60 | A, whatever the page does with it (`PauseMenu.OnA`, `0x8002FEF0`) |
| 0x0741 | 60 | B or Start (`PauseMenu.OnB`, `0x8002FE5C`; `OnStart`) |
| 0x074D | 120 | The tabs sliding back, L or Z (`PauseMenu.OnTabBack`, `0x8002F7C4`), if the tab changes |
| 0x074E | 120 | The tabs sliding forward, R (`PauseMenu.OnTabForward`, `0x8002F948`) |
| 0x074D, 0x074E | 60 | The menu opening on the System or Party tab, or on the World tab, and closing the other way round (`PauseMenu.SlideOut`, `0x8002E9D0`). An item's details opening with C-down, and closing as it's let go (`PartyMenu.CloseDetails`, `0x80034C18`). A parchment popup opening (`MenuPopup.Open`, `0x800336A0`), and the crafting and healer lists closing with B |
| 0x0739 | 60 | C-left or C-right changing the party member, with more than one (`PartyMenu.ChangeMember`, `0x8003054C`) |
| 0x05E8 | 300, after 60 | The post-battle window, as it's built, when a member has gone up a level |
| 0x05E7 | 300 | A script's experience (`AddExp`), if the speaker is in the party |
| 0x072D | 300 | Gold gained: a script's `AddGold`, gold-only loot, the loot window opening on gold (after 60) |
| 0x072E | 300 | Gold paid (`TakeGold`) |
| 0x0718, 0x0721 | 60 | A cheat code accepted, rejected |
| The dialogue table | 300 | A script's `PlaySound`, by index into 39 sequences at ROM `0xE0960`: 0x0720, 0x0721, 0x05E7, 0x05E8, 0x0587, 0x0588, 0x058A, 0x0722, 0x0723, 0x0576, 0x0577, 0x0578, 0x057A, 0x0634, 0x0635, 0x056C–0x056F, 0x0638, 0x0637, 0x0641, 0x0636, 0x0639, 0x070E–0x0711, 0x071C–0x071F, 0x0724, 0x0725, 0x0727, 0x0728, 0x072D, 0x072E, 0x0749 |
| 0x0729, 0x072A, 0x072B, 0x072C | 180 | A container opening, by its kind 0–3; kinds 4–7 play 0x0729 (`0x800CD9D0`) |
| 0x0724, 0x0725, 0x0727, 0x0728 | 240 | A trapped container exploding, one at random |
| 0x071C–0x071F | 240–420 | Thunder, one at random, as the lightning flashes |

- **The gain:** the menus pass 1. `AddGold` (`0x800105EC`), the loot window's coins, thunder, the containers (`0x8000FF30`, `0x8000FFE4`) and the scripts' sounds (`0x8004F7B8`) pass the sound volume, so they start quieter and swell to full over 90 ticks; at full sound volume it makes no difference.
- **The menus' presses** go through one object, `BigAssMenu` (`0x8002E538`), whose callbacks play the sounds above after passing the press to the page: A, B and Start only with no popup at its level, and up and down not on the Party tab under a popup. Left and right at its level are silent, and so are the volume bars. The Party tab's lists play 0x073B on up and down themselves (`PartyMenu.ListUp`, `0x8007D358`), so it plays twice there at once, louder (confirmed in the emulator), and the port does too, with no popup up. The journal takes up and down itself and scrolls silently (also confirmed). No sound plays for a conversation's choices, a sign, or the loot window's yes and no.
- **Rain** plays 0x071B looped while it rains (not snows) under the sky, at `min(255, trunc(intensity × 1.5 × 255)) × sound volume`, centred. It isn't a one-shot: `ProcessWeather` plays it on the sequencer at volume 0 and sets its volume every frame from the rain's drawn intensity, and stops it once the rain has died away, at once under no sky, and as trek's world goes ([weather](weather.md#the-rain-loop)). The port's is `WeatherSounds`, as is the thunder.

## Bodies' sounds

Each body's footsteps, voice and weapon come from the **audio keys** (`AudioKeys`, the DB table at ROM `0x1FE6780`, loaded by `AudioKeys.Load`, `0x80053000`). `ProcessPlayers` (`0x80012784`) checks them every tick for every body with an actor within 50 of what the camera looks at (`AudioKeys.ProcessPlayer`, `0x80053560`), in trek, conversations, cut-scenes and combat.

- **The table** is three u32 offsets (profiles at 0x18, keys at 0x6F8, key indices at 0xF98) and three u16 counts (220, 276, 560). A profile is 8 bytes: a u16 kind, a u16 count of keys, a u16 start into the key indices, and a zero. A key is 8 bytes: a u16 `frac`, a request byte, a count of sound ids (0, 1 or 4) and four id bytes. A key index is a u16 into the keys.
- **A body's profile** is its entity's position in the entity list (`GetIdIndex`, body `+0x758`). The player and cut-scene actors keep `Player.Reset`'s (`0x80011C90`) default, 153, Alaron's; wanderers and combatants get their entity's. Position 220 (Dracovern) reads past the 220 profiles into key 0, which is kind 0: silent.
- **When a key fires.** Each tick the actor steps (its frame runs 1 to the animation's length, the core's `ActorStateMachine.Clock`), and when its frame is 1 the body latches its request. A key fires on the tick its request is the latched one and the frame equals `(u16)(int)(frac / 65500f × length)` in single precision, or 1 if that's 0 (65500.0 at `0x800D205C`). So a frac of 65500 is the last frame. It fires again only if the frame holds on the next tick. `AudioKeys.PlayEntityKey` (`0x800532BC`) plays the first key that matches: one of its ids at random (`Rng.Below`, the main stream), into the 126 voice sequences (`0x800E02A8`); a key with no ids plays the weapon's sound instead.
- **The kind** (0 for 3 profiles, 1 for 6, 2 for 194, 3 for 17) sets the footsteps' loudness, × 0.5, 0.75 or 1.0 (doubles at `0x800D2070`, `0x800D2080`), and the footsteps' set: kinds 1 and 2 the people's, 3 the heavy bodies'. Kind 0 plays nothing at all.
- **Footsteps** (`AudioKeys.PlaySurfaceSteps`, `0x800530DC`) come from the step sets (`0x800DFFE8`, ROM `0xE0BE8`): 26 surfaces × sneak, walk and run, a byte each, indexing 48 footstep keys (`0x800DFE60`, ROM `0xE0A60`, 8 bytes each as above, entry 0 empty), all at frac 65500 with four step ids, for request 2, 3 or 25. Step ids index 96 sequences (`0x800E0128`, ROM `0xE0D28`). The surface is the last one the body's sphere touched that's 3 or more (body `+0x102`, updated before anything else each tick), so walls and leaving the ground don't change it. For people: sand and default 0x067E–0x0681, snow 0x068A–0x068D, grass 0x0649–0x064C, mud 0x0679–0x067C, sizzling 0x0682–0x0685, cave 0x0644–0x0647, wood 0x069F–0x06A2, echoing wood 0x06A3–0x06A6, stone 0x068E–0x0691, echoing stone 0x0692–0x0695, the great echoing stone 0x0696–0x0699, water 0x069B–0x069E, and sneaking 0x0686–0x0689 on all but water. Heavy bodies don't sneak: default, grass and sand 0x0657–0x065A, snow 0x0660–0x0663, mud 0x065C–0x065F, cave and the echoing stones 0x0668–0x066B, wood 0x066D–0x0670, echoing wood 0x06A7–0x06AA, stone 0x0664–0x0667, and sizzling and water as people's. The see-through surfaces sound as their plain ones.
- **The weapon's sound** (`AudioKeys.WeaponSound`, `0x800876E8`) is only in combat (`Combat.IndexOfBody` searches only in game mode 2): the wielded weapon's row of a table by `WeaponsWithModels` position (`0x800D44D0`, ROM `0xD50D0`, 8 bytes a row, its first two u16s alike), a voice id, such as 2 (0x06AD) for axes and 107 for staves. No weapon, or one without a model, is silent. **A spell's cast** (`AudioKeys.PlaySpellCast`, `0x800534BC`) plays its sequence from 59 by spell position (`0x800E04A0`, ROM `0xE10A0`) on the first tick the caster's request is 5 after combat's `Player.SetCastSound`.
- **Loudness** (`Audio.VolumeAndPan`, `0x8004EB90`): `(u8)(int)((1 − distance from what the camera looks at / 15) × 255)`, with nothing played at 0. It isn't clamped: past 15 the int's low byte wraps, so a body from about 15.1 to 50 away is loud again, a sawtooth falling off to 1 at 30 (confirmed in the emulator). Footsteps take the kind's factor, truncated, and skip at 0; then everything plays at `(u8)(int)(byte × sound volume)`, once. The pan is map sounds', with `1 − loudness` being `distance / 15`.
- **In the port,** `AudioKeys` wraps each body's animation (`AudioKeys.Sounding`), so the keys are checked on every step of it: the player's, the wanderers' and a cut-scene's actors', which have Alaron's keys as the original's do.
- **Two slots a body.** `AllocPlayerAudio` (`0x80012610`) alternates between them, stopping what was in the one it takes, and plays once, without a time limit, centred where the body was: the sound doesn't follow it.

## Volumes

- **The sound and music volumes** are floats, 1.0 and 0.65 in a new game (`Settings.SoundVolume`, `Settings.MusicVolume`), which scale the byte each sequence is played at.
- **One-shots take the sound volume twice:** `Audio.PlaySound` stores it as the sound's own volume and `Audio.UpdateSound` multiplies by it again; some pass it as their gain too, a starting fade. [User-checks](../user-checks.md) asks whether that's audible.
- **Nothing has priority.** Every sequence is played with priority 0, so a note finding all 32 voices busy is dropped, and a 33rd sequence doesn't play.
