# For the user to check

Things only the user can settle, in the emulator or by reading the ROM. Each links to where the port's reading is written up. When one is settled, write what was found in that doc and take it off this list.

## In the emulator

- **The lighthouse keeper's key** (Borg13 890 node 13): whether the keeper animates as he hands it over. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: whether the scene ends at his line, hangs or plays on. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **A selling price.** Buying prices are confirmed; one sell question would confirm selling's, such as the Sabre at STAI01's shop in the "Before final encounter" checkpoint, which the port has as 135. See [items](systems/items.md#shops).
- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row, when a save allows it. See [ui](systems/ui.md#the-party-tabs-actions).
- **Sholeh in Ugarit after she joins** (`aidyn:dialogue/0x04AD`), in full-game testing: whether she's there again once she's joined, and what keeps her away when the party has no free place, which the data doesn't show. See [wanderers](systems/wanderers.md#joining).
- **The menus' held-stick repeat** in a list, such as the Party tab's inventory. The original repeats after 30 polls and then every 5 (`BaseWidget::Control`), but how often it polls isn't settled: 1 s and 0.17 s at a 30 fps frame, half that at 60 polls a second. The port uses 0.4 s and 0.1 s. Holding the stick down a long list and counting the rows it passes in a few seconds would settle it. The volume bars step every tick instead, which the user timed. See [ui](systems/ui.md#the-pause-menu).

## In the ROM

- **The trap's menu** (`WidgetChoiceDia::Update`): the question above Disarm and Exit, and the cursor's option pulsing grey to white, the scroll menu's defaults. The user thinks it looks right. See [map-objects](systems/map-objects.md#popups).
