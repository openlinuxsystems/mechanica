Mechanica
=========

Install
-------
Unzip this folder anywhere and keep everything in it together: the game reads mods/ (all the
game's content) and docs/ (the in-game Handbook) from the folder it runs in. Nothing else needs
installing; the .NET runtime is bundled.

  Windows: double-click Mechanica.exe. If SmartScreen says "Windows protected your PC", click
           "More info", then "Run anyway" (the build isn't code-signed).
  Linux:   run ./Mechanica.x86_64 (chmod +x it first if your unzip tool dropped the flag).

Your saves, settings and profile live in the user data folder, not here, so replacing this folder
with a newer version keeps them:
  Windows: %APPDATA%\Godot\app_userdata\Mechanica
  Linux:   ~/.local/share/godot/app_userdata/Mechanica

Playing together
----------------
Both players need the same version of the game (and the same mods).

  Host:   New game (tick "Open to LAN"), or in a world press Esc, then "Open to LAN".
          The host's firewall must allow the game: Windows asks the first time; click Allow.
  Friend: Join game. Games on the same network are listed; otherwise type the host's
          address, e.g. 192.168.1.20:27460.

The game uses UDP port 27460 (game) and 27462 (LAN discovery). To play over the internet, the
host forwards UDP 27460 on their router, or both players join the same virtual LAN (Tailscale,
ZeroTier, Radmin VPN) and the friend types the host's address on it.

Keys: WASD move, mouse look, Space jump, Shift sprint, F interact, Esc menu, F1 help.
