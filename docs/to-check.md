# For the user to check

Things only the user can settle, in the emulator or by reading the ROM. Each links to where the port's reading is written up. When one is settled, write what was found in that doc and take it off this list.

## In the emulator

- **The lighthouse keeper's key** (Borg13 890 node 13): whether the keeper animates as he hands it over. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: whether the scene ends at his line, hangs or plays on. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **A selling price.** Buying prices are confirmed; one sell question would confirm selling's, such as the Sabre at STAI01's shop in the "Before final encounter" checkpoint, which the port has as 135. See [items](systems/items.md#shops).
- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row, when a save allows it. See [ui](systems/ui.md#the-party-tabs-actions).
- **Sholeh in Ugarit after she joins** (`aidyn:dialogue/0x04AD`), in full-game testing: whether she's there again once she's joined, and what keeps her away when the party has no free place, which the data doesn't show. See [wanderers](systems/wanderers.md#joining).
- **The journal after loading:** which entry is purple in the journal just after loading a save. The machine code suggests the highest-numbered, as loading turns every entry on again; the notice of a new entry doesn't show, which the user confirmed. See [ui](systems/ui.md#the-journal).

- **Wrapped pitches in music:** 0x05DD and 0x05DE (and SFX such as 0x0759 and 0x075F) have notes whose pitch, above 65,535 Hz, the original keeps in 16 bits, so they'd play far too low. The port does the same. Listen for a low growl or rumble in those against `aidyn audio`'s render. See [audio](formats/audio.md#the-event-stream).
- **The sound volume at half:** the menu's sounds take the sound volume twice and the containers' three times, by the machine code. Whether half sound volume makes them quarter loud is a check against the port, which now plays them so. See [audio](systems/audio.md#volumes).
- **Which way sounds pan:** a sound to the camera's right should come from the right speaker; the formula says right is positive, and the port plays it so, but nobody has listened. Any map sound with falloff, such as a fountain, turned side on. See [audio](systems/audio.md#sound-map-objects).

- **Rain after loading:** loading a save made in the rain outdoors (the "Before Rooughah" one is in an interior, so step outside first and save there): whether the rain is there at once, or dies away and sets in again over about ten seconds, as the machine code suggests. The port keeps it falling. See [weather](systems/weather.md#differences-from-the-original).
- **Rain beside the original's:** next time it rains outdoors in the emulator, a screenshot or two of the rain at the follow camera, to set beside the port's (`--weather rain,<intensity>`, which the F3 overlay's drop count helps match). The port has only a few small, faint streaks on screen at once and more ripples on the ground, as the code says; whether the original looks as sparse, and whether ripples show on the ground in Gwernia but not under its roofs. Snow too, if a snowy terrain (5) comes up. See [weather](systems/weather.md#rain-and-snow).
- **Rain in a cut-scene:** whether rain or snow falls during a cut-scene staged outdoors while it's raining, as the machine code suggests (the dialogue mode's frames run `ProcessWeather` too). The port's cut-scenes have none yet. See [weather](systems/weather.md#to-port).
- **Point lights on Alaron:** standing near a fire indoors, such as the one in the Gwernia interiors' A01 (the port's `--set 5 --chunk A01 --at 11,10.5`), he should be lit warm on the side facing it and flicker with it, more the nearer he is, out to 5, while the townsman standing beside him isn't. Then the 24 lights in EhudUnder (C11–C19, E13, E15, G11, G17) that shine from the camera's eye: bodies there should be lit purple, and gold or red, on the side facing the camera, whichever way it looks. See [rendering](systems/rendering.md#point-lights).
- **A trapped container exploding** at night or indoors: Alaron should flash orange and then glow red to yellow for about ten seconds if he's within 5 of it. See [rendering](systems/rendering.md#point-lights).
- **Particles beside the original's:** a creature that emits while it stands or walks, such as a Fire Elemental, Air Elemental or Lava Hound wandering in trek, or Mago (dialogue entity 0x1413) in a scene: the particles' size, colour, how fast they spawn and drift, and that they fade as the port's do. The editor shows Oriana's orb with `--record aidyn:actor/0x2BD7 --drive 5`. See [particles](formats/particles.md#the-simulation).

- **Where the map area north of Ugarit unlocks:** It currently doesn't unlock while travelling through it in the port.

- **The trap's menu** (`WidgetChoiceDia::Update`): the question above Disarm and Exit, and the cursor's option pulsing grey to white, the scroll menu's defaults. The user thinks it looks right. See [map-objects](systems/map-objects.md#popups).
