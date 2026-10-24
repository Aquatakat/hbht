# 0026. Controls are listed by what the player does in each situation, and bound per device in the core

Status: Accepted · Oct 24, 2026 · Fills in [0024](0024-the-options-are-pages-by-kind.md)'s "Controls will join them"

## Context

Until now the runtime's input was a set of named Godot actions built in code (`Game.AddInputs`), one per N64 button, each with a key and a joypad button or axis. They were read wherever they were needed: `menu_details` (Tab, Y) was the menus' C-down and combat's spell menu, `menu_member_left` (Q, LB) changed the member and stepped a bow's target, and Q and E turned the camera in trek and changed the member in the menus. Nothing could be rebound, there was no mouse look, and binding one N64 button would have changed everything it did in every situation.

## Decision

- **Controls are listed by what the player does in each situation, not by the original's buttons** (`GameControls`, in `Aidyn.Core.Controls`). The situations are trek, the menus, a battle and cut-scenes and conversations (`Situation`). A button that does different things in different situations is several controls that share a default: C-down is the menus' Details and combat's Spell Menu. Each control says what pressing it, and letting it go, is to the game (`MenuAction`), so the runtime translates an event by the controls of the situation it's in (`Situations.Of(GameMode)`).
- **Each control has bindings on two devices**, the keyboard with the mouse and a controller, kept in the core as strings (`ControlBindings`, `Binding`): `Key:W`, `Mouse:Left`, `Joy:A`, `Axis:LeftX-`. A direction (Move, Camera, a menu's cursor) instead takes a set of sources, each four directions at once: WASD, the arrows, the mouse, either stick, the D-pad (`InputSource`). The runtime turns the bindings into Godot's input actions as the game starts and whenever they change (`InputBindings.Apply`). They're kept in `settings.json`'s `controls`, only the controls the player changed, so a control added later takes its default.
- **The defaults are the bindings the runtime had**, so nothing changes for a player who never opens the page.
- **Conflicts are checked only within a situation**, and the newest binding wins: a control in the same situation that had the input loses it, a button its binding and a direction the source. Two directions may share a source. A battle's aiming is a situation of its own for this, so the shoulders can turn the camera on a turn and step a bow's target when aiming, as by default.
- **The player can't lock themselves out.** The essential controls (trek's Menu, the menus' Move Cursor, Accept and Back, combat's Pause and a conversation's Continue) always keep a binding on each device: clearing one is refused, a rebind that would take one's last binding is refused, and a file that leaves one with none gets its default back. The page's prompt waits five seconds and binds whatever input of its device comes, Escape, Backspace, Start and B among them; nothing cancels it but the time running out.
- **Camera Hold steers the camera with what moves.** Sources Move and Camera share move him, and with Camera Hold held steer the camera instead, holding him still as the N64's R does; Move's alone still walk him and Camera's alone always steer. The mouse can't move him, so off the Camera row it steers while Camera Hold is held (the right button's drag, as before), and on it it always steers, captured in trek and a battle (mouse look).
- **The debug keys aren't controls**: the F-keys, the free camera's keys and fast-forward stay fixed, and a control can't take F3 or F5–F11.

## Consequences

- A new thing the player does is a `GameControl` in its situation's place in `GameControls.All`, with defaults and what it is to the game; the runtime reads it by its ID, and the page lists it. Code reads the control's action, never a key.
- `--press` names the controls' actions, so a press means what it does in the situation the game is in: `menu_accept` in the menus, `battle_attack` in a battle. A binding's name (`Key:P`) presses that input itself.
- A cell holds what the defaults give it, but the page's rebind replaces it with one input, so a player can't add a second key; Reset brings the defaults' second keys back.
