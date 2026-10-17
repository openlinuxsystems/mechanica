# UI Style Guide

Every menu, HUD element and mod tool in Mechanica uses one blue industrial look. This page is for
anyone adding a screen to the game or a mod tool. It covers the palette, the theme, the shared
building blocks and the rules each new menu follows.

## Palette

| Name | Colour | Use |
|------|--------|-----|
| Navy | `#1A2332` at 85% opacity | panel backgrounds |
| Deep navy | `#0D1520` | loading and boot screens |
| Control fill | `#121A26` | buttons, text boxes, lists |
| Control pressed | `#0B111A` | pressed buttons |
| Blue | `#2E86DE` | borders, accents, title bars |
| Cyan | `#00D4FF` | hover, focus, selection, progress, scrollbars |
| Text | `#FFFFFF` | primary text |
| Secondary text | `#B0B8C1` | hints, paths, secondary details |
| Disabled | `#5A6470` | disabled controls |
| Error | `#E5534B` | errors, refusals, failed loads |
| Warning | `#E8E3A0` | warnings |
| Success | `#4CD07D` | success, running |

There is no orange anywhere in the interface: menus, the HUD, overlays, gizmos, tool ghosts and the
script editor. Saturated yellow counts as orange. Content is not interface: a part's paint or a
traffic cone can be any colour. Core tests fail if any palette, overlay or syntax colour
reads as orange, or if this page leaves one out. A headless test (`game/tests/ui`) fails if the theme,
the main menu or an overlay has any.

The palette lives in `src/Mechanica.Core/Ui/UiPalette.cs`. Game code reads it as Godot colours
through `UiTokens`, and rich text (`[color=…]`) uses the `UiPalette` hex strings. Never write a colour
by hand in a screen or overlay: add a named colour to `UiPalette` and this page instead.

## Overlays and gizmos

The mod tools' 3D views and the debug overlays share one set of colours, so a joint looks the same in
the Part Editor, the Test Sandbox and the world.

| Name | Colour | Use |
|------|--------|-----|
| Joint rigid | `#4CD07D` | welded faces and rigid sockets |
| Joint hinge | `#2E86DE` | bearings and rotational sockets |
| Joint slider | `#2FD9C4` | pistons and linear sockets (teal) |
| Joint spring | `#A47CF3` | suspension and spring sockets (violet) |
| No connection | `#E5534B` | faces where nothing would attach |
| Gizmo selected | `#FFFFFF` | the selected socket, a joint's current position |
| Collision | `#00D4FF` | collision wireframes |
| Centre of mass | `#E5534B` | the centre-of-mass cross |
| Joint locked | `#5A6470` | a locked joint in the joint overlay |
| Signal on | `#00D4FF` | a logic connection whose source is on |
| Signal off | `#2E86DE` | a logic connection whose source is off |
| Node logic | `#2E86DE` | a logic connection node with the Connect Tool (solid) |
| Node power | `#00D4FF` | a power connection node (solid) |
| Node bearing | `#FFFFFF` | a bearing connection node, with a blue ring |
| Node structural | `#C3CBD6` | a structural or weld-related connection node |
| Force arrow | `#00D4FF` | forces in the force overlay (aero, thrust) |
| Torque arrow | `#A47CF3` | torques in the force overlay |
| Preview background | `#1A2332` | the background of 3D previews |

