# UI Style Guide

Every menu, HUD element and mod tool in Mechanica uses one blue industrial look. This page is for
anyone adding a screen to the game or a mod tool. It covers the palette, the theme, the shared
building blocks and the rules each new menu follows.

The look is moving to a lighter design language (UI spec §0), first used by the Fabricator and the hotbar. New
screens use it, and older screens move to it one at a time. Until a screen moves it keeps the original palette and
`MechanicaTheme` described further down.

## Design language

A light blue browsing surface with picture tiles, dark chrome headers and dark detail cards, over a dimmed world.
The Fabricator (UI spec §8) is the reference screen; the mockups are `ui_redesign/proposed_fabricator.png` and
`proposed_fabricator_storage.png` in the project files.

### Palette

| Token | Colour | Use |
|-------|--------|-----|
| Scrim | `#0A1220` at 45% opacity | behind any full-screen menu, dims the world (not the hotbar) |
| Chrome | `#1E2F48` | window headers, the tab row, inactive tabs |
| Chrome 2 | `#263A57` | buttons and fields on chrome |
| Chrome line | `#34507A` | the few hairlines left: field edges, chips, secondary buttons |
| Surface | `#4B9BE6` | the browsing surface: grids, lists, the active tab |
| Surface deep | `#3A86D2` | bands on the surface: the queue strip, drawers |
| Tile | `#6CB2EE` | item and recipe tiles, hotbar slots |
| Tile hover | `#86C1F4` | a hovered tile |
| Tile selected | `#9ACDF7` with a white 3 px ring | the selected tile, the current hotbar slot |
| Card | `#22344F` | detail cards, dialogs, tooltips |
| Inset | `#2B4060` | panels inside a card: stats, material tiles, the stepper |
| Ink | `#0E2340` | text and numbers on Surface and Tile; the outline round item pictures |
| Text muted | `#B7C7DC` | secondary text on chrome and cards |
| Accent | `#38C8FF` | primary buttons, progress, focus, filled pips, the level pill, NEW tags |
| Accent ink | `#0B1E33` | text on Accent |
| Success text | `#5EE08A` | ready, enough, running (the linked storage dot) |
| Success fill | `#7FE0A6` | finished items waiting in the queue strip |
| Error text | `#FF7A7A` | short counts, reasons an action is off |
| Error mark | `#E5484D` | "can't make yet" dots, the ring round a short material |
| Pip off | `#3A5070` | unfilled stat pips; also the disabled primary button's fill |
| Disabled text | `#8EA3BF` | text on a disabled primary button |
| Category tools | `#F4E15C` | the Tools tab's dot and the dot on its items' tiles (lemon, never orange) |
| Category blocks | `#EAF4FF` | the Blocks tab's dot and its items' dots |
| Category interactive | `#B98CFF` | the Interactive tab's dot and its items' dots |
| Category parts | `#62E0A0` | the Parts tab's dot and its items' dots |
| Category consumable | `#FF8ACF` | the Consumable tab's dot and its items' dots |

White (`#FFFFFF`) is the text on chrome and cards and the selected ring, and warnings stay the pale yellow below.

- White text never sits on Surface or Tile: use Ink. Core tests check every text pairing reaches 4.5:1 contrast.
- Panels are opaque. The scrim does the dimming, and nothing from the world shows through a panel.
- Colour is never the only signal: a state that uses colour also gets a dot, ring, icon or text.

### Type

The game bundles Barlow and Barlow Condensed (SIL OFL, `game/fonts`, loaded by `UiFonts`).

| Role | Face | Size at 1280×720 | Variation | Use |
|------|------|------------------|-----------|-----|
| Display | Barlow Condensed Bold, uppercase, +3 px tracking | 30 | `DisplayLabel` | window titles |
| Heading | Barlow Condensed Bold | 24 | `HeadingLabel` | item names in cards |
| Section | Barlow Condensed Bold, uppercase, +2 px tracking | 13 | `SectionLabel`, `InkSectionLabel` | MATERIALS, QUEUE, INVENTORY |
| Body | Barlow Regular / SemiBold | 15 / 13 | default, `MutedLabel`, `InkLabel` | labels, descriptions |
| Numeric | Barlow Condensed Bold | 15 | `NumericLabel`, `InkNumericLabel` | counts, have / need, timers |

### Shape and depth

- A 4 px grid: window padding 16, gaps between tiles 8, gaps between sections 12.
- Corners: 14 for windows and cards, 8 for inner panels and tiles, 10 for buttons, round for pills.
- Depth comes from fill and a 3 px darker bottom edge on tiles and primary buttons. Windows and cards have no drop
  shadow (no soft translucent edge round a menu). Borders are the exception: only fields, focus rings and the selected ring have them.
- Item pictures (`ItemIcons.Finish`, `IconFinish`) are cropped to the item, scaled so its largest side fills 80% of
  the tile, and get a 2 px Ink outline and a soft contact shadow so pale parts still read on a light tile.

### The station theme

`game/scripts/Ui/StationTheme.cs` builds the design language as a theme. A screen that uses it sets
`Theme = StationTheme.Theme` on its root; everything under that root inherits it. Pick a variation, never a colour.

