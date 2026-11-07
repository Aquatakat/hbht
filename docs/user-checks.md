# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### The renderers (ADR 0038)

Choose Mobile, then Compatibility, on the Video page, restart each time, and play a while: a town (Port Saiid's streets, Gwernia), the Overworld's roads and a battle. Look for roads and shadows flickering against the ground under them (Mobile has no depth prepass), shadows under Compatibility (a flat square there, which can sink into rising ground), and anything darker, paler or missing against Forward+. Also try the Intel graphics under Compatibility: Windows' Settings › System › Display › Graphics, the game's Godot executable set to Power saving ([rendering](systems/rendering.md#the-renderers)).

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–14 are played, and none is waiting. If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).
