# UI: text and the conversation box (roadmap 1F)

How the port lays out and draws the original's UI. The fonts and string tables themselves are in [text](../formats/text.md).

## The screen

The original draws its UI on a 320 × 240 screen (`SCREEN_WIDTH`, `SCREEN_HEIGHT`); the Expansion Pak's high-resolution mode is 512 wide, and `borg8DlistInit` scales images to it. Every UI image is a texture rectangle with bilinear filtering and a combiner of texel × primitive colour (`N64BorgImageDraw`), so text is its sheet's texels times the text's colour, alpha too. At 1:1 the filter samples each texel exactly: text and portraits land on the framebuffer pixel for pixel, and only scaled images, such as a box's gradient or the party's portraits at 0.75, are filtered.

**The soft look is the video output.** The emulator's captures of the original are 640 × 240, and their columns alternate between the framebuffer's pixels and the average of the two either side (the compass's letters read 153, 109, 65 across). Each line's 320 pixels are spread over 640 by linear interpolation, and the lines aren't touched. On a real console the VI does that resampling and adds its own anti-aliasing and divot filters, and then a composite or S-Video signal and a CRT soften it further; the captures show only the first step.

`UiScreen` (in `Aidyn.Game`, which the editor shares) draws the UI the same way: views draw in a 320 × 240 `SubViewport`, in the original's pixels, with linear filtering as the console has it, and the screen is scaled up afterwards to fit, by one of three filters (`UiFilter`):

- **VideoOut**, the default: as the captures show it, each line doubled to 640 with the averages between, then scaled up sharp.
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

- **A black band** runs across the screen from the box's top to 180 plus the portrait border's height, 232. Above it a 10-pixel gradient fades upward, image `0x1C3` scaled from 16 pixels tall and drawn black.
- **The portrait's border** (`0xDF`, 52 × 52) is at x 20, level with the box's top, and the portrait (40 × 40) sits 6 pixels inside it. A speaker with no portrait gets `0x0125` (`BORG8_PortraitNPCMale1`).
- **The line** is in (200, 200, 200), from x 78 (the border's width + 26) to 294, and ends 2 pixels above what's under it: the band's bottom, or the choices. The box's top is 4 pixels above the line, but never below 180, so the box is at least the border's height: a short line sits at y 184, and the box grows upward from five lines.
- **Choices** are in tan (200, 180, 100), from x 88, 4 pixels apart, and end at 228. The cursor's choice pulses toward (50, 45, 25, 63) and back over 24 frames each way, whole steps per channel (`WidgetScrollMenu::Tick`).
- **A line before its own speaker's choices shows alone.** When a menu's first choice has the line's speaker, the line shows first, and A brings up the choices alone under Alaron's portrait. The box shrinks to the choices, but the portrait and its border stay where the line put them, since `Setup` doesn't move them on that path.

Not ported yet: the widgets' fade in and out (`fadeFloatMirror`), scrolling a menu taller than its bounds, and the full-screen backdrops' text with its shadow (`WidgetShadowText`).

## In the editor

A script's player shows the conversation box above the page's controls, over a plain backdrop, at 1×, 2× or 3× and with any of the three filters. When a line shows alone before its choices, Continue brings them up, as A does. Hovering a choice puts the cursor on it. `--zoom <1-3>` and `--ui-filter pixels|videoout|smooth` set these for screenshots.
