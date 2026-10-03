# For the user to check

Things only the user can settle, in the emulator or by reading the ROM. Each links to where the port's reading is written up. When one is settled, write what was found in that doc and take it off this list.

## In the emulator

- **The lighthouse keeper's key** (Borg13 890 node 13): whether the keeper animates as he hands it over. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: whether the scene ends at his line, hangs or plays on. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Shop prices.** A few prices, buying and selling, with a checkpoint's party, to compare with the port's barter factor. See [items](systems/items.md#shops).
- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row, when a save allows it. See [ui](systems/ui.md#the-party-tabs-actions).
- **Sholeh in Ugarit after she joins** (`aidyn:dialogue/0x04AD`), in full-game testing: whether she's there again once she's joined, and what keeps her away when the party has no free place, which the data doesn't show. See [wanderers](systems/wanderers.md#joining).

- **Keelin's scene from her dialogue object** (`aidyn:voxel/0x1436/12`, 1.2 from her body toward +X and +Z): walk up against her, then turn about 45° or more to the right (screen right, as you face her from the room) without moving off, and press A. In the port that reaches her object, while facing her within about 30° reaches her body (simulated on Terminor's collision, Oct 3, 2026). Whether it plays with its cameras on her and Alaron, rather than the scene her body starts, which reads another scene's reference points and crashes when she isn't recruited. See [scripts](systems/scripts.md#the-world-it-plays-in).

## In the ROM

- **The trap's menu** (`WidgetChoiceDia::Update`): the question above Disarm and Exit, and the cursor's option pulsing grey to white, the scroll menu's defaults. The user thinks it looks right. See [map-objects](systems/map-objects.md#popups).
