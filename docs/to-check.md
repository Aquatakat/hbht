# For the user to check

Things only the user can settle, in the emulator or by reading the ROM. Each links to where the port's reading is written up. When one is settled, write what was found in that doc and take it off this list.

## In the emulator

- **The lighthouse keeper's key** (Borg13 890 node 13): whether the keeper animates as he hands it over. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: whether the scene ends at his line, hangs or plays on. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Shop prices.** A few prices, buying and selling, with a checkpoint's party, to compare with the port's barter factor. See [items](systems/items.md#shops).
- **A tracking camera's aim while Alaron runs** (Gwernia's houses and inn): whether he runs ahead of the middle of the screen and the camera catches up when he stops, or he stays in the middle; and whether the view jolts as one of the inn's cameras hands over to the next. The port's aim lags about 1.4 behind him and snaps onto him at a hand-off, as the code reads if the move carries on every frame. See [cameras](systems/cameras.md#camera-objects-processvoxelcamera).
- **The bare interiors' camera** in a Gwernia castle corridor with no camera object, walking into a corner: it should hang 2 to 4 back and stop 0.25 off the walls. See [cameras](systems/cameras.md#the-interiors-camera-cameramode1).
- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row, when a save allows it. See [ui](systems/ui.md#the-party-tabs-actions).


## In the ROM

- **The trap's menu** (`WidgetChoiceDia::Update`): the question above Disarm and Exit, and the cursor's option pulsing grey to white, the scroll menu's defaults. The user thinks it looks right. See [map-objects](systems/map-objects.md#popups).
