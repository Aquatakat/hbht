# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### Combat Turns: Start Early's director (piece C)

Under Combat Turns: Start Early the camera is now always Camera: Enhanced, filming the drawing ([cameras](systems/cameras.md#start-earlys-director)). In a battle where enemies overlap (bandits, such as `--battle aidyn:mapobject/0/J18/6`, or the Chaos casters), with F3's drawing on to see what it holds:

1. Is every hit, miss, fall and buff on the screen as it lands? Does the camera start toward one early and arrive as it lands, or does it cut at the last moment (F3 says "cut to a beat about to strike")?
2. When two enemies' results play at once, is the wide or high shot that holds both readable, or would you rather they took turns (`BeatDistance`, 40, and `BeatPitches` say how wide and how high)?
3. With one enemy acting alone, does it feel like Camera: Enhanced under Original turns?
4. In your turn while an earlier hit still plays, is the view drawn back enough to see it and your character, and does it settle back over the shoulder smoothly after?
5. Does the stick go the way you see, all through your turn? Does R still take the camera, and aiming give it back?
6. Are the bodies' sounds panned and levelled for the camera you see?

After your Nov 10 play (F3 now rings walkers teal and says how long an outcome hold has left):

7. Is each result held long enough after it lands before the camera moves on, and is a second between two enemies' results right (`OutcomeHold`, 60 ticks)?
8. Do the two-shots-every-tick flicker, Dougal's swing's wobble and the wild spin still happen anywhere?
9. Do enemies walking while you play, or while another's result shows, stay in view, and do you see where their walks end? Does the view draw back too quickly for them (`DrawBackPace`)?
10. Stopping with no enemy ahead, does the view rest well where it stopped?
11. If a walk stalls again (Dougal's on Nov 10, Keelin's on Nov 11 at C03 (50.6, 35.2) to (48.1, 34.7)), read F3's first line as it does: it now ends with the body's request (2 walks, 3 runs) and the stick's tilt. Keelin's recording shows a real slowdown to about half speed for 1.2 seconds, on one smooth plane 6° downhill, no body within 3.5 of her, well inside her field, the camera still: nothing of the arena's, the bodies' or the camera's. What's left is the stick near the 0.8 tilt a body runs from, the request going between walk and run; the recording's turn-order portraits covered the request. A request that flips while the tilt wavers about 0.8 settles it, and then whether to add a margin to the threshold is yours.

After your Nov 10 question about Fireball:

12. Does Fireball (or any spell at several targets, an enemy's or yours) now land on all its targets at once, in one shot holding them all, its casts' sounds and visuals together? The caster now faces the middle of its targets where they're within a 120° arc of it, and stays as it stood where they surround it (your Nov 11 idea, `CastArc`): does that read right, with a buff on the whole party and with Fireball on a pack ([combat](systems/combat.md#pacing-beats-and-the-players-wait))?

After your Nov 11 recording ([cameras](systems/cameras.md#start-earlys-director), the user's second play):

13. Does the view still go past a shot and come back anywhere? F3 says what it was doing ("holding the outcome", "over the shoulder", "a shot of its own").
14. Does each of your turns now open with a cut over the shoulder where the view was far round, rather than a swing? Would you rather a quick pan there, as into an aim?
15. Reserves walking on and throwing: does the wide shot hold through their walk, then cut over the thrower's shoulder, then to the hit with the throw in the air?
16. Is the notch of a zoom gone from a wait on a throw?
17. Bandits walking in your turn: does the view widen once for them and stay, without the wobble? Does it stay wide too long after they stop?

After your second Nov 11 play (the battle screen and Dragon Flames):

18. Dragon Flames at four: does every target now burn? F3's last line has the emitters in use, of 64 under Start Early. Do the four hits land near enough together in your own cast (they're a few ticks apart; an enemy's are on one tick)?
19. Do the enemies' portraits now stay put through a Fireball and the enemies' turns: none gone, none empty with its bar, no flicker? A reserve keeps its small frame until the oldest thing still playing is done: is that lag noticeable?
20. Does the log's portrait now go with its line?

After the bow's lost reticle (Nov 11):

21. With Sholeh's bow, take a potion in hand from C-up and put it away with B, or drink it: does her next A still bring the free cursor, and her arrow fly before the result? If the reticle goes again without a potion, note what she did the turn before.

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
