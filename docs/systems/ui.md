# UI: text and the conversation box (roadmap 1F)

How the port lays out and draws the original's UI. The fonts and string tables themselves are in [text](../formats/text.md).

## The screen

The original draws its UI on a 320 × 240 screen (`SCREEN_WIDTH`, `SCREEN_HEIGHT`); the Expansion Pak's high-resolution mode is 512 wide, and `borg8DlistInit` scales images to it. Every UI image is a texture rectangle with bilinear filtering and a combiner of texel × primitive colour (`N64BorgImageDraw`), so text is its sheet's texels times the text's colour, alpha too. At 1:1 the filter samples each texel exactly: text and portraits land on the framebuffer pixel for pixel, and only scaled images, such as a box's gradient or the party's portraits at 0.75, are filtered.

**The soft look is the video output.** The emulator's captures of the original are 640 × 240, and their columns alternate between the framebuffer's pixels and the average of the two either side (the compass's letters read 153, 109, 65 across). Each line's 320 pixels are spread over 640 by linear interpolation, and the lines aren't touched. On a real console the VI does that resampling and adds its own anti-aliasing and divot filters, and then a composite or S-Video signal and a CRT soften it further; the captures show only the first step.

**The original covers the screen's edges** with black bars for overscan (`Graphics::EndList`): in 1.0 and 1.1, 16 pixels at the left and right and 12 at the top and bottom, filled rectangles drawn after everything else. The EU version draws 18 and 12 with its image code. So the UI's own boxes run under the bars and are cut off by them.

`UiScreen` (in `Aidyn.Game`, which the editor shares) draws the UI the same way: views draw in a `SubViewport`, in its pixels, with linear filtering as the console has it, and the screen is scaled up afterwards to fill the window, by one of three filters (`UiFilter`). The screen is `Lines` pixels tall, 240 by default as the original's, and as wide as the window's shape, but never narrower than 4:3. Views lay themselves out by its size rather than assuming 320 × 240. A taller screen draws the UI smaller, with room for more: at 480 lines, text is half the size. Text itself is always drawn as authored, pixel for pixel, and wraps at the same widths. The filters:

- **VideoOut**, the default: as the captures show it, each line doubled in width with the averages between, then scaled up sharp.
- **Pixels:** sharp.
- **Smooth:** bilinear both ways.

The canvas blends in on-screen values, as Godot's 2D does without HDR, so a glyph's alpha and colour multiply as the N64's do. It's premultiplied, since alpha blending into a transparent target leaves colour times alpha, and the upscale filters average it that way. A later pass could add the VI's other filters or a CRT's.

## Laying text out

`TextLayout` (`Aidyn.Core.Ui`) is the font engine's layout, over a font record (`Font::PrintMain`, `GetHeightScaled`, `GetWidthScaled`):

- A character draws its glyph at the pen, as wide as its measured width and a whole cell tall, and moves the pen by that width. A character with no glyph is skipped and takes no room.
- **Text wraps only at spaces.** After a space, if the next word would end past the right edge, the line breaks and the space is dropped. A space at the start of a line takes no room, and a word longer than the line runs past the edge. `\n` breaks the line.
- Lines are the cell's height plus 2 apart: 11 pixels in both fonts.
- Text is drawn only inside its widget's bounds. A glyph across an edge is cut, and one outside is dropped.
- Characters from `0x7F` are the controller buttons, drawn in their own colour with the text's alpha.

## The conversation box

`DialogueBox` (`Aidyn.Core.Ui`) places what a conversation shows, as `DiaMenuSub::InitDefault` and `DialougeClass::Setup` do, and `ConversationBoxView` draws it in the main font. It's for conversations and the other modes without a backdrop; backdrops and the help menu draw on a full-screen image (`BigBGDialoug`), which isn't ported yet.

The box is laid out in the original's 320 × 240 screen, placed at the bottom centre of the UI screen, so on a 4:3 screen of 240 lines it's where the original's is, and elsewhere it keeps its size. The positions below are in that frame.

- **The original's band** is translucent black: its image, `0x1C4`, is a single white texel of alpha 136, drawn black. It runs right across the screen from the box's top to 180 plus the portrait border's height, 232, so the overscan bars cut it at 16, 304 and 228. Above it a 10-pixel gradient fades upward, image `0x1C3` squeezed from 16 pixels.
- **The port floats it** instead, since a wide screen has no bars to cut it off. The band runs from 16 to 304 and down to 228, where the bars would stop it, and it's feathered on every side where the bars were, from the pieces the original's own popups are made of (`WidgetChoiceDia::SetImages`, see `FeatheredPanel`): the 10-pixel gradient above, `0x1C2` below, `0x1C0` and `0x1C1` beside it, each fading outward from alpha 136, and the rounded corners `0x1BC`–`0x1BF`. On a 4:3 screen it looks as the original's does inside its bars. The decomp names three of the four gradients wrongly; the machine code puts `0x1C3` above a popup, `0x1C2` below, `0x1C0` left and `0x1C1` right, and the dialogue box's gradient is `0x1C3`.
- **The portrait's border** (`0xDF`, 52 × 52) is at x 20, level with the box's top, and the portrait (40 × 40) sits 6 pixels inside it. A speaker with no portrait gets `0x0125` (`BORG8_PortraitNPCMale1`).
- **The line** is in (200, 200, 200), from x 78 (the border's width + 26) to 294, and ends 2 pixels above what's under it: the band's bottom, or the choices. The box's top is 4 pixels above the line, but never below 180, so the box is at least the border's height: a short line sits at y 184, and the box grows upward from five lines.
- **Choices** are in tan (200, 180, 100), from x 88, 4 pixels apart, and end at 228. The cursor's choice pulses toward (50, 45, 25, 63) and back over 24 frames each way, whole steps per channel (`WidgetScrollMenu::Tick`).
- **A line before its own speaker's choices shows alone.** When a menu's first choice has the line's speaker, the line shows first, and A brings up the choices alone under Alaron's portrait. The box shrinks to the choices, but the portrait and its border stay where the line put them, since `Setup` doesn't move them on that path.

Not ported yet: the widgets' fade in and out (`fadeFloatMirror`), scrolling a menu taller than its bounds, and the full-screen backdrops' text with its shadow (`WidgetShadowText`).

## The pause menu (M2)

Not ported yet. What the decomp, the imported images and the user's captures (Oct 1, 2026; see the [reference rig](../reference-rig.md#emulator-captures)) show so far (`BigAssMenu.cpp`, `DollMenu*.cpp`). Positions are in the 320 × 240 frame.

- **The tabs are one long scroll.** System, Party and World are each a widget tree in its own 320 × 240 frame, 320 pixels apart, and changing tab slides them all 10 pixels a frame. Under them is a 3D scroll, `aidyn:actor/0x0E6B` (scene `0x0E63`), seen from above, which slides with them: System shows its wooden roller at the left, World at the right, and Party, in the middle, neither. Its states unroll and roll it on opening and closing; the user reads some as the full menu's and some, shorter, as the smaller windows' (barter, training, containers). Past the torn top and bottom edges the world shows through.
- **Each tab has a full-screen image** over the scroll (`pause_menu_borg8`): `0x00D8`, `0x00D9` and `0x00DA`, all 320 × 240.
- **The Party tab's layout is painted into its image.** `0x00D9` is one parchment with the silhouette and its slot frames, the portrait frames, the labels HitPoints, Level and Gold, the HP bar's frame, the knotwork divider down the middle and "Details" with the C-down button. None of those labels are in the string tables. The widgets only fill it in.
- **The header:** the member's portrait large at the top left with their name beside it, and the others small after it, in party order starting from the member shown. Under the large portrait, the aspect's and the school's icons. HP as current and maximum in the bar's frame, then level and gold.
- **The silhouette:** each slot's frame holds the worn item's icon, or a faint icon of what the slot takes (helm, amulet, gloves, ring and so on).
- **The list** (`DollMenuLists`) is one of up to four: inventory, stats, skills and spells (training drops stats and adds a second column; barter keeps only the inventory). The analogue stick changes it, and its title is an image (Inventory, Stats, Skills, Spells) on the rule above it, with a small down arrow at the right when it has more rows than show. The list sits at (160, 101), bounded from x 160 to 300 and y 101 to 205; the others wait at x 20. The cursor's row is in lavender.
  - **Inventory**, 13 pixels a row, eight showing: the wearer's small portrait, the item's icon, the count and the name. Worn items come first, the member shown first and then the rest in the header's order, then what's carried.
  - **Stats**, 11 a row: the six attributes, with the difference gear makes in red or green, then XP remaining, total XP and next level.
  - **Skills**, 13 a row: the skill's icon, name, level and its difference from gear; weapon skills have no icon.
  - **Spells**, 13 a row: two icons (the school's and the spell's symbol), the rank and the name.
- **C-down's details** open a popup on the 160 × 160 parchment (`0x00E2`, with its shadow `0x00E4`) over the silhouette, from x 15 to 160 and y 63 to 218: an item's icon and name with its skill, aspect, needs, damage, what it adds and its spell and resistances; a stat's description; for a craft skill, what it can make (Mechanic's Create Armor lists the hides). Barter and post-battle training use the same parchment over the silhouette.
- **A red arrow** under the knotwork at (126, 207) points both ways in trek (`BORG8_SliderArrowLR`) and only left in combat.
- **The other tabs:** System is a list (Load Game, Save Game, Options, Theater, Credits, Help) under its title image. World is the world map at the left with its compass, the calendar (the season's name, the month's days with the moon's phases, today boxed) and Camp and Journal. The journal is a book filling the screen, its entries in fixed order, scrolling, with the newest in purple and an arrow at the right.

### Layouts, proposed (Oct 1, 2026)

The user wants the menu responsive: exactly the original's at 4:3 and 240 lines, opened up where the screen has room. To settle in M2's first session:

- **Chosen by the screen's size in its own pixels, not its shape.** The classic layout fits 320 × 240; the wide one needs about 560 × 320, as 16:9 at 360 lines (640 × 360) gives. A screen too small for the wide layout gets the classic one at the centre, as the conversation box is.
- **The tabs are a screen apart, not 320.** On a screen wider than 320, tabs 320 apart would show the neighbours' edges, so each tab is as wide as the screen and the scroll slides that far. The scroll itself is the one thing that must stretch with the screen: its rollers belong at the screen's edges on System and World, so the sheet between them is scaled across, or its rollers moved apart, once its scene has been looked at.
- **Classic** draws `0x00D9` whole and fills it in at the original's coordinates. At exactly 320 × 240 it's the original, row for row.
- **Wide** has three columns: the character (portraits, HP, level, gold and the silhouette), the list, and C-down's details, kept open for the row under the cursor. Lists use the main font where the original squeezes rows into the slim one, and as many rows as fit. The parchment is composed: the silhouette, labels, divider and "Details" are cut out of `0x00D9` as image pieces and placed, on parchment panels built from its torn edges or `0x00E2`, so the art stays the original's and no new strings are needed.
- **The reuses keep their columns.** Barter puts the shop's stock where the character was (as the original covers the silhouette with it), the party's inventory in the middle and the details on the right; training shows spells and skills side by side.
- **One model, two layouts.** The menu's state (the member, the tab, each list's rows, order and cursor, and what A does in each reuse) is in the core, and a layout only places panels, so the reuses and both layouts share it.
- **Controls:** C-left and C-right become the shoulders (LB, RB), changing the member; Z/L and R, which change the tab, become the triggers; C-down's details are a face button, and in the wide layout, where the details are always open, it does nothing.

## A faithful mode, later

The floating panel and the resizable screen are enhancements. The user chose them over the original's look for now (Sep 30, 2026), and left a faithful mode for the fidelity profile at the very end ([roadmap](../roadmap.md), Phase 6's enhancements). What it would take:

