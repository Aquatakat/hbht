# 0014. The menus keep the original's layouts; new layouts are for mods

Status: Accepted · Oct 4, 2026

## Context

The UI screen can be taller than the original's 240 lines and any shape, so the pause menu had a second, wide layout from 560 × 320 up: the Party tab's image grown into a sheet as big as the screen, from pieces of it cut out, mirrored, tiled and feathered together, with the list in the main font and C-down's details always open on a mirrored parchment at the right. The System tab sat on the same sheet, and the barter and training states had wide versions of their own.

The original's menus are bespoke art at one size: the labels, the frames, the knotwork and the torn edges are painted into each tab's image. Growing them leaves seams and smears all over, at the torn edges, where pieces meet and where the parchment's grain repeats. Each can be patched, but there's always another, and the World tab, whose map and calendar are painted in, couldn't be grown at all. The user found the wide layout made too many trade-offs to correct (Oct 4, 2026).

## Decision

- **The menus are drawn as the original lays them out**, in its 320 × 240 frame at the screen's centre, at every screen size. A taller screen draws them smaller, as it does all the UI.
- **The tabs show side by side** on a screen wider than the frame, 320 apart as the scroll's images join, the one in use at the centre and those beside it faded into their parchment.
- **A different layout needs new art**, which is outside a reimplementation's scope. It's a modding feature for after 1.0: a mod replacing a menu entirely, built in the editor. How that works is decided then.

## Consequences

- The wide layout's code is gone: the sheet and its tiling, the Party tab's wide drawing and the barter and training states' wide versions. The pile is always left of the party's list, so the stick's left reaches it, as in the original.
- Where the port has to change how the original's art looks, as when the World tab beside the one in use fades its painted calendar, it does it with the art's own pieces, at the art's own size.
- Menus for other screen shapes wait for menu replacement in the editor.
