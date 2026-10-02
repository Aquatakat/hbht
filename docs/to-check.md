# For the user to check

Things only the user can settle, in the emulator or by reading the ROM. Each links to where the port's reading is written up. When one is settled, write what was found in that doc and take it off this list.

## In the emulator

- **The lighthouse keeper's key** (Borg13 890 node 13): whether the keeper animates as he hands it over. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: whether the scene ends at his line, hangs or plays on. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Shop prices.** A few prices, buying and selling, with a checkpoint's party, to compare with the port's barter factor. See [items](systems/items.md#shops).
- **The interiors' camera and camera objects** (M2 session 4c), against footage: Gwernia's house A from the door (the camera should cut to a fixed point near the ceiling and turn to follow Alaron), walking across Gwernia's inn (four floor-triggered cameras easing from point to point over a second), and a Gwernia castle corridor with no camera object, walking into a corner (the bare interiors' camera, kept 0.25 off the walls). See [cameras](systems/cameras.md#the-interiors-camera-cameramode1).
- **A door into an interior with no camera point,** where the port puts the camera 4 in front of Alaron looking back at him, as `TeleportPlayer` reads. See [cameras](systems/cameras.md#which-mode-m2-session-4c).
- **Loading a save made in a house from the title screen:** whether the camera follows as indoors, or as outdoors until the next door (the title's load may not teleport). See [cameras](systems/cameras.md#which-mode-m2-session-4c).
- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row, when a save allows it. See [ui](systems/ui.md#the-party-tabs-actions).


## In the ROM

- **The trap's menu** (`WidgetChoiceDia::Update`): the question above Disarm and Exit, and the cursor's option pulsing grey to white, the scroll menu's defaults. The user thinks it looks right. See [map-objects](systems/map-objects.md#popups).
