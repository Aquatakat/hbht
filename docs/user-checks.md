# Checks for the user

Things only the user can settle, by playing the original in the emulator, by listening, or in full-game testing. Each says what to do and what to look for, and links to where the port's reading is written up. When one is settled, the finding goes in that doc and the item comes off this list. A check far into the story needn't be played to: a save can be written into the pak beside the ROM, at the place with the flags and party it needs (`hbht save to-pak`, [saves](systems/saves.md#the-originals-save)).

## Setting up a check

The user needn't play to a check: a save is written into the Controller Pak beside 1.0's ROM, at the place, flags and party the check needs, and the user loads it from the title in `tools/ares.ps1 v10`. Every save so far has loaded. The format and the commands are in [saves](systems/saves.md#making-and-checking-saves).

1. **Start from a save of the port's.** `hbht save from-pak <rom> <slot> --pak "roms/<1.0 rom basename> - story saves.pak" --out <save>` takes one of the four story saves (A before the final encounter, B before Shamsuk's Tower, C before Rooughah, D in Terminor, each with the [checkpoint](reference-rig.md#checkpoint-library) captured at the same place); `hbht save from <checkpoint>` or `hbht save new` do too. Pick the one nearest the check in the story, and say which flags were changed to get there.
2. **Find what the check needs.** The map object that starts the script or holds the place: search the installed base plugin's `records/maps/*.json` for the script's ID (its stage, position, radius and enabling flag), and `records/mapsets.json` for the chunk that places the map. `hbht flag <n>` says what sets, clears and reads a flag, and what a derived one is made of. `hbht dialogue <rom> <number>` dumps the script, for the path to the node that matters.
3. **Edit it** with the scripts in `tools/saves/`, each run as `dotnet run tools/saves/<script>.cs -- …`:
   - `party.cs <in> <out> --remove <entity hex,…> --add <entity hex,…>` changes the party by the scripts' own leave and join, so the party's flags follow.
   - `edit.cs <in> <out> --at <map set> <chunk> <x> <z> [<y>] --face <x> <z> --set <n,…> --clear <n,…> --give <item ID> <n> --label <text>` stands the player on the floor under a point of the chunk (swept down on the port's collision; give `y` indoors, or it lands on the roof), facing another point, sets or clears plain flags, and adds items to the inventory.
   - `flags.cs <save> <n,…>` prints flags, derived ones included, as the save loads: check the object's enabling flag holds, and that what should be hidden is.
4. **Write it**: `hbht save to-pak <rom> <save> A`. Close ares first, since it writes its pak back as it closes. Slot A is the checks'; the command keeps the pak as it was first (`<pak>.<time>.bak`), and the story saves stay untouched in `- story saves.pak`, which the tests read.
5. **For the runtime**, the same JSON loads with `tools/godot.ps1 --path src/HBHT.Game -- --save <path>`. Keep it in the gitignored `out/` and give the user the path: what Claude writes under `%LOCALAPPDATA%` lands in the Claude app's private copy, not the data folder the user's runtime reads. The saves made for the checks so far are in `out/user-checks/`.
6. **When the user reports back,** the finding goes in the system's doc and the item comes off this list. The port's fixes follow [ADR 0018](adr/0018-the-port-fixes-bugs-and-sets-enhancements.md) and, for the game's data, [ADR 0029](adr/0029-content-fixes-wait-for-a-bugfix-mod.md).

## Sound

- **The sound volume at half:** by the code, the menu's sounds take the sound volume twice and the containers' three times. At half sound volume, are they a quarter as loud? The port plays them so. [audio](systems/audio.md#volumes)
- **The post-battle windows in the runtime**, after a battle won in the forest that gains Alaron a level (as in the prologue): are the training and loot windows silent but for the menu's own sounds, with no ambient sound or rain under them, and does the level-up jingle (0x05E8) play about a second after the scroll starts unrolling, as in the original? [ui](systems/ui.md#after-a-battle)