Tool previews (the hammer's ghost) are cyan at 35% opacity.

Connection lines (the Connect Tool, UI spec §12) are 3 px blue with a 1 px cyan core and a chevron every 40 px
pointing from source to target. Nodes are 14 px, grow 20% under the crosshair and pulse cyan every 0.8 s while
their part's signal is on. Targets a drag cannot end on fade to 30%; a refused link fades out in the error red.

## Script editor

The Lua editor in the Part Editor and the Bot Behavior Editor sits on the control fill. Errors in the
console are the error red, warnings the pale warning yellow, and other output secondary text. The line with
an error has an error-red band at 30% opacity.

| Name | Colour | Use |
|------|--------|-----|
| Keyword | `#C586C0` | `local`, `function`, `if`, … |
| Library | `#4EC9B0` | `math`, `string`, `table`, … |
| API | `#4FC1FF` | Mechanica's API names and their members |
| Callback | `#00D4FF` | functions the game calls (`on_tick`, `get_outputs`, …) |
| Function | `#8AB4F8` | other function calls |
| Member | `#9CDCFE` | table members |
| String | `#8FD9A8` | string literals |
| Number | `#D7BAFF` | number literals |
| Symbol | `#D4D4D4` | operators and punctuation |
| Comment | `#6A8A9A` | comments |

## The theme

`game/scripts/Ui/MechanicaTheme.cs` builds the theme in code. The `UiRoot` autoload puts it on the
root window, so every control in every scene inherits it. Godot only passes a theme down through
controls and windows, so `UiRoot` also sets it on any control or window whose parent is a plain node or
a `CanvasLayer` (the Part Editor, the HUD, the pause menu). Don't set colours on a screen: pick a
type variation instead.

| Variation | Base | Use |
|-----------|------|-----|
| `PrimaryButton` | Button | the main action of a window (cyan) |
| `DangerButton` | Button | confirming something destructive |
| `SecondaryLabel` | Label | secondary text |
| `ErrorLabel`, `WarningLabel`, `SuccessLabel` | Label | status messages |
| `TitleBar`, `TitleLabel` | PanelContainer, Label | a window's title bar |

Buttons are a dark fill with a blue border. On hover the border turns cyan and the button grows to
1.05× around its centre. `UiRoot` adds that to every button; set the `no_hover_scale` meta to opt one
out. Pressed buttons go darker, and disabled ones turn grey. Scrollbars are thin and cyan. Tooltips
are a navy panel with a blue border and show after 0.25 s.

## Building blocks

- **`ToolWindow`** is a panel with a blue title bar, an X at the top right, a `Body` for content and a
  bottom-right action bar (`AddAction(text, pressed, primary)`). Esc closes the topmost open window.
  `CloseRequested` fires on X or Esc. Set `CloseGuard` to ask about unsaved changes first: it gets an
  action to call when closing is fine.
- **`ConfirmDialog.Ask(owner, title, message, confirmText, onConfirm, onCancel, destructive)`** dims
  the screen and asks. Esc or Cancel says no. Use it before deleting, overwriting, resetting or
  discarding changes.
- **`MechanicaTheme.SelectedSlot()`** returns the bright cyan border for a selected slot or item.

## Layout

- The UI is laid out for 1280×720 and the window stretches it (`canvas_items`), so it scales cleanly
  up to 4K. The window can't be made smaller than 1280×720. The UI scale setting (75% to 150%) applies
  on top of that.
- Use containers and anchors, never fixed positions. Multi-panel screens keep balanced proportions,
  for example 30/40/30 for list, preview and inventory.
- Click targets are at least 32×32 px at 1080p.
- Keep the user's place: scroll position, selection and open tab survive closing, reopening and
  resizing.

## Mod tool mockups

Placeholder layouts for mod tools (UI spec §7). Each box is a navy panel with a blue border. `[ ]` is
a button, `[Save]`-style buttons on the right are primary (cyan), and `■` marks the cyan selection.

A property editor, such as the Part Editor or the Bot Behavior Editor, splits 30/40/30: list, preview
and properties.

```
┌─ Part Editor ─────────────────────────────────────────────────────────────── X ┐
│ ┌ Parts ─────────────┐ ┌ Preview ─────────────────────────┐ ┌ Properties ─────┐ │
│ │ Search…            │ │                                  │ │ Name    [     ] │ │
│ │ ■ Metal block      │ │        (3D view, navy back)      │ │ Mass kg [ 12.5] │ │
│ │   Wood block       │ │   green rigid · blue hinge ·     │ │ Size    [1 1 1] │ │
│ │   Bearing          │ │   teal slider · violet spring    │ │ Script  [Edit ] │ │
│ │   Piston           │ │                                  │ │                 │ │
│ └────────────────────┘ └──────────────────────────────────┘ └─────────────────┘ │
│ Problems: ✓ No problems.                       [Delete] [Revert]  [Save]        │
└─────────────────────────────────────────────────────────────────────────────────┘
```

A tool dialog uses `ToolWindow`: a title bar with an X, the body, then the actions bottom right.
Delete asks first with `ConfirmDialog`.

```
┌─ Delete recipe ────────────────── X ┐
│ Delete "Metal block"?               │
│ This removes recipes/metal.json.    │
│                                     │
│                 [Cancel]  [Delete]  │
└─────────────────────────────────────┘
```

The script editor puts the code on the left and the console below, with the API reference on the
right.

```
┌─ Script: piston.lua ──────────────────────────────────────── X ┐
│ ┌ Code ───────────────────────────────────┐ ┌ API ───────────┐ │
│ │ 1  local push = 40       -- newtons     │ │ Search…        │ │
│ │ 2  function on_update(dt)               │ │ ■ part.id      │ │
│ │ 3    local v = part.velocity()          │ │   part.position│ │
│ │ 4  end                                  │ │   part.velocity│ │
│ └─────────────────────────────────────────┘ └────────────────┘ │
│ ┌ Console ───────────────────────────────────────────────────┐ │
│ │ piston.lua: ready                                          │ │
│ └────────────────────────────────────────────────────────────┘ │
│              [New from template]  [Revert]  [Save & reload]    │
└────────────────────────────────────────────────────────────────┘
```

## Rules for new menus

1. A clear title bar with an X at the top right (`ToolWindow`).
2. Primary actions at the bottom or right: cyan when enabled, grey when disabled.
3. The key that opens a menu also closes it, and Esc always closes it and returns to play.
4. Every button has a tooltip with its name and a short description.
5. Destructive actions ask first (`ConfirmDialog`).
6. Show progress and status with cyan bars or rings.
7. Anything that changes the world goes to the host through the Action API.
8. Keep the focus order logical, ready for controller navigation.

Menus that must follow these rules include crafting station screens, upgrade panels, mod tool
property editors, quest dialogue and choice windows, Creation management screens and new settings
panels.
