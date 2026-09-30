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

## In the editor

A script's player shows the conversation box above the page's controls, over a plain backdrop, at 1×, 2× or 3×, on a 4:3 or 16:9 window, with a UI screen of 240, 360 or 480 lines and with any of the three filters. When a line shows alone before its choices, Continue brings them up, as A does. Hovering a choice puts the cursor on it. `--zoom <1-3>`, `--aspect 4:3|16:9`, `--ui-lines <n>` and `--ui-filter pixels|videoout|smooth` set these for screenshots.
