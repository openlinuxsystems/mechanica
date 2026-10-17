# Translating

Every menu, HUD line and mod tool shows its text through string tables, so the game can be played in
any language someone writes a file for. The game ships in English.

## Adding a language

A language is one JSON file named for its code:

```json
{
  "code": "fr",
  "name": "Français",
  "strings": {
    "New game": "Nouvelle partie",
    "Hosting on port {0}.": "Hébergé sur le port {0}.",
    "Show compass": "Afficher la boussole"
  }
}
```

- `code` is a locale code: `fr`, `de`, `pt-BR`.
- `name` is the language's own name, as the Language list in Options shows it.
- `strings` maps the English text to the translation. Start from `game/data/lang/en.json`, which lists every
  key the game uses (each one maps to itself), and replace the right-hand sides. A key left out shows in English.
- `{0}`, `{1}`, … are values the game fills in (a port, a part name, a key). A translation must keep the same
  ones, in any order; a file that drops or adds one is not loaded, and the log says which string is wrong.
- BBCode tags such as `[b]` are kept as they are.

Put the file in the player's `lang` folder: `user://lang/` (on Linux
`~/.local/share/godot/app_userdata/Mechanica/lang/`; the Language tooltip in Options shows the full path).
Open Options, Interface, and pick it from **Language**; the UI changes at once. Files shipped with the game
live in `game/data/lang/`; a file in the player's folder with the same code wins.

## What is not translated

- Text players and mods write: player names, chat, save names, part, item, bot and quest names and
  descriptions, quest steps and dialogue. Mods ship in their author's language.
- The handbook pages (these docs), the Lua API list and the behaviour tree node help in the mod tools.
- Messages the host sends: an Action refused by the host (for example "out of reach"), a refused join, and
  validation problems the mod tools list come from the shared rules code in English, because the host and
  each client may use different languages and those messages carry values built on the host.
- Key names (`Left Click`, `Space`) and Godot's own file browser.

## For developers

UI text reaches the tables three ways:

- A literal set on a Label, Button, tooltip, placeholder, tab or list item (`Text = "Show compass"`) is
  translated by Godot's auto-translation as it is shown. Nothing else is needed.
- Text built at run time goes through `Loc.T("Hosting on port {0}.", port)`; never `$"..."` for words a
  player reads. Pass numbers already formatted (`$"{metres:0}"`).
- A control that shows player or mod text gets `Loc.Verbatim(control)`, so it is never looked up.

After adding or changing UI text, regenerate the English table and run the checks:

```sh
godot --headless --path game res://tests/language/LanguageTest.tscn -- --collect   # writes game/data/lang/en.json
godot --headless --path game res://tests/language/LanguageTest.tscn               # pseudo-locale walk
dotnet test Mechanica.sln                                                          # key checks
```

The language test opens every window (main menu and each screen, every Options tab, the mod tools, and in a
world the HUD, in-game menu, inventory, logbook, chat, compass, part settings, inspector and a confirm dialog)
in the pseudo-locale `qps`, where every string is accented, a third longer and bracketed (`⟦Shów çómpáss····⟧`).
Any text still in plain English skipped the tables, and the test names it. The Core tests check that every
literal `Loc.T` key is in `en.json` and that every key in `en.json` is written somewhere in the code or data
(a key that is not was built at run time and skipped `Loc.T`).
