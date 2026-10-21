# 0018. The port fixes the original's bugs outright, and puts enhancements behind settings

Status: Accepted · Oct 17, 2026 · Supersedes the fidelity toggle in [0003](0003-headless-rules-core.md) and [0007](0007-rom-versions.md) · How a setting reaches the core is refined by [0022](0022-the-core-is-handed-its-settings-each-tick.md)

## Context

At the start of the project, [0003](0003-headless-rules-core.md) said that a kept original bug sits behind a fidelity toggle. [0007](0007-rom-versions.md) said that 1.1's code fixes become fidelity toggles, and the roadmap's Phase 6 put every enhancement and bug fix behind a fidelity profile. A toggle like that would cover three different things:

- **Exact N64 reproduction.** This isn't a goal of the project.
- **Fixed bugs that a player can turn back on.** These confuse a player and give little value.
- **Enhancements that a player can turn on or off.** These make sense, and most built so far work this way.

Rain shows the difference. The original's raindrops collide with the collision, invisible walls and boxes included, so splashes appear floating in the air. That's the genuine N64 experience, but it's clearly wrong, and there's little value in keeping it. The port's rain lands on the surfaces the chunks draw (`VisibleWorld`). The console also draws very few raindrops, probably for its performance. More rain genuinely changes how the game feels, so that sits behind a setting (`PrecipitationDensity`), defaulting to the original's.

The port has already worked this way without a toggle anywhere:

- **Bugs fixed:** the camera's mode now follows the chunk however the game was loaded ([cameras](../systems/cameras.md)), and each wanderer notices the player for itself ([wanderers](../systems/wanderers.md)).
- **Enhancements as settings** on the options menu's graphics page ([0013](0013-settings-in-the-options-menu.md)): draw distance, model detail, the UI screen's height and filter, the sky's placement, rain density and a brisker pace.

## Decision

- **Exact N64 reproduction is not a goal.** The port plays and feels like the original. Its rules, formulas, timings and content are ported faithfully ([0003](0003-headless-rules-core.md), [0017](0017-the-core-steps-every-tick.md)), and where it departs, it does so on purpose.
- **A bug is fixed outright, with no toggle.** A bug is the original doing something clearly wrong: a splash floating on an invisible wall, or a camera that depends on how the game was loaded.
  - The user decides each fix.
  - The system's doc says what the original does, with its addresses, and that the port fixes it.
  - A bug that is part of how the game plays may be kept instead, decided the same way, one at a time.
- **1.1's code fixes are bugs like any other.** As each is identified, the user decides whether the port takes it.
- **An enhancement is a setting.** An enhancement changes something the original does correctly, but within the console's limits or by a matter of taste: few raindrops, a short draw distance, a slow pace.
  - It's a property on `Settings` and a row in the options menu ([0013](0013-settings-in-the-options-menu.md)).
  - Its default is the user's choice, usually the original's.
  - An enhancement that came early as the default, such as the floating conversation box, can gain a setting for the original's look later.
- **The rules don't branch on a fidelity mode.** Nothing in the core asks which profile it's in. A setting the core needs is passed to it like any other input. A preset that changes several settings at once is a convenience the options menu may add, not a mode.
- **Numeric types still match the original's** ([0003](0003-headless-rules-core.md)), including overflow and truncation. Where that truncation is a bug that matters to play, it's decided as any other bug is.

## Consequences

- There's one version of each rule to test. Differential tests (roadmap 2.9) compare against the original where the port is faithful. Where it fixes a bug, the test asserts the port's behaviour and its doc cites the fix.
- Phase 4's list of bugs to keep becomes a list of decisions, one each, such as the `CheckPartyGoldU16` truncation, the crash when `TakeItem` runs on an item the party doesn't have, and the status-icon cycle gap.
- Phase 6 is a list of settings, not a profile.
- A "faithful mode" in a system doc means a setting for the original's look or feel. It never means a switch that restores a bug.
