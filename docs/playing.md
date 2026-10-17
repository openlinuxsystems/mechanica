# How to Play

You start at the crash site. The quest log says what to do next, and Use (**E**) does most things in
the world. Press **H** (or **F1**) at any time to open this help; it opens on the page for what you are
doing (the quest log, your inventory, the Part Editor, ...). Type in the box at the top of the help to
search every page.

## Controls

These are the default keys. Every one can be changed in Options, Controls.

| Key | What it does |
|-----|--------------|
| W A S D | Walk |
| Space | Jump. In deep water, swim up; at the surface, jump out. |
| Shift | Sprint (and swim faster) |
| Ctrl | Crouch: move slowly. In deep water, dive. |
| Mouse | Look. Click the world to take the mouse back after a menu. |
| X / C | Zoom in to first person / out to third person (also Alt + scroll) |
| 1 to 7 | Pick a tool: Hammer, Lift, Weld, Paint, Sledgehammer, item in hand, Connect Tool |
| Left click | Create, attack or place: the Hammer places parts, the Sledgehammer swings |
| Right click | Remove or pick up: the Hammer removes parts, the Lift takes its Creation away |
| Scroll | Hammer: previous or next part. Item in hand: previous or next item. |
| Q / Shift + Q | Turn the part (Hammer, Weld, Lift) or pick the next or previous colour (Paint) |
| T | Hammer and Weld: tip the part over |
| Up / Down arrows | Raise or lower the Lift |
| G | Drop one of the item in hand |
| E | Use: open a container or station, pick up items, harvest, sleep in a bed, sit in or leave a seat, use a part, change a part's settings, use a quest marker, talk, respawn after dying |
| I or B | Inventory |
| L | Logbook: the quest log, where you pick an answer when a quest asks you to decide |
| Esc | Menu: save, load, open your world to the LAN, Options, help, quit. Esc also closes any open window. |
| H or F1 | Help |
| F7 / F8 / F9 | Logic Visualizer, Creation Inspector and force overlay |

In water deeper than about chest height you swim: there is no ground to stand on, you float with your
head out, Space rises and Ctrl dives. Shallower water slows you down.

While a menu or the inventory is open, your character stands still, and a Creation you are driving
stops getting your input.

## Options

**Options** is on the main menu and in the menu (Esc). Its tabs:

- **Controls**: every action with a primary and an alternate key. Click a key, then press the new key
  or mouse button (Esc cancels). If another action already has it, choose **Swap** to trade keys or
  **Overwrite** to take it; actions that share a key show in red. Search at the top; each category and
  the whole list can be reset.
- **Graphics**: resolution, display mode (windowed, borderless, fullscreen) and VSync, which change
  when you press **Apply** and go back after 15 seconds unless you keep them; a quality preset (Low,
  Medium, High, Ultra, or Custom for view distance, shadows, anti-aliasing, textures and effects); UI
  scale from 75% to 150%.
- **Audio**: master, music, sound effects and interface volume, mute all, and the output device.
- **Gameplay**: mouse sensitivity, invert Y, field of view, auto-save (off, or every 5, 10 or 15
  minutes while you host), the most players a world you host takes, and your player name.
- **Interface**: show or hide the tool bar and key hints.

Everything else applies as you change it. **Reset to Defaults** resets the open tab after asking, and
closing Options saves your settings in `settings.json` in the game's user data folder. Settings are
yours alone: they never change the world or other players' games.

## Your character

The first time you play in a world you choose the **Male Scrapper** or the **Female Scrapper** (on the New
game screen when you start the world, or in a window when you join one). They move and play the same; the
choice is kept for you in that world and everyone sees it. See [Player Characters](characters.md).

## Playing together

Everyone plays in one world: the host's. From the menu (Esc), **Open to LAN** lets players on your
network join; they see your world under **Join game** in the main menu, or can type your address and
port (27460 unless you changed it). Players outside your network join by address once the port is
forwarded to you. Quest progress is shared by everyone in the world.

## Saving

Only the host saves. **Save game** in the menu writes the world, every player's inventory, health and
bed, and the story, to a file named after the world. **Load game** in the main menu starts it again. See
[Saves](saves.md).

## More

- [Survival](survival.md): health, items, crops, water and beds
- [Quests and Dialogue](quests.md)
- [Logic and Power Tutorial](logic-power-tutorial.md): seats, engines and wiring
- [Getting Started](getting-started.md) with the Part Editor
