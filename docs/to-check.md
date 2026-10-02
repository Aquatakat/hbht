# For the user to check

Things only the user can settle, in the emulator or by reading the ROM. Each links to where the port's reading is written up. When one is settled, write what was found in that doc and take it off this list.

## In the emulator

- **The lighthouse keeper's key** (Borg13 890 node 13): whether the keeper animates as he hands it over. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: whether the scene ends at his line, hangs or plays on. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Shop prices.** A few prices, buying and selling, with a checkpoint's party, to compare with the port's barter factor. See [items](systems/items.md#shops).
- **Enemies chasing.** Near an enemy outside Gwernia, walking toward it: that it notices him walking at about 13 in front of it and 7 behind (it measures 10 from 3 ahead of itself), and running half as far again; that it runs at him; and that if he runs off past its party's radius it stands about 5 seconds before going back. Standing still or sneaking it notices him at a bit over half the walking distance, and with a party member's Stealth at 10, a fifth of that. See [wanderers](systems/wanderers.md#wandering-noticing-and-chasing-5b).
- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row, when a save allows it. See [ui](systems/ui.md#the-party-tabs-actions).


## In the ROM

- **The trap's menu** (`WidgetChoiceDia::Update`): the question above Disarm and Exit, and the cursor's option pulsing grey to white, the scroll menu's defaults. The user thinks it looks right. See [map-objects](systems/map-objects.md#popups).