| Variation | Base | Use |
|-----------|------|-----|
| `MainWindow`, `Header`, `TabStrip`, `SurfacePanel` | PanelContainer | the parts of a station window |
| `Drawer`, `QueueBand` | PanelContainer | bands on the surface |
| `CardPanel`, `InsetPanel` | PanelContainer | a detail card and the panels in it |
| `Pill`, `WarningPill`, `KeyCap` | PanelContainer | level pill and NEW tag, warnings, key hints |
| `PrimaryButton` | Button | the main action (Accent; a flat slate fill when off, never just an outline) |
| plain `Button` | Button | secondary actions on chrome (Chrome 2, white text, hairline) |
| `CloseButton`, `StepperButton`, `StationTab`, `RecipeTile` | Button | the X, − and +, category tabs, recipe tiles |
| `StepperField` | LineEdit | the number in a quantity stepper |

Hover is one step lighter and pressed one step darker. Scrollbars are 8 px and white. Tooltips are a Card panel.

### Components

`game/scripts/Ui/StationControls.cs` holds the small controls screens share:

- **`KeyButton`**: a button with a key cap after its label ("Upgrade U"). Set `Key` from `GameInput.Label`, so it
  follows the player's bindings.
- **`QuantityStepper`**: − 1 +, Shift+click for ±10, and the number can be typed.
- **`PipBar`**: a stat's 10 pips. `ItemStats.Rows` turns a part's mass, durability, friction and buoyancy into pips and
  SI values; the pip scale is `stat_pips` in `game/data/hud.json`.
- **`QueueCell`**: a chevron cell of a progress strip, filled by its state, with progress in Accent.
- **`ItemSlot` with `Tiled`**: an item tile in Tile blue with Ink counts. The hotbar, the Inventory window, containers and
  the Storage drawer use it. `Dot` puts the item's category dot (`UiTokens.CategoryDot`) in its bottom-left corner,
  ringed in Ink so every colour reads on Tile.
- **`UiGlyph`**: small line icons drawn in code (the Inventory window's satchel, the search magnifier, the Organize
  sort icon), so they scale with the UI and need no textures.

A reason line (Error text) sits under any action that is off and says why, for example "Needs 1 more Scrap Metal."

### Layout

- A station window is a main window (header, tabs, tile grid, progress strip) and a 302 px detail card 12 px to its
  right: 886 + 302 at 1280×720, 596 tall, centred above the hotbar, which stays visible and undimmed. Wider windows
  give the grid more columns; below 1280 the card narrows to 280 px.
- Tiles are 76 px with 8 px gaps and show a picture only; the name is in the tooltip and the card.
- There is no always-visible inventory column. A drawer slides over the left half of the grid when it is needed.
- The Inventory window is a main window ten tiles wide and as tall as a station window: a header with the satchel icon,
  the INVENTORY title and fill pill, the search field, Organize and X; the category tabs (each with its dot); the tile
  grid. An open container has its own screen (the container over the inventory), with Organize in its header.
- The hotbar's slots are Tile blue with Ink numbers and counts, 6 px apart. The current slot is Tile selected with the
  white 3 px ring, exactly like a selected recipe.

## Original palette

Screens that have not moved to the design language yet use these colours and `MechanicaTheme`.

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
| Health | `#36B36B` | the health bar fill, lighter at the top and darker at the bottom (`ShadedBox`) |
| GaugeGas | `#DB4A41` | a Gas vehicle gauge's fill (shaded like Health) |
| GaugeElectric | `#2E86DE` | a battery vehicle gauge's fill (the interface Blue) |
| GaugeChemical | `#8CCB3A` | a Chemical vehicle gauge's fill |
| GaugeWater | `#4FC3DE` | a Water vehicle gauge's fill |
| GaugeAmmo | `#CBBF3A` | an Ammo vehicle gauge's fill (mustard, clear of orange) |

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

Tool previews (a placed part's ghost) are cyan at 35% opacity.

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
- **`MechanicaTheme.SelectedSlot()`** returns the bright cyan border for a selected slot or item on a screen that
  has not moved to the design language.

## Layout

- The UI is laid out for 1280×720 and the window stretches it (`canvas_items`), so it scales cleanly
  up to 4K. The window can't be made smaller than 1280×720. The UI scale setting (75% to 150%) applies
  on top of that.
- Use containers and anchors, never fixed positions. Station screens follow the design language layout above.
  Mod tools keep balanced proportions, for example 30/40/30 for list, preview and properties.
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
2. Primary actions at the bottom or right: cyan when enabled, a flat slate fill with a reason line when disabled.
3. The key that opens a menu also closes it, and Esc always closes it and returns to play.
4. Every button has a tooltip with its name and a short description.
5. Destructive actions ask first (`ConfirmDialog`).
6. Show progress and status with cyan bars or rings.
7. Anything that changes the world goes to the host through the Action API.
8. Keep the focus order logical, ready for controller navigation.

Menus that must follow these rules include crafting station screens, upgrade panels, mod tool
property editors, quest dialogue and choice windows, Creation management screens and new settings
panels.
