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
| Blue | `#2E86DE` | borders, accents, title bars |
| Cyan | `#00D4FF` | hover, focus, selection, progress, scrollbars |
| Text | `#FFFFFF` | primary text |
| Secondary text | `#B0B8C1` | hints, paths, secondary details |
| Disabled | `#5A6470` | disabled controls |
| Error | `#E5534B` | errors, refusals, failed loads |
| Warning | `#E8E3A0` | warnings |
| Success | `#4CD07D` | success, running |

There is no orange anywhere, in menus, overlays or materials. A headless test (`game/tests/ui`)
fails if the theme or the main menu has any.

The palette lives in `src/Mechanica.Core/Ui/UiPalette.cs`. Game code reads it as Godot colours
through `UiTokens`.

## The theme

`game/scripts/Ui/MechanicaTheme.cs` builds the theme in code. The `UiRoot` autoload puts it on the
root window, so every control in every scene inherits it. Don't set colours on a screen: pick a
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
