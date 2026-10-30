# 0030. A modern variant of a system is a setting beside the original's, held to the original's reach

Status: Proposed · Oct 29, 2026 · Adds to [0018](0018-the-port-fixes-bugs-and-sets-enhancements.md)

## Context

[0018](0018-the-port-fixes-bugs-and-sets-enhancements.md) puts enhancements behind settings: more rain, a longer draw distance, a brisker pace. Each changes how much of something the original does, or how it looks, and the rules stay the original's.

Modern movement is a different kind of enhancement ([movement](../systems/movement.md#modern-movement)). The user found 1.0's movement faithfully ported and faithfully bad: spongy on terrain, bouncing off slopes, creeping up things and sluggish. Modern movement replaces the rules themselves, a new controller beside the original's, chosen by a setting. A new rule for how the player moves can open places the original never lets him reach, out of the world or past where the story means him to go, and losing a path the original has is as bad. Neither shows until someone walks there, and the game has 17 map sets.

## Decision

- **A modern variant of a system is a setting beside the original's, never a replacement.** The original's rules stay as they are, tested as they are, and stay the default until the user chooses otherwise. The variant is passed to the core each tick like any other setting ([0023](0023-the-core-is-handed-its-settings-each-tick.md)).
- **It's held to the original's reach.** Where a variant could change what the player can get to, a check compares the two across all the game's content, using the core's own code and data, not a model of it: for movement, `hbht movement reach` (`Reach`). What only the variant reaches is a new way out of bounds until it's judged a fine new path, and what only the original reaches is a lost path. Each is settled before the variant is offered, and the check runs again whenever either changes.
- **The variant keeps the contracts the rest of the game reads.** Trek, the camera, triggers, breadcrumbs and the fall rescue read the player the same way under either movement.
- **The data's own intent decides where it can.** Modern movement treats plain collision as walls because that's how the designers used it (94% of its upward-facing area is steeper than 60°, where 91% of the other surfaces' is within 26° of level), not by a steepness picked by feel alone.

## Consequences

- Modern movement ships with its check, and its doc lists what the check found and how each was judged.
- A later variant, such as a modern camera, would come with a check of its own for what it could break, such as seeing out of the world.
- The original's rules can't drift to suit a variant: the check compares against them, and their tests stay.
