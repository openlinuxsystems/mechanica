using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Net;
using Mechanica.Core.Saves;
using Mechanica.Core.Settings;
using Mechanica.Core.Survival;
using Mechanica.Core.Ui;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;
using DisplayMode = Mechanica.Core.Settings.DisplayMode;

namespace Mechanica.Game.Menus;

/// <summary>
/// Options (UI spec §16), from the main menu and the in-game menu. Tabs down the left: Controls (every
/// action, searchable, click a key and press the new one, conflicts offer swap or overwrite), Graphics,
/// Audio, Gameplay and Interface. Changes apply as they are made and are saved on close, except the
/// window's size, mode and VSync, which wait for Apply and go back after 15 seconds unless kept. Reset to
/// Defaults resets the open tab after asking. The open tab is remembered between visits.
/// </summary>
public partial class OptionsScreen : ToolWindow
{
    /// <summary>The tabs, in order.</summary>
    public static readonly string[] Tabs = ["Controls", "Graphics", "Audio", "Gameplay", "Interface"];

    /// <summary>Seconds a new window setting waits to be kept.</summary>
    public const double RevertSeconds = 15;

    private static int _lastTab;

    private readonly Dictionary<string, VBoxContainer> _pages = [];
    private readonly Dictionary<string, Button> _tabButtons = [];
    private readonly List<(InputActionDef Def, Control Row, Label Name, Button Primary, Button Alternate)> _bindingRows = [];
    private UserSettings _draft = null!;
    private Keymap _keymap = null!;
    private (string Action, BindingSlot Slot, Button Button)? _capture;
    private ConfirmDialog? _revert;
    private GraphicsSettings? _revertTo;
    private double _revertLeft;
    private LineEdit _search = null!;
    private LineEdit _name = null!;
    private Button _apply = null!;
    private VBoxContainer _custom = null!;

    /// <summary>The profile file for the player's name; null uses <see cref="Main.ProfilePath"/>.</summary>
    public string? ProfileFile { get; init; }

    /// <summary>The world's game mode and difficulty when opened over a world (shown, not changed, on Gameplay); null from the main menu.</summary>
    public (GameMode Mode, Difficulty Difficulty)? World { get; init; }

    /// <summary>The settings as edited so far.</summary>
    public UserSettings Draft => _draft;

    /// <summary>The open tab.</summary>
    public string CurrentTab => Tabs[_lastTab];

    /// <summary>The player name box.</summary>
    public LineEdit PlayerName => _name;

    /// <summary>The action waiting for a key, or null.</summary>
    public string? Capturing => _capture?.Action;

    /// <summary>True while a window change waits to be kept or reverted.</summary>
    public bool RevertPending => _revert is not null;

    private string ProfilePath => ProfileFile ?? ProjectSettings.GlobalizePath(Main.ProfilePath);