- **The screen.** `UiScreen` fixed at 320 × 240 whatever the window's shape (`Lines` 240, width 320), shown pillarboxed at 4:3, with the VideoOut filter. The 3D view would need the same 4:3 frame, since the bars below cover it too.
- **The overscan bars.** Black rectangles over everything, drawn last, as `Graphics::EndList` does: 16 pixels at the left and right and 12 at the top and bottom in 1.0 and 1.1, 18 and 12 in the EU version. They'd belong over the whole frame, 3D and UI, not in a UI view, and they'd cut the conversation box off at 16, 304 and 228 as the original's is.
- **The conversation box.** A style for `DialogueBox`: a band right across the frame, from 0 to 320 and down to 180 plus the border's height (232), with only the 10-pixel gradient above it. That's a `FeatheredPanel` whose bottom and side feathering are 0, which `UiImages.DrawPanel` already skips, and the frame at (0, 0).
- **Other UI.** Everything laid out by the screen's size would be at its original place once the screen is 320 × 240, as the conversation box is. Nothing else changes.
- **High-resolution mode**, if the fidelity profile offers the Expansion Pak's option: a 512 × 240 screen, where the original lays UI out in 320 × 240 and `borg8DlistInit` stretches every image 1.6 times across, bilinear, so text is filtered horizontally. `UiScreen` would draw its 320-wide canvas into a 512-wide one with linear filtering before the VideoOut filter. Its overscan bars are 20 pixels at the sides.
- **The VI and the TV**, which apply to either mode: the VI's anti-aliasing and divot filters and a composite or CRT look are further `UiFilter`s.

## In the editor

A script's player shows the conversation box above the page's controls, over a plain backdrop, at 1×, 2× or 3×, on a 4:3 or 16:9 window, with a UI screen of 240, 360 or 480 lines and with any of the three filters. When a line shows alone before its choices, Continue brings them up, as A does. Hovering a choice puts the cursor on it. `--zoom <1-3>`, `--aspect 4:3|16:9`, `--ui-lines <n>` and `--ui-filter pixels|videoout|smooth` set these for screenshots.
