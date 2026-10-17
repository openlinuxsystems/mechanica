# Project settings

`project.godot` is kept exactly as the Godot 4.7.2 editor writes it (no comments, the editor's section order,
defaults left out), so opening the project never rewrites it and pulls don't conflict with an editor-saved copy.
Change settings in the editor's Project Settings, or match its format. Why the non-default values are set:

- `display/window/size/*`, `stretch/*`: UI spec §11, the UI is laid out for 1280×720 and scales with the window
  (canvas_items) up to 4K.
- `gui/timers/tooltip_delay_sec=0.25`: UI spec §9 and §13, tooltips after 0.25 s of hover.
- `physics/jolt_physics_3d/limits/max_angular_velocity=150`: Jolt's default (15π rad/s) caps a 0.36 m wheel at
  17 m/s, below a plane's takeoff speed.
- `rendering/textures/vram_compression/import_etc2_astc=true`: the macOS (Apple Silicon) export requires it; game
  content loads from mods at run time, so it only affects `res://`.
- `dotnet/*`: the C# assembly name and the solution one folder up.