    public override void _Ready()
    {
        Title = "Options";
        Name = "Options";
        _draft = GameSettings.Current;
        _keymap = _draft.Keymap();
        CustomMinimumSize = new Vector2(860, 560);
        var split = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        Body.AddChild(split);
        var side = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0) };
        split.AddChild(side);
        var group = new ButtonGroup();
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        split.AddChild(scroll);
        var pages = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(pages);
        for (var i = 0; i < Tabs.Length; i++)
        {
            var index = i;
            var button = new Button { Text = Tabs[i], ToggleMode = true, ButtonGroup = group, CustomMinimumSize = new Vector2(0, 40), Alignment = HorizontalAlignment.Left };
            button.Pressed += () => ShowTab(index);
            side.AddChild(button);
            _tabButtons[Tabs[i]] = button;
            var page = new VBoxContainer { Name = Tabs[i], SizeFlagsHorizontal = SizeFlags.ExpandFill, Visible = false };
            page.AddThemeConstantOverride("separation", 8);
            pages.AddChild(page);
            _pages[Tabs[i]] = page;
        }
        BuildControls(_pages["Controls"]);
        BuildGraphics(_pages["Graphics"]);
        BuildAudio(_pages["Audio"]);
        BuildGameplay(_pages["Gameplay"]);
        BuildInterface(_pages["Interface"]);
        AddAction("Reset to Defaults", AskReset, tooltip: "Put this tab's settings back to their defaults");
        _apply = AddAction("Apply", ApplyWindow, primary: true, tooltip: "Use the new resolution, display mode and VSync");
        AddAction("Close", RequestClose, tooltip: "Save and close (Esc)");
        CloseGuard = go =>
        {
            Commit();
            go();
        };
        ShowTab(_lastTab);
        UpdateApply();
    }

    public override void _Input(InputEvent @event)
    {
        if (_capture is { } capture)
        {
            if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
            {
                _capture = null;
                RefreshBindings();
            }
            else if (GameInput.ToChord(@event) is { } chord)
            {
                _capture = null;
                Bind(capture.Action, capture.Slot, chord);
            }
            else
                return;
            GetViewport().SetInputAsHandled();
            return;
        }
        base._Input(@event);
    }

    public override void _Process(double delta)
    {
        if (_revert is null)
            return;
        _revertLeft -= delta;
        if (_revertLeft <= 0)
            _revert.Cancel();
    }

    /// <summary>Opens a tab by index.</summary>
    public void ShowTab(int index)
    {
        _lastTab = Math.Clamp(index, 0, Tabs.Length - 1);
        foreach (var (name, page) in _pages)
            page.Visible = name == Tabs[_lastTab];
        _tabButtons[Tabs[_lastTab]].ButtonPressed = true;
    }

    /// <summary>Starts waiting for a key for one binding, as clicking its key field does.</summary>
    public void StartCapture(string action, BindingSlot slot)
    {
        var row = _bindingRows.First(r => r.Def.Id == action);
        var button = slot == BindingSlot.Primary ? row.Primary : row.Alternate;
        _capture = (action, slot, button);
        button.Text = "Press any key…";
    }

    /// <summary>
    /// Binds <paramref name="chord"/>; when another action has it, asks whether to swap the two or take it
    /// over (§16 "Conflict detection").
    /// </summary>
    public void Bind(string action, BindingSlot slot, InputChord chord)
    {
        var conflicts = _keymap.ConflictsWith(action, chord);
        if (conflicts.Count == 0)
        {
            SetBinding(action, slot, chord, swap: false);
            return;
        }
        var others = string.Join(", ", conflicts.Select(c => InputActions.Find(c.Action) is { } d ? ActionName(d) : c.Action));
        var dialog = ConfirmDialog.Ask(this, "Key in use", Loc.T("{0} is already used for {1}. Swap the keys, or take it over and leave {1} without it?", chord.Display(), others),
            "Overwrite", () => SetBinding(action, slot, chord, swap: false), RefreshBindings);
        dialog.AddChoice("Swap", () => SetBinding(action, slot, chord, swap: true));
    }

    /// <summary>Saves everything and the player's name; Close does this too.</summary>
    public void Commit()
    {
        if (_revert is not null)
            _revert.Cancel();
        GameSettings.Save(WithSeen(_draft));
        var profile = Main.UserProfile(_name.Text, ProfilePath);
        _name.Text = profile.Name;
    }

    /// <summary>Applies the window settings, then asks to keep them; they go back after <see cref="RevertSeconds"/>.</summary>
    public void ApplyWindow()
    {
        _revertTo = GameSettings.Current.Graphics;
        GameSettings.Apply(WithSeen(_draft));
        GameSettings.ApplyWindow();
        _revertLeft = RevertSeconds;
        _revert = ConfirmDialog.Ask(this, "Display settings", Loc.T("Keep these display settings? They go back in {0} seconds.", $"{RevertSeconds:0}"),
            "Keep", () =>
            {
                _revert = null;
                GameSettings.Save(WithSeen(_draft));
                UpdateApply();
            },
            () =>
            {
                _revert = null;
                _draft = _draft with { Graphics = _draft.Graphics with { Resolution = _revertTo.Resolution, DisplayMode = _revertTo.DisplayMode, VSync = _revertTo.VSync } };
                GameSettings.Apply(WithSeen(_draft));
                GameSettings.ApplyWindow();
                RebuildPage("Graphics", BuildGraphics);
                UpdateApply();
            }, destructive: false);
    }

    // ---- Controls ----

    private void BuildControls(VBoxContainer page)
    {
        var top = new HBoxContainer();
        page.AddChild(top);
        _search = new LineEdit { PlaceholderText = "Search controls", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _search.TextChanged += _ => FilterBindings();
        top.AddChild(_search);
        var resetAll = new Button { Text = "Reset All Controls", TooltipText = "Put every key back to its default" };
        resetAll.Pressed += () => ConfirmDialog.Ask(this, "Reset controls", "Put every key back to its default?", "Reset", () =>
        {
            _keymap.ResetAll();
            KeymapChanged();
        });
        top.AddChild(resetAll);
        page.AddChild(new Label { Text = "Keyboard and mouse", ThemeTypeVariation = "HeaderSmall" });
        page.AddChild(new Label { Text = "Click a key to change it, then press the new key or mouse button. Esc cancels.", ThemeTypeVariation = "SecondaryLabel" });
        foreach (var category in InputActions.All.Select(a => a.Category).Distinct())
        {
            var header = new HBoxContainer();
            header.AddChild(new Label { Text = category, ThemeTypeVariation = "TitleLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var reset = new Button { Text = "Reset Category", TooltipText = Loc.T("Put the {0} keys back to their defaults", Loc.T(category)) };
            reset.Pressed += () =>
            {
                _keymap.ResetCategory(category);
                KeymapChanged();
            };
            header.AddChild(reset);
            page.AddChild(header);
            foreach (var def in InputActions.All.Where(a => a.Category == category))
            {
                var row = new HBoxContainer();
                var name = new Label { Text = ActionName(def), SizeFlagsHorizontal = SizeFlags.ExpandFill };
                row.AddChild(name);
                var primary = KeyButton(def.Id, BindingSlot.Primary);
                var alternate = KeyButton(def.Id, BindingSlot.Alternate);
                row.AddChild(primary);
                row.AddChild(alternate);
                page.AddChild(row);
                _bindingRows.Add((def, row, name, primary, alternate));
            }
        }
        RefreshBindings();
    }

    // An action's name in the language in use; the numbered hotbar slots share one key.
    private static string ActionName(InputActionDef def) =>
        def.Id.StartsWith("hotbar_", StringComparison.Ordinal) && int.TryParse(def.Id["hotbar_".Length..], out var slot)
            ? Loc.T("Hotbar {0}", slot)
            : Loc.T(def.Name);

    private Button KeyButton(string action, BindingSlot slot)
    {
        // Key names show as the keyboard has them.
        var button = Loc.Verbatim(new Button { CustomMinimumSize = new Vector2(170, 32), TooltipText = Loc.T(slot == BindingSlot.Primary ? "Primary key: click to change" : "Alternate key: click to change") });
        button.SetMeta(HoverScale.OptOut, true);
        button.Pressed += () => StartCapture(action, slot);
        return button;
    }

    private void SetBinding(string action, BindingSlot slot, InputChord chord, bool swap)
    {
        _keymap.Set(action, slot, chord, swap);
        KeymapChanged();
    }

    private void KeymapChanged()
    {
        _draft = _draft.WithKeymap(_keymap);
        GameSettings.Apply(WithSeen(_draft));
        RefreshBindings();
    }

    private void RefreshBindings()
    {
        var conflicting = _keymap.Conflicting();
        foreach (var (def, _, name, primary, alternate) in _bindingRows)
        {
            primary.Text = _keymap.Get(def.Id)?.Display() ?? "—";
            alternate.Text = _keymap.Get(def.Id, BindingSlot.Alternate)?.Display() ?? "—";
            name.ThemeTypeVariation = conflicting.Contains(def.Id) ? "ErrorLabel" : "";
            name.TooltipText = conflicting.Contains(def.Id) ? "Another control uses the same key." : "";
        }
        FilterBindings();
    }

    private void FilterBindings()
    {
        var term = _search.Text.Trim();
        foreach (var (def, row, _, primary, alternate) in _bindingRows)
        {
            row.Visible = term.Length == 0
                || def.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || ActionName(def).Contains(term, StringComparison.OrdinalIgnoreCase)
                || def.Category.Contains(term, StringComparison.OrdinalIgnoreCase)
                || Loc.T(def.Category).Contains(term, StringComparison.OrdinalIgnoreCase)
                || primary.Text.Contains(term, StringComparison.OrdinalIgnoreCase)
                || alternate.Text.Contains(term, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- Graphics ----

    private void BuildGraphics(VBoxContainer page)
    {
        var g = _draft.Graphics;
        page.AddChild(new Label { Text = "Window", ThemeTypeVariation = "TitleLabel" });
        var resolutions = Resolutions();
        var current = g.Resolution ?? $"{DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}";
        if (!resolutions.Contains(current))
            resolutions.Insert(0, current);
        Option(page, "Resolution", resolutions, resolutions.IndexOf(current), i => SetGraphics(x => x with { Resolution = resolutions[i] }), "Window size in windowed mode. Needs Apply.");
        Option(page, "Display mode", ["Windowed", "Borderless", "Fullscreen"], (int)g.DisplayMode,
            i => SetGraphics(x => x with { DisplayMode = (DisplayMode)i }), "Needs Apply.");
        Toggle(page, "VSync", g.VSync, v => SetGraphics(x => x with { VSync = v }), "Wait for the display's refresh. Needs Apply.");
        page.AddChild(new Label { Text = "Quality", ThemeTypeVariation = "TitleLabel" });
        string[] levels = ["Low", "Medium", "High", "Ultra"];
        Option(page, "Preset", [.. levels, "Custom"], (int)g.Preset, i =>
        {
            SetGraphics(x => x.WithPreset((Quality)i));
            RebuildPage("Graphics", BuildGraphics);
        });
        _custom = new VBoxContainer { Visible = g.Preset == Quality.Custom };
        page.AddChild(_custom);
        Slider(_custom, "View distance", g.ViewDistance, GraphicsSettings.MinViewDistance, GraphicsSettings.MaxViewDistance, 32, v => SetGraphics(x => x with { ViewDistance = v }), v => Loc.T("{0} m", $"{v:0}"));
        Option(_custom, "Shadows", levels, (int)g.Shadows, i => SetGraphics(x => x with { Shadows = (Quality)i }));
        Option(_custom, "Anti-aliasing", levels, (int)g.Antialiasing, i => SetGraphics(x => x with { Antialiasing = (Quality)i }));
        Option(_custom, "Textures", levels, (int)g.Textures, i => SetGraphics(x => x with { Textures = (Quality)i }));
        Option(_custom, "Effects", levels, (int)g.Effects, i => SetGraphics(x => x with { Effects = (Quality)i }));
        page.AddChild(new Label { Text = "Foliage density", ThemeTypeVariation = "TitleLabel" });
        string[] foliage = ["Off", "Low", "Medium", "High", "Very high"];
        Option(page, "Grass", foliage, (int)g.Grass, i => SetGraphics(x => x with { Grass = (FoliageLevel)i }), "How much grass is drawn, and how far. Lower it for a faster frame rate.");
        Option(page, "Ground cover", foliage, (int)g.GroundCover, i => SetGraphics(x => x with { GroundCover = (FoliageLevel)i }), "Clover and small flowers under the grass.");
        Option(page, "Bushes", foliage, (int)g.Bushes, i => SetGraphics(x => x with { Bushes = (FoliageLevel)i }), "Decorative bushes. Bushes that drop seeds always show.");
        page.AddChild(new Label { Text = "Interface size", ThemeTypeVariation = "TitleLabel" });
        Slider(page, "UI scale", _draft.UiScale, UiPalette.MinUiScale, UiPalette.MaxUiScale, 0.05f, v => Live(_draft with { UiScale = v }), v => $"{v * 100:0}%");
    }

    private static List<string> Resolutions()
    {
        var screen = DisplayServer.ScreenGetSize();
        var all = new[] { (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160) };
        var fit = all.Where(r => screen.X <= 0 || (r.Item1 <= screen.X && r.Item2 <= screen.Y)).Select(r => $"{r.Item1}x{r.Item2}").ToList();
        return fit.Count > 0 ? fit : ["1280x720"];
    }

    private void SetGraphics(Func<GraphicsSettings, GraphicsSettings> change)
    {
        Live(_draft with { Graphics = change(_draft.Graphics).Clamped() });
        if (_custom is not null)
            _custom.Visible = _draft.Graphics.Preset == Quality.Custom;
        UpdateApply();
    }

    private void UpdateApply()
    {
        if (_apply is null)
            return;
        var g = _draft.Graphics;
        _apply.Disabled = _revert is not null || (g.Resolution == WindowApplied.Resolution && g.DisplayMode == WindowApplied.DisplayMode && g.VSync == WindowApplied.VSync);
    }

    // The window settings last put into effect.
    private static GraphicsSettings WindowApplied => GameSettings.WindowSettings;

    // ---- Audio ----

    private void BuildAudio(VBoxContainer page)
    {
        var a = _draft.Audio;
        Slider(page, "Master", a.Master, 0, 1, 0.01f, v => SetAudio(x => x with { Master = v }), Percent);
        Slider(page, "Music", a.Music, 0, 1, 0.01f, v => SetAudio(x => x with { Music = v }), Percent);
        Slider(page, "Sound effects", a.Sfx, 0, 1, 0.01f, v => SetAudio(x => x with { Sfx = v }), Percent);
        Slider(page, "Interface", a.Ui, 0, 1, 0.01f, v => SetAudio(x => x with { Ui = v }), Percent);
        Toggle(page, "Mute all", a.Mute, v => SetAudio(x => x with { Mute = v }));
        var devices = AudioServer.GetOutputDeviceList().ToList();
        if (devices.Count > 1)
        {
            var at = Math.Max(0, devices.IndexOf(a.OutputDevice ?? "Default"));
            Option(page, "Output device", devices, at, i => SetAudio(x => x with { OutputDevice = devices[i] == "Default" ? null : devices[i] }));
        }
    }

    private static string Percent(float v) => $"{v * 100:0}%";

    private void SetAudio(Func<AudioSettings, AudioSettings> change) => Live(_draft with { Audio = change(_draft.Audio).Clamped() });

    // ---- Gameplay ----

    private void BuildGameplay(VBoxContainer page)
    {
        if (World is { } world)
        {
            page.AddChild(new Label { Text = "This world", ThemeTypeVariation = "TitleLabel" });
            var difficulty = world.Mode == GameMode.Creative ? "None (Creative)" : world.Difficulty.ToString();
            Row(page, "Difficulty", "Chosen when the world was made, in New Game; it stays with the world.")
                .AddChild(new Label { Name = "WorldDifficulty", Text = difficulty, ThemeTypeVariation = "SecondaryLabel" });
        }
        page.AddChild(new Label { Text = "Camera", ThemeTypeVariation = "TitleLabel" });
        Slider(page, "Mouse sensitivity", _draft.MouseSensitivity * 1000, 0.2f, 20f, 0.1f, v => Live(_draft with { MouseSensitivity = v / 1000 }), v => $"{v:0.0}");
        Toggle(page, "Invert Y", _draft.InvertY, v => Live(_draft with { InvertY = v }));
        Slider(page, "Field of view", _draft.FieldOfView, UserSettings.MinFieldOfView, UserSettings.MaxFieldOfView, 1, v => Live(_draft with { FieldOfView = v }), v => $"{v:0}°");
        page.AddChild(new Label { Text = "Help", ThemeTypeVariation = "TitleLabel" });
        Toggle(page, "Show tutorial hints", _draft.Gameplay.ShowTutorialHints, v => Live(_draft with { Gameplay = _draft.Gameplay with { ShowTutorialHints = v } }),
            "A short tip the first time you start, pick up each tool or open each screen.");
        var again = new Button { Name = "Show tips again", Text = "Show tips again", Disabled = (GameSettings.Current.Gameplay.SeenHints?.Count ?? 0) == 0 };
        again.Pressed += () => ConfirmDialog.Ask(this, "Show tips again", "Show every tutorial tip again as you play?", "Show again", () =>
        {
            GameSettings.Save(GameSettings.Current with { Gameplay = GameSettings.Current.Gameplay with { SeenHints = null } });
            again.Disabled = true;
        }, destructive: false);
        Row(page, "Tips already seen", "Tips show once each. This brings them all back.").AddChild(again);
        page.AddChild(new Label { Text = "Saving", ThemeTypeVariation = "TitleLabel" });
        var choices = GameplaySettings.AutoSaveChoices;
        Option(page, "Auto-save", [.. choices.Select(m => m == 0 ? Loc.T("Off") : Loc.T("Every {0} min", m))], choices.ToList().IndexOf(_draft.Gameplay.AutoSaveMinutes),
            i => Live(_draft with { Gameplay = _draft.Gameplay with { AutoSaveMinutes = choices[i] } }), "How often a world you host saves itself.");
        page.AddChild(new Label { Text = "Multiplayer", ThemeTypeVariation = "TitleLabel" });
        Slider(page, "Max players (host)", _draft.Gameplay.MaxPlayers, GameplaySettings.MinPlayers, GameplaySettings.MaxMaxPlayers, 1,
            v => Live(_draft with { Gameplay = _draft.Gameplay with { MaxPlayers = (int)v } }), v => $"{v:0}");
        var password = new LineEdit
        {
            Name = "Server password", Text = _draft.Gameplay.ServerPassword, Secret = true, MaxLength = NetPassword.MaxLength,
            PlaceholderText = "none: anyone on your network can join", SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        password.TextChanged += t => Live(_draft with { Gameplay = _draft.Gameplay with { ServerPassword = t } });
        Row(page, "Server password (host)", "Players must type this to join a world you open to the LAN. It is not encrypted, so don't reuse a real password.").AddChild(password);
        var row = Row(page, "Player name", "Other players see this name. Saved worlds know you by your profile, so you can change it any time.");
        _name = new LineEdit { Text = Main.UserProfile(null, ProfilePath).Name, MaxLength = PlayerProfile.MaxNameLength, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(_name);
    }

    // ---- Interface ----

    private void BuildInterface(VBoxContainer page)
    {
        Toggle(page, "Show the tool bar and key hints", _draft.Interface.ShowHotbar, v => Live(_draft with { Interface = _draft.Interface with { ShowHotbar = v } }));
        Toggle(page, "Show compass", _draft.Interface.ShowCompass, v => Live(_draft with { Interface = _draft.Interface with { ShowCompass = v } }),
            "The compass bar at the top of the screen, with quest goals and places you've found.");
        Toggle(page, "Show player names", _draft.Interface.ShowPlayerNames, v => Live(_draft with { Interface = _draft.Interface with { ShowPlayerNames = v } }),
            "Other players' names over their heads.");
        var modes = Enum.GetValues<ColorBlindMode>();
        Option(page, "Colour-blind filter", [.. modes.Select(m => m == ColorBlindMode.Off ? "Off" : m.ToString())], Array.IndexOf(modes, _draft.Interface.ColorBlind),
            i => Live(_draft with { Interface = _draft.Interface with { ColorBlind = modes[i] } }),
            "Corrects colours on the whole screen so ones that look alike stay apart: Protanopia (red), Deuteranopia (green), Tritanopia (blue).");
        var languages = Loc.Refresh();
        Loc.Verbatim(Option(page, "Language", [.. languages.Select(l => l.Name)], languages.ToList().FindIndex(l => l.Code.Equals(_draft.Interface.Language, StringComparison.OrdinalIgnoreCase)) is var at and >= 0 ? at : 0,
            i => Live(_draft with { Interface = _draft.Interface with { Language = languages[i].Code } }),
            Loc.T("The language of menus and the HUD. To add one, put its file in {0}.", ProjectSettings.GlobalizePath(Loc.UserFolder))));
        Slider(page, "Chat opacity", _draft.Interface.ChatOpacity, 0f, 1f, 0.05f, v => Live(_draft with { Interface = _draft.Interface with { ChatOpacity = v } }), Percent);
    }

    // ---- Shared ----

    private void Live(UserSettings settings)
    {
        _draft = settings.Clamped();
        GameSettings.Apply(WithSeen(_draft) with { Graphics = _draft.Graphics with { Resolution = WindowApplied.Resolution, DisplayMode = WindowApplied.DisplayMode, VSync = WindowApplied.VSync } });
    }

    // Tips seen while Options is open (it can be opened over the world) are kept, not undone by the draft.
    private static UserSettings WithSeen(UserSettings s) => s with { Gameplay = s.Gameplay with { SeenHints = GameSettings.Current.Gameplay.SeenHints } };

    private void AskReset()
    {
        var tab = CurrentTab;
        ConfirmDialog.Ask(this, "Reset to defaults", Loc.T("Put every {0} setting back to its default?", Loc.T(tab)), "Reset", () =>
        {
            var d = new UserSettings();
            switch (tab)
            {
                case "Controls":
                    _keymap.ResetAll();
                    KeymapChanged();
                    return;
                case "Graphics":
                    Live(_draft with { Graphics = d.Graphics with { Resolution = _draft.Graphics.Resolution, DisplayMode = _draft.Graphics.DisplayMode, VSync = _draft.Graphics.VSync }, UiScale = d.UiScale });
                    break;
                case "Audio":
                    Live(_draft with { Audio = d.Audio });
                    break;
                case "Gameplay":
                    Live(_draft with { MouseSensitivity = d.MouseSensitivity, InvertY = d.InvertY, FieldOfView = d.FieldOfView, Gameplay = d.Gameplay });
                    break;
                case "Interface":
                    Live(_draft with { Interface = d.Interface });
                    break;
            }
            RebuildPage(tab, tab switch
            {
                "Graphics" => BuildGraphics,
                "Audio" => BuildAudio,
                "Gameplay" => BuildGameplay,
                _ => BuildInterface,
            });
        });
    }

    private void RebuildPage(string tab, Action<VBoxContainer> build)
    {
        var page = _pages[tab];
        var name = tab == "Gameplay" ? _name.Text : null;
        foreach (var child in page.GetChildren())
        {
            page.RemoveChild(child);
            child.QueueFree();
        }
        build(page);
        if (name is not null)
            _name.Text = name;
        UpdateApply();
    }

    private static HBoxContainer Row(VBoxContainer page, string label, string? tooltip = null)
    {
        var row = new HBoxContainer { TooltipText = tooltip ?? "" };
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(220, 0), TooltipText = tooltip ?? "", MouseFilter = MouseFilterEnum.Pass });
        page.AddChild(row);
        return row;
    }

    private static OptionButton Option(VBoxContainer page, string label, IReadOnlyList<string> items, int selected, Action<int> changed, string? tooltip = null)
    {
        var option = new OptionButton { Name = label, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32), TooltipText = tooltip ?? "" };
        foreach (var item in items)
            option.AddItem(item);
        option.Selected = Math.Clamp(selected, 0, items.Count - 1);
        option.ItemSelected += i => changed((int)i);
        Row(page, label, tooltip).AddChild(option);
        return option;
    }

    private static void Toggle(VBoxContainer page, string label, bool value, Action<bool> changed, string? tooltip = null)
    {
        var check = new CheckButton { Name = label, ButtonPressed = value, TooltipText = tooltip ?? "" };
        check.Toggled += v => changed(v);
        Row(page, label, tooltip).AddChild(check);
    }

    private static void Slider(VBoxContainer page, string label, float value, float min, float max, float step, Action<float> changed, Func<float, string> format)
    {
        var row = Row(page, label);
        var slider = new HSlider { Name = label, MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
        var shown = new Label { Text = format(value), CustomMinimumSize = new Vector2(70, 0), HorizontalAlignment = HorizontalAlignment.Right };
        slider.ValueChanged += v =>
        {
            shown.Text = format((float)v);
            changed((float)v);
        };
        row.AddChild(slider);
        row.AddChild(shown);
    }
}
