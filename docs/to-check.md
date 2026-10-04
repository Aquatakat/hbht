# For the user to check

Things only the user can settle, in the emulator or by reading the ROM. Each links to where the port's reading is written up. When one is settled, write what was found in that doc and take it off this list.

## In the emulator

- **The lighthouse keeper's key** (Borg13 890 node 13): whether the keeper animates as he hands it over. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: whether the scene ends at his line, hangs or plays on. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **A selling price.** Buying prices are confirmed; one sell question would confirm selling's, such as the Sabre at STAI01's shop in the "Before final encounter" checkpoint, which the port has as 135. See [items](systems/items.md#shops).
- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row, when a save allows it. See [ui](systems/ui.md#the-party-tabs-actions).
- **Sholeh in Ugarit after she joins** (`aidyn:dialogue/0x04AD`), in full-game testing: whether she's there again once she's joined, and what keeps her away when the party has no free place, which the data doesn't show. See [wanderers](systems/wanderers.md#joining).
- **The party's portraits when standing still.** 1.0's code raises them only with B, and when trek starts (a new game, loading, after a cut-scene or battle); moving lowers them. Do they ever come up by standing still for a while? See [ui](systems/ui.md#the-partys-portraits).
- **The sundial in a cave.** The code hides its moon and sun on walking from outdoors into a cave's sky, such as from Gwernia into the castle, and shows them again when trek starts, as after loading, which would explain the capture inside the castle showing them. Walking in from outside, do they go? See [ui](systems/ui.md#the-sundial).
- **The journal after loading:** which entry is purple in the journal just after loading a save. The machine code suggests the highest-numbered, as loading turns every entry on again; the notice of a new entry doesn't show, which the user confirmed. See [ui](systems/ui.md#the-journal).

## In the ROM

- **The trap's menu** (`WidgetChoiceDia::Update`): the question above Disarm and Exit, and the cursor's option pulsing grey to white, the scroll menu's defaults. The user thinks it looks right. See [map-objects](systems/map-objects.md#popups).
