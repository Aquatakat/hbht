# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### Combat Turns: Start Early's pacing (piece A)

Under Combat Turns: Start Early, in a battle with several enemies (the rats at F09, bandits, the Chaos casters), how the beats, the turn's wait and the held stick feel ([combat](systems/combat.md#pacing-beats-and-the-players-wait)):

1. Is each result (a hit, a miss, a fall, a buff or debuff) on screen long enough to read? Each beat lasts its reaction, at least half a second (`BeatLeast`, 30 ticks).
2. When two enemies act at once far apart, does the later one's hit wait for the earlier one's to finish, while its walk still starts early? Does that read better than stage 4, or slower than it's worth?
3. After an enemy hits a party member, does your turn wait till you've seen the hit? When the hit is near your character, it may open while the hit plays: is that right?
4. While an enemy is still walking near your character, does your turn wait to give you the stick, then come as soon as it can't be reached? Is the wait too long or too short (`Buffer`, one enemy radius)?
5. Press A while your turn waits: does it bring what holds it to its end at once, and the turn open?
6. Aim at an enemy far off that's still walking in the picture: the aim is held, your character standing still in its pose, till it's drawn where it is, or A skips. Is the still pose acceptable?
7. Overall, is Start Early now paced right: faster than Original, but every result seen?

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–14 are played, and none is waiting. If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).
