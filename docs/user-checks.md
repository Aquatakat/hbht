# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–13 are played. Left:

- **Sense Aura at level 9** (setup 14, no log needed): the pak's slot A, `out/parity/saves/setup14.json`, setup 1's party and Tomb Rats with Alaron's Sense Aura at 9 (Wizard 10). Cast it at a Tomb Rat and screenshot the popup. Level 9 adds its last line, "Total Armor Protection %d", which the port works out as a blow from the front would (`Battle.Protection`: the sheet's, the armor's, Warrior ÷ 5, potions, and the shield).

If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).
