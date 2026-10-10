# Audio (roadmap 1D)

What plays which sound when, and how the port plays them. [The audio format](../formats/audio.md) has the samples, the sequences and the driver. Settled from 1.0's machine code; the names are in the symbol map.

## What's ported

- **The player** is `AudioEngine` in the core's `Audio`: the original's sequencer and voice manager, stepped on the original's 1000 µs clock at any output rate, mixing 32 voices to 16-bit stereo. It plays a `Sequence`, which `Sequence.Load` builds from a sequence record, its `.dcm` events and its samples' WAVs. It's tested against hand-built sequences for timing, tempo, pitch, volume, pan, loops, note-offs and the 32-sequence limit.
- **It differs from the original** in three small ways. The sequencer's changes reach the voices at once rather than on the voice manager's next call, up to a millisecond sooner. libultra's four-tap resampler is approximated with cubic interpolation. And where the original reads past a sample's end (one instrument's loop runs past its sample), the port plays silence.
- **`aidyn audio`** renders every sequence to WAV with it, for listening.
- **Not ported yet:** everything below. The runtime plays nothing yet: the cues that would play a sound are logged.

## Music

**One piece of background music plays at a time** (`Bgm.Update`, `0x8000E570`, every frame). When the requested piece changes, the old one stops and the new one starts from its beginning, looped, at the music volume, centred. There's no crossfade. The volume is sent again every frame, so the Options slider acts at once. The request is set as a map set is entered (`Zones.SetMapSet`, `0x80026264`):

| Map set | Music |
| --- | --- |
| Overworld | By the chunk entered: M23–Q21's thirteen chunks 0x062C (0x062D without the Expansion Pak), N26–T28's twenty-two 0x05EA (0x05EB); anywhere else none, until a music map object plays (lists at `0x800CE71C` and `0x800CE738`, by the chunk's scene). |
| Gwernia castle (4) | 0x05FD |
| Erromon (7) | 0x0790 |
| Jundar interiors (11) | 0x061C in two chunks |
| Ehud's underground (12) | 0x05DF |
| Ugarit (13) | 0x061B |
| Chaos Isle (15) | 0x05D7 if the chunk's column has bit 2 clear, else 0x05D5 |
| Battle arenas (16) | One at random: 0x05A5, 0x05AB, 0x05B4, 0x05B9 or 0x05C3; without the Pak 0x38DE, 0x38DF, 0x05B8 or 0x05BD (`0x800CE778`, `0x800CE784`) |
| The rest | None |

- **The title** plays 0x0632 (0x05D9 without the Pak) at volume 0xA5, looped (`Title.StartMusic`, `0x8001B0B0`). The credits play 0x05D9 at the music volume.
- **The cinematics** play a sequence per shot, looped, at the louder of the two volumes (`Cinematic.StartShot`, `0x80053E4C`): the opening 0x0601, 0x0608, 0x060B, 0x060C, 0x060E, 0x0610, 0x0612, 0x0613, 0x0615, 0x0616, 0x0617, 0x0619 and 0x061A; the Shadow's 0x061D, 0x061E, 0x0621 and 0x0622; Roog's 0x05DA, 0x05DB and 0x05DC; the ending's 0x05ED to 0x05F2. The importer already reads them with the cinematics.

## Sound map objects

A map's sound objects (`SoundObject`; 1,038 of them) are played by `ProcessAudioBubbles` (`0x8004ECA0`) every frame, up to 80 at once, as a chunk loads them (`Audio.StartChunk`, `0x8004E58C`).

- **Inside its radius** (from the player), a sound plays. Its loudness is `1 − (distance from the camera's eye) / radius`, not clamped, unless it's `NoFalloff`, which instead fades in to full over 90 ticks. Its pan is `−side × (1 − loudness)`, where `side` is the sine of the angle between the camera's facing and the sound, unless it's `NoPan`. Each frame the sequence's volume is `fade × the object's volume × 255 × (Music ? music volume : sound volume)` and its pan `(pan + 1) × 0.5 × 255` (`Audio.UpdateSound`, `0x8004E900`).
- **Outside it,** a sound fades out over 90 ticks if it had faded in, and then stops; one with falloff is already near silent at the edge, so it stops at once. Coming back starts it again from the beginning.
- **The flags** (`SoundFlags`): `Loop` loops the sequence, and one that ends or couldn't start is tried again next frame. `Repeat` starts the sound again after a random wait between `MinWait` and `MaxWait` seconds from each start (`RandomExtensions.Float`), cutting it off if it's still playing; it starts at once on entering. `NoFalloff`, `NoPan` as above. `Music` uses the music volume, and in combat music objects aren't loaded. A sound's flag C gate is checked only as it starts. The data's flags: 0 (4 objects), Loop (314), Repeat (375), NoFalloff (26), Loop and NoFalloff (210), Repeat and NoFalloff (62), Loop, NoFalloff and NoPan (34), and the 14 music objects with Music (21, 25, 29).
- **Music objects** play across chunks: when a chunk unloads, its music is handed to the next chunk that loads one with the same sequence (`Audio.PersistMusic`, `Audio.FindPersistentMusic`), so it plays on without a break; otherwise it's freed two ticks later. Their sequences are 0x05C9, 0x05CB, 0x05CE, 0x05D2, 0x05DF, 0x05E5, 0x05F5, 0x05F6, 0x05F7, 0x0629, 0x062B and 0x078F, in the Overworld's H08, M07, I13, P17, A06 and C15 and in map sets 6, 8 and 12. Each needs the Expansion Pak (the object's own flag 0x40).
- **The first start** passes a duration of 2000 ms, and every later frame −1, so a sound the game stops updating dies within two seconds.

## Sound effects

One-shots go through `Audio.PlaySound` (`0x8004E150`): a sequence, a pan, a gain, a lifetime in ticks after which it's stopped, and a delay. Up to 16 play at once. They're centred and don't fall off.

| Sequence | Lifetime | What |
| --- | --- | --- |
| 0x073B | 30 | The pause menu's cursor, and the theatre and Controller Pak menus' |
| 0x073D | 30 | A page's own cursor, such as Options' graphics page |
| 0x0740 | 60 | A |
| 0x0741 | 60 | B |
| 0x074D, 0x074E | 60–120 | The pause menu's tabs sliding back and forward; an item's details opening and closing |
| 0x0739 | 60 | Changing the party member |
| 0x05E8 | 300, after 60 | The level-up page, when a member has gone up a level |
| 0x05E7 | 300 | A script's experience (`AddExp`), if the speaker is in the party |
| 0x072D | 300 | Gold gained: a script's `AddGold`, gold-only loot, the barter menu (after 60) |
| 0x072E | 300 | Gold paid (`TakeGold`) |
| 0x0718, 0x0721 | 60 | A cheat code accepted, rejected |
| The dialogue table | 300 | A script's `PlaySound`, by index into 39 sequences at ROM `0xE0960`: 0x0720, 0x0721, 0x05E7, 0x05E8, 0x0587, 0x0588, 0x058A, 0x0722, 0x0723, 0x0576, 0x0577, 0x0578, 0x057A, 0x0634, 0x0635, 0x056C–0x056F, 0x0638, 0x0637, 0x0641, 0x0636, 0x0639, 0x070E–0x0711, 0x071C–0x071F, 0x0724, 0x0725, 0x0727, 0x0728, 0x072D, 0x072E, 0x0749 |
| 0x0729, 0x072A, 0x072B, 0x072C | 180 | A container opening, by its kind 0–3; kinds 4–7 play 0x0729 (`0x800CD9D0`) |
| 0x0724, 0x0725, 0x0727, 0x0728 | 240 | A trapped container exploding, one at random |
| 0x071C–0x071F | 240–420 | Thunder, one at random, as the lightning flashes |

- **Rain** plays 0x071B looped while it rains (not snows), at `min(255, intensity × 1.5 × 255) × sound volume`, centred.
- The docs once had a tab's sound as 0x07D4, which is a texture.

## Bodies' sounds

Each body's footsteps, voice and weapon come from the **audio keys** (`AudioKeys`, the DB table at ROM `0x1FE6780`, loaded by `AudioKeys.Load`, `0x80053000`), checked every tick for every body (`AudioKeys.ProcessPlayer`, `0x80053560`). A body's sounds are heard within 15 of the camera's eye: loudness `1 − distance / 15`, panned as map sounds are, and nothing at 0 (`Audio.VolumeAndPan`, `0x8004EB90`). They play at `loudness × sound volume`, in two slots a body alternates between.

- **The table** is a header of three offsets and three counts, then 220 profiles, 276 keys and 560 key indices. A profile, one per entry of the entity list, is a kind and a run of key indices. A key is a point in an animation, `frac / 65500` of the way through, an animation request, and 0, 1 or 4 sound ids (4: one at random). It fires on the tick the body's animation, started by that request, reaches that frame (at least frame 1).
- **The kind** sets the footsteps' loudness: 0 is silent with no keys at all (the bats and the squid), 1 half (beasts: the lava hound, wyvern, scorpion, bear, boar and rat), 2 three quarters (194 humanoids), 3 full (ogres, cyclopes, minotaurs, Chaos soldiers, the giant skeleton).
- **The keys** are voices: request 7 (hurt) and 8 (attack) choose from four, 9 (dying) has one, and 10 two. Requests 5 (casting) and 12–18 (attacks) play the weapon's sound instead: the equipped weapon's entry in a table by weapon model (`0x800D44D0`), such as 0x06AD for staves and great swords and 0x0712 unarmed. A spell's cast plays its own, from 59 by spell (`0x800E04A0`), as the caster's request turns 5. Key ids are indices into 126 sequences (`0x800E02A8`).
- **Footsteps** fire on the last frame of the walk, run and sneak animations (requests 2, 3, 25), one of four sequences by the surface last walked on that isn't a wall. The kind picks the set (`0x800DFFE8`, 26 surfaces × sneak, walk, run). For people: sand and default 0x067E–0x0681, snow 0x068A–0x068D, grass 0x0649–0x064C, mud 0x0679–0x067C, sizzling 0x0682–0x0685, cave 0x0644–0x0647, wood 0x069F–0x06A2, echoing wood 0x06A3–0x06A6, stone 0x068E–0x0691, echoing stone 0x0692–0x0695, the great echoing stone 0x0696–0x0699, water 0x069B–0x069E, and sneaking 0x0686–0x0689 on all but water. The see-through surfaces sound as their plain ones. Heavy bodies have their own set, such as grass 0x0657–0x065A. Step ids index 96 sequences (`0x800E0128`).

## Volumes

- **The sound and music volumes** are floats, 1.0 and 0.65 in a new game (`Settings.SoundVolume`, `Settings.MusicVolume`), which scale the byte each sequence is played at.
- **One-shots take the sound volume twice:** `Audio.PlaySound` stores it as the sound's own volume and `Audio.UpdateSound` multiplies by it again, and the containers', thunder's and scripts' gold sounds pass it as their gain too, three times. [To-check](../to-check.md) asks whether that's audible.
- **Nothing has priority.** Every sequence is played with priority 0, so a note finding all 32 voices busy is dropped, and a 33rd sequence doesn't play.
