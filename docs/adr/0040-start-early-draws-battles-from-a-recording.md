# 0040. Combat Turns' Start Early runs a battle ahead and draws it from a recording

Status: Accepted · Nov 7, 2026

## Context

A battle plays one action at a time: each combatant waits for the last action's walk, swing, missile and hit to finish before it begins. Much of a battle is that waiting, and many actions share nothing: two bandits throwing at two different party members, or a walk on one side of the arena and a potion on the other. The player asked for the next combatant to start while the last action plays, as Ogre Battle 64 does, as a setting over the original's pace.

There are two ways to do this. The core could step several actions at once, or it could step them one after another as now and overlap only what's drawn. Stepping them at once would change the rules, because a turn's ticks feed them:
- every body's actor rolls on one shared `Animation` stream, at each state's end, and those rolls decide fidgets and refused runs;
- the stuck test counts over a body's root motion;
- a missile's flight time follows from where it leaves the hand;
- the AI chooses by where the other bodies stand.

The core is headless (ADR 0003) and can play a whole turn in a few milliseconds without drawing it.

## Decision

- **A setting, Combat Turns: Original or Start Early,** on the Enhancements page and handed to the core as 0023 says. Original is the default and plays as now.
- **Under Start Early the core runs ahead.** When a turn the AI plays begins, the battle steps without drawing, tick by tick, in Original's order. It stops at a turn the player plays, at anything that waits for a press (Sense Aura's box, a held line) and at the battle's end. It steps as many ticks a frame as a budget allows, enough to stay ahead of the drawing. The rules, their order and their random numbers are Original's.
- **The battle records what's drawn, tick by tick:**
  - each combatant's place, heading, movement, what it holds, its alpha, its tint, and its actor's state and clock;
  - the numbers, missiles and spell visuals, with their particles' starts and stops;
  - the ring and the aim;
  - the fade and the sky's dimming;
  - the battle screen's log, portrait, marks, fleeing and health;
  - the cues and the sounds.

  The views draw from the recording under both settings, never from the core's live objects, so a battle is drawn one way. Under Original the recording is drawn as it's made.
- **The recording is cut into actions, one a turn.** An action's bodies are those it changes beyond their idling: its combatant, its targets, and those it pushes, brings on or fells. An action starts drawing at the earliest tick where both hold:
  - none of its bodies is still drawing in an earlier action;
  - no body it moves comes within reach of a body still drawing, along their recorded paths (their swept spheres).
- **A turn the player plays stays live.** It opens once its combatant is free in the drawing. Anything it touches that's still drawing is first fast-forwarded to its action's end: a target it aims at, a body near the player's path, or anything the pause menu shows.
- **A during the AI's walk only fast-forwards the drawing under Start Early,** the user's choice. The walk plays out in the core as if A wasn't pressed, sticking included. Under Original, A keeps 1.0's skip to the walk's end.
- **A battle's ending waits for the drawing:** a victory, a defeat, the rewards, the Shadow's ending, and a flee that ends it.
- **The battle asks the view nothing that changes its ticks:**
  - the core owns each combatant's actor motion;
  - the core works out where the hand's locator is from the actor's own data;
  - the core decides when a spell visual strikes and how long its emitters run;
  - the core's sounds go through a seam that the recording can take.

  This is what lets the core run ahead. It also makes headless runs, the tests among them, play as the live game does under both settings.

## Consequences

- With Start Early, the core plays a battle as Original does, tick for tick, as long as A isn't pressed in a walk. Combat parity holds under both settings.
- The runtime draws a recording of what the core stepped rather than the core's present. That is a new way of drawing, but its loop stays as thin as ADR 0012 asks.
- What the session steps outside the battle stays at the drawing's pace: play time, the sky, the weather's particles and the arena's sounds. Their `Cosmetic` draws then differ from Original's, and no rule reads them.
- An idle body between two of its actions may hold or skip idle frames to meet the next one. That shows as a small jump, which a blend can hide.
- The camera follows the newest action at first. A camera that frames overlapping actions together comes last.
- A wrong overlap shows only in the drawing: bodies pass through each other, and the outcome is unaffected.
