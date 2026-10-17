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
| Space | Jump |
| Shift | Sprint |
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
  scale from 75% to 150%. View distance (128 to 1024 m) is how far you see: the ground around players
  is simulated out to 128 m, and past that the land and its rocks and trees are drawn as scenery you
  can't touch, fading into fog at the view distance.
- **Audio**: master, music, sound effects and interface volume, mute all, and the output device.
- **Gameplay**: over a world, its difficulty (chosen in New Game, see [Survival](survival.md#difficulty)); mouse sensitivity, invert Y, field of view, tutorial hints (a short tip at the top of
  the screen the first time you start, pick up each tool or open each screen; **Show tips again** brings
  back the ones you have seen; the tips are in `game/data/tutorial.json`), auto-save (off, or every 5, 10 or 15
  minutes while you host), the most players a world you host takes, the server password for worlds you host, and your player name.
- **Interface**: show or hide the tool bar and key hints, the compass, other players' names over their heads, how opaque the chat background is, and a
  colour-blind filter (Protanopia, Deuteranopia or Tritanopia) that corrects colours on the whole screen
  so ones that look alike stay apart, and the **Language** of menus and the HUD. The game ships in English; a
  language file dropped into your `lang` folder (Options shows where) adds that language to the list. See
  [Translating](translating.md).

Everything else applies as you change it. **Reset to Defaults** resets the open tab after asking, and
closing Options saves your settings in `settings.json` in the game's user data folder. Settings are
yours alone: they never change the world or other players' games.

## Compass

The bar at the top of the screen shows which way you face, with the points and a tick every 15°. Blue
diamonds are your quest goals and the places you have found, with the distance under them within 500 m;
the primary one is larger and pulses cyan. With the logbook open (the mouse is free), click a diamond to
make it primary. A goal behind you sits at the edge you are nearer turning to, with an arrow. Within
100 m a diamond also floats over the place itself. The compass hides while the inventory or menu is open,
and Options, Interface, Show compass turns it off.

## Playing together

Everyone plays in one world: the host's. From the menu (Esc), **Open to LAN** lets players on your
network join; they see your world under **Join game** in the main menu, or can type your address and
port (27460 unless you changed it). Players outside your network join by address once the port is
forwarded to you. Quest progress is shared by everyone in the world.

Press **Enter** (Chat) to talk: type a line and press Enter to send it, or Esc to close. Lines show
bottom left with the speaker's name in cyan and fade after a few seconds; open chat to scroll back. A line
is at most 200 characters and each player may send 5 every 10 seconds. Chat is the `SendChat` action, so
agents on the AI endpoint can talk too.

To keep strangers out, set a **Server password** in Options, Gameplay (or start with `--password text`).
Players then type it in the password box on **Join game**; the list marks worlds that need one. A changed
password counts from the next player who joins. The password is sent unencrypted, so don't reuse a real one.

## Saving

Only the host saves. **Save game** in the menu writes the world, every player's inventory, health and
bed, and the story, to a file named after the world. **Load game** in the main menu starts it again. See
[Saves](saves.md).

## More

- [Survival](survival.md): health, items, crops, water and beds
- [Quests and Dialogue](quests.md)
- [Logic and Power Tutorial](logic-power-tutorial.md): seats, engines and wiring
- [Getting Started](getting-started.md) with the Part Editor
