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
- **Far bodies' footsteps:** a body's loudness isn't clamped, so by the machine code one 15.1 to 30 from where the camera looks plays loud again, fading to nothing at 30, and again to 45 ([audio](systems/audio.md#bodies-sounds)). The port does the same. Stand still in a town with wanderers walking 15 to 30 away (Gwernia's open ground, M07) and listen for their footsteps loud, against the port.
- **The Party tab's cursor:** up and down there may play the cursor sound twice at once, once from the menu and once from the list, which would sound louder than on the World or System tab. The port plays it once. See [audio](systems/audio.md#sound-effects).

## In the ROM

- **The trap's menu** (`WidgetChoiceDia::Update`): the question above Disarm and Exit, and the cursor's option pulsing grey to white, the scroll menu's defaults. The user thinks it looks right. See [map-objects](systems/map-objects.md#popups).
