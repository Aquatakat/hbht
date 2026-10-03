# For the user to check

Things only the user can settle, in the emulator or by reading the ROM. Each links to where the port's reading is written up. When one is settled, write what was found in that doc and take it off this list.

## In the emulator

- **The lighthouse keeper's key** (Borg13 890 node 13): whether the keeper animates as he hands it over. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: whether the scene ends at his line, hangs or plays on. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Shop prices.** A few prices, buying and selling, with a checkpoint's party, to compare with the port's barter factor. See [items](systems/items.md#shops).
- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row, when a save allows it. See [ui](systems/ui.md#the-party-tabs-actions).
- **Potions across a save.** From the code, the original's saves drop the potions working on the party, which the port's keep. To check: drink a potion with a timer (Strength, say), save, load and see whether the stat is still raised. See [saves](systems/saves.md#the-format).
- **Sholeh in Ugarit after she joins** (`aidyn:dialogue/0x04AD`): from the data, nothing hides her party once she's joined, so leaving the area and coming back should find her there, and talking to her should add a second Sholeh. If you're ever there with her freshly recruited, whether she's there again and what talking does. See [wanderers](systems/wanderers.md#joining).
- **A kept member's return.** Godric, Abrecan, Arturo, Becan and Keelin come back with their experience and stats but nothing worn (it went to the inventory when they left); everyone else comes back as new, at their starting level. If a playthrough ever dismisses and rejoins one of each, whether that's what happens. See [characters](systems/characters.md#joining-and-leaving-m2-session-5d).


## In the ROM

- **The trap's menu** (`WidgetChoiceDia::Update`): the question above Disarm and Exit, and the cursor's option pulsing grey to white, the scroll menu's defaults. The user thinks it looks right. See [map-objects](systems/map-objects.md#popups).
