using Godot;
using Mechanica.Core;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Sound;
using Mechanica.Core.Ui;
using Mechanica.Game.Mods;
using Mechanica.Game.Sound;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Editor;

/// <summary>
/// The Sound Event Binder (chunk 16): the Part Editor's Sounds tab. The top half binds the part's sound
/// names (<c>place</c>, <c>impact</c>, a script's <c>start</c>…) to registered sound events, with the
/// material fallback shown when a name is unbound and a play button for each. The bottom half edits the
/// mod's own sound events: clips imported into <c>sounds/</c>, bus, loop, variation, distance and loop
/// parameters, previewed through a <see cref="SoundPlayer"/> before they are saved. Data logic lives in Core
/// (<see cref="SoundBankDocument"/>, <see cref="SoundRegistry"/>, <see cref="PartSounds"/>).
/// </summary>
public partial class SoundBinderPanel : VBoxContainer
{
    private PropertyGrid _bindings = null!;
    private LineEdit _newKey = null!;
    private Label _bankTitle = null!;
    private ItemList _events = null!;
    private PropertyGrid _eventGrid = null!;
    private RichTextLabel _bankProblems = null!;
    private readonly HashSet<string> _extraKeys = [];
    private PartDocument? _part;
    private int _selected = -1;

    /// <summary>Mods folder the registry is loaded from (vanilla's is added when it lacks one).</summary>
    public Func<string> ModsRoot { get; init; } = () => ModPaths.ModsRoot;

    /// <summary>Opens the import file dialog for a kind of asset and calls back with the chosen file.</summary>
    public Action<AssetKind, Action<string>> Import { get; init; } = (_, _) => { };

    /// <summary>Shows an error to the user.</summary>
    public Action<string?> ShowError { get; init; } = _ => { };

    /// <summary>Raised after <see cref="Registry"/> is reloaded, so the part's sound warnings can be checked again.</summary>
    public event Action? RegistryChanged;

    /// <summary>Every sound event the binder can bind, from the mods folder.</summary>
    public SoundRegistry Registry { get; private set; } = SoundRegistry.Empty;

    /// <summary>The open part's mod's sound bank, or null before a part is opened.</summary>
    public SoundBankDocument? Bank { get; private set; }

    /// <summary>Plays previews.</summary>
    public SoundPlayer Player { get; private set; } = null!;

    /// <summary>The selected event in <see cref="Bank"/>, or -1.</summary>
    public int SelectedEvent => _selected;

    /// <summary>The bank's problems as plain text, for tests.</summary>
    public string BankProblemsText => _bankProblems.GetParsedText();

    /// <summary>The binding rows' sound pickers by sound name, for tests.</summary>
    public IReadOnlyDictionary<string, Control> BindingFields => _bindings.Fields;

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Player = new SoundPlayer { Name = "Preview" };
        Player.Listener = () => Player.GlobalPosition;
        AddChild(Player);

        AddChild(new Label { Text = "Part sounds", ThemeTypeVariation = "HeaderSmall" });
        AddChild(Hint("Names the game plays (place, remove, break, impact) and names the part's script plays. Unbound material names fall back to the sound material's events."));
        _bindings = new PropertyGrid();
        AddChild(_bindings);
        var add = new HBoxContainer();
        AddChild(add);
        _newKey = new LineEdit { PlaceholderText = "sound name, e.g. start", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        add.AddChild(_newKey);
        AddButton(add, "+ Sound name", () =>
        {
            var key = _newKey.Text.Trim();
            if (!Identifiers.IsValid(key))
            {
                ShowError(Loc.T("Sound name '{0}' must be lowercase snake_case.", key));
                return;
            }
            _newKey.Text = "";
            AddKey(key);
        });

        AddChild(new HSeparator());
        _bankTitle = new Label { Text = "Sound events", ThemeTypeVariation = "HeaderSmall" };
        AddChild(_bankTitle);
        _events = Loc.Verbatim(new ItemList { CustomMinimumSize = new Vector2(0, 120) });
        _events.ItemSelected += i => SelectEvent((int)i);
        AddChild(_events);
        var buttons = new HFlowContainer();
        AddChild(buttons);
        AddButton(buttons, "+ Event", () => SelectEvent(Bank?.Add("new_sound") ?? -1), "Add a sound event to this mod");
        AddButton(buttons, "+ Event from clip…", () => Import(AssetKind.Sound, path => ShowError(NewEventFromClip(path))), "Import an .ogg or .wav and make an event for it");
        AddButton(buttons, "Delete", () =>
        {
            if (Bank is null || _selected < 0)
                return;
            var index = _selected;
            _selected = -1;
            Bank.Remove(index);
        });
        AddButton(buttons, "▶ Preview", () => ShowError(PreviewSelected() is null ? "Fix the event's problems to preview it." : null));
        AddButton(buttons, "■ Stop", () => Player.StopAll());
        AddButton(buttons, "Save", () => SaveBank(), "Validate and write the mod's sound bank");
        _eventGrid = new PropertyGrid();
        AddChild(_eventGrid);
        _bankProblems = Loc.Verbatim(new RichTextLabel { BbcodeEnabled = true, FitContent = true, SelectionEnabled = true });
        AddChild(_bankProblems);

        ReloadRegistry();
    }

    // ---- Public operations (buttons call these; tests drive them directly) ----

    /// <summary>Reads every mod's sound events again, after a save or when the editor opens.</summary>
    public void ReloadRegistry()
    {
        var root = ModsRoot();
        var mods = Directory.Exists(root)
            ? Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, ModLoader.ManifestFileName))).Order(StringComparer.Ordinal).ToList()
            : [];
        if (!mods.Any(d => Path.GetFileName(d) == Identifiers.VanillaModId))
            mods.Insert(0, Path.Combine(ModPaths.ModsRoot, Identifiers.VanillaModId));
        Registry = SoundRegistry.Load(ModLoader.Load(mods));
        Player.Registry = Registry;
        Player.ClearCache();
        RebuildBindings();
        RegistryChanged?.Invoke();
    }

    /// <summary>Shows a part's bindings and opens its mod's sound bank (the first one it has, or a new one).</summary>
    public void SetPart(PartDocument? part)
    {
        _part = part;
        _extraKeys.Clear();
        if (Bank is not null)
            Bank.Changed -= OnBankChanged;
        Bank = null;
        _selected = -1;
        if (part is not null)
        {
            var diagnostics = new List<DataDiagnostic>();
            Bank = SoundBankDocument.Open(part.ModDirectory, SoundBankDocument.Files(part.ModDirectory).FirstOrDefault(), diagnostics);
            if (Bank is null)
                ShowError(string.Join("\n", diagnostics));
            else
                Bank.Changed += OnBankChanged;
            _selected = Bank is { Events.Count: > 0 } ? 0 : -1;
        }
        RebuildBindings();
        RebuildBank();
    }

    /// <summary>Redraws the binding rows from the part, after an edit, undo or redo.</summary>
    public void RebuildBindings()
    {
        if (_bindings is null)
            return;
        _bindings.Clear();
        if (_part is null)
            return;
        var d = _part.Definition;
        foreach (var key in Keys(d))
            AddBindingRow(key, d);
    }

    /// <summary>Binds a sound name of the open part to a sound id (bare or qualified; null unbinds).</summary>
    public void Bind(string key, string? sound) => _part?.SetSound(key, sound);

    /// <summary>Plays what a sound name of the open part plays now, as the game would. Null when nothing does.</summary>
    public SoundPlayer.Voice? PreviewBinding(string key)
    {
        if (_part is null)
            return null;
        var sound = Registry.Find(PartSounds.For(_part.Definition, key), _part.ModId);
        return sound is null ? null : Player.Play(sound, Player.GlobalPosition);
    }

    /// <summary>Selects an event of the bank and shows its fields.</summary>
    public void SelectEvent(int index)
    {
        _selected = Bank is not null && index >= 0 && index < Bank.Events.Count ? index : -1;
        RebuildBank();
    }

    /// <summary>Imports a clip into the mod's <c>sounds/</c> and makes a new event named after it. Returns an error, or null.</summary>
    public string? NewEventFromClip(string file)
    {
        if (Bank is null)
            return "Open or create a part first.";
        if (!AssetImporter.TryImport(file, Bank.ModDirectory, AssetKind.Sound, out var path, out var error))
            return error;
        var id = Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        SelectEvent(Bank.Add(Identifiers.IsValid(id) ? id : "new_sound", path));
        return null;
    }

    /// <summary>Imports a clip and adds it to the selected event. Returns an error, or null.</summary>
    public string? AddClip(string file)
    {
        if (Bank is null || _selected < 0)
            return "Select a sound event first.";
        if (!AssetImporter.TryImport(file, Bank.ModDirectory, AssetKind.Sound, out var path, out var error))
            return error;
        Player.ClearCache();
        var e = Bank.Events[_selected];
        Bank.Set(_selected, e with { Clips = [.. e.Clips ?? [], path] });
        return null;
    }

    /// <summary>Plays the selected event as it stands, unsaved changes included. Null while it has errors.</summary>
    public SoundPlayer.Voice? PreviewSelected()
    {
        if (Bank is null || _selected < 0 || Bank.Preview(_selected) is not { } sound)
            return null;
        return Player.Play(sound, Player.GlobalPosition);
    }

    /// <summary>Validates and writes the bank, then reloads the registry so the new events can be bound.</summary>
    public SoundBankSaveResult? SaveBank()
    {
        if (Bank is null)
            return null;
        var result = Bank.Save();
        if (result.Succeeded)
            ReloadRegistry();
        ShowBankProblems(result.Diagnostics, result.Succeeded ? Loc.T("Saved {0}.", $"{Bank.ModId}/{Bank.RelativeFile}") : Loc.T("Save failed; nothing was written."));
        return result;
    }

    // ---- Bindings ----

    // Suggested names for the part's kind first, then any others it binds.
    private IEnumerable<string> Keys(PartDefinition d) =>
        PartSounds.SuggestedKeys(d.Category).Concat((d.Sounds?.Keys ?? Enumerable.Empty<string>()).Concat(_extraKeys).Order(StringComparer.Ordinal)).Distinct();

    private void AddKey(string key)
    {
        if (_part is null)
            return;
        _extraKeys.Add(key);
        RebuildBindings();
    }

    private void AddBindingRow(string key, PartDefinition d)
    {
        var part = _part!;
        var bound = d.Sounds?.GetValueOrDefault(key);
        var fallback = PartSounds.For(d with { Sounds = null }, key);
        var picker = Loc.Verbatim(new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false });
        picker.AddItem(fallback is null ? Loc.T("(none)") : Loc.T("(material: {0})", fallback));
        var ids = Registry.Ids.ToList();
        foreach (var id in ids)
            picker.AddItem(Registry.ShortId(id, part.ModId));
        var current = bound is null ? null : Registry.Find(bound, part.ModId);
        if (bound is null)
        {
            picker.Selected = 0;
        }
        else if (current is not null)
        {
            picker.Selected = ids.IndexOf(current.QualifiedId) + 1;
        }
        else
        {
            picker.AddItem(Loc.T("{0} (not registered)", bound));
            picker.Selected = picker.ItemCount - 1;
        }
        picker.ItemSelected += i =>
        {
            if (i == 0)
                Bind(key, null);
            else if (i <= ids.Count)
                Bind(key, Registry.ShortId(ids[(int)i - 1], part.ModId));
        };
        if (fallback is not null && bound is null)
            picker.TooltipText = Loc.T("Unbound: plays {0} from the sound material", fallback);
        var box = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddChild(picker);
        AddButton(box, "▶", () => PreviewBinding(key), "Play it");
        AddButton(box, "×", () =>
        {
            _extraKeys.Remove(key);
            if (bound is null)
                RebuildBindings();
            else
                Bind(key, null);
        }, "Unbind");
        _bindings.Row(key, box);
        _bindings.Fields[key] = picker;
    }

    // ---- The mod's events ----

    private void OnBankChanged()
    {
        if (Bank is not null && _selected >= Bank.Events.Count)
            _selected = Bank.Events.Count - 1;
        RebuildBank();
    }

    private void RebuildBank()
    {
        if (_events is null)
            return;
        _events.Clear();
        _eventGrid.Clear();
        if (Bank is null)
        {
            _bankTitle.Text = "Sound events";
            _bankProblems.Text = "";
            return;
        }
        _bankTitle.Text = Loc.T(Bank.IsDirty ? "Sound events in {0}  • unsaved" : "Sound events in {0}", $"{Bank.ModId}/{Bank.RelativeFile}");
        foreach (var e in Bank.Events)
            _events.AddItem($"{e.Id}{(e.Loop == true ? "  (loop)" : "")}");
        if (_selected >= 0)
        {
            _events.Select(_selected);
            AddEventFields(_selected, Bank.Events[_selected]);
        }
        ShowBankProblems(Bank.Validate(), null);
    }

    private void AddEventFields(int index, SoundEventDefinition e)
    {
        var bank = Bank!;
        void Set(Func<SoundEventDefinition, SoundEventDefinition> change) => bank.Set(index, change(bank.Events[index]));
        var g = _eventGrid;
        g.AddText("Id", e.Id, v => Set(x => x with { Id = v }), "snake_case");
        g.AddText("Description", e.Description, v => Set(x => x with { Description = v }));
        var clips = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var clipText = Loc.Verbatim(new LineEdit { Text = string.Join(", ", e.Clips ?? []), PlaceholderText = "sounds/…ogg, other_mod:sounds/…wav", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        void CommitClips()
        {
            var list = clipText.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!list.SequenceEqual(e.Clips ?? []))
                Set(x => x with { Clips = list });
        }
        clipText.TextSubmitted += _ => CommitClips();
        clipText.FocusExited += CommitClips;
        clips.AddChild(clipText);
        AddButton(clips, "Import…", () => Import(AssetKind.Sound, path => ShowError(AddClip(path))), "Import a clip into sounds/ and add it");
        g.Row("Clips", clips);
        g.AddEnum<SoundBus>("Bus", e.Bus, v => Set(x => x with { Bus = v }), optional: true);
        g.AddBool("Loop", e.Loop, v => Set(x => x with { Loop = v == true ? true : null }));
        g.AddNumber("Volume", e.VolumeDb, -80, 24, 0.5, v => Set(x => x with { VolumeDb = (float?)v }), optional: true, suffix: "dB");
        g.AddNumber("Pitch", e.Pitch, 0.01, 16, 0.01, v => Set(x => x with { Pitch = (float?)v }), optional: true, fallback: 1);
        g.AddNumber("Volume variation", e.VolumeVariationDb, 0, 24, 0.5, v => Set(x => x with { VolumeVariationDb = (float?)v }), optional: true, suffix: "± dB");
        g.AddNumber("Pitch variation", e.PitchVariation, 0, 0.99, 0.01, v => Set(x => x with { PitchVariation = (float?)v }), optional: true);
        g.AddBool("Positional", e.Spatial, v => Set(x => x with { Spatial = v }), optional: true);
        g.AddNumber("Full volume within", e.MinDistance, 0, 1000, 0.5, v => Set(x => x with { MinDistance = (float?)v }), optional: true, fallback: SoundEvent.DefaultMinDistance, suffix: "m");
        g.AddNumber("Silent beyond", e.MaxDistance, 0.01, 5000, 1, v => Set(x => x with { MaxDistance = (float?)v }), optional: true, fallback: SoundEvent.DefaultMaxDistance, suffix: "m");
        g.AddEnum<SoundRolloff>("Rolloff", e.Rolloff, v => Set(x => x with { Rolloff = v }), optional: true);
        g.AddNumber("Max copies", e.MaxInstances, 1, 64, 1, v => Set(x => x with { MaxInstances = (int?)v }), optional: true, fallback: SoundEvent.DefaultMaxInstances);
        g.AddNumber("Cooldown", e.Cooldown, 0, 60, 0.05, v => Set(x => x with { Cooldown = (float?)v }), optional: true, suffix: "s");
        g.AddJson("Parameters", System.Text.Json.JsonSerializer.Serialize(e.Parameters ?? [], DataJson.WriteOptions), text =>
        {
            try
            {
                var parameters = DataJson.Read<List<SoundParameter>>(text);
                Set(x => x with { Parameters = parameters is { Count: > 0 } ? parameters : null });
                return null;
            }
            catch (System.Text.Json.JsonException ex)
            {
                return ex.Message;
            }
        });
    }

    private void ShowBankProblems(IReadOnlyList<DataDiagnostic> diagnostics, string? headline)
    {
        var lines = new List<string>();
        if (headline is not null)
            lines.Add($"[b]{Escape(headline)}[/b]");
        foreach (var d in diagnostics)
        {
            var color = d.Severity == DiagnosticSeverity.Error ? UiPalette.Error : UiPalette.Warning;
            lines.Add($"[color={color}]{(d.Severity == DiagnosticSeverity.Error ? Loc.T("Error") : Loc.T("Warning"))}[/color] {Escape(d.JsonPath ?? d.File)}: {Escape(d.Message)}");
        }
        if (diagnostics.Count == 0 && headline is null)
            lines.Add($"[color={UiPalette.Success}]{Loc.T("No problems.")}[/color]" + (Bank is { IsDirty: true } ? " " + Loc.T("Save writes the mod's sound bank.") : ""));
        _bankProblems.Text = string.Join("\n", lines);
    }

    private static Label Hint(string text) =>
        new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = "SecondaryLabel" };

    private static Button AddButton(Container parent, string text, Action pressed, string tooltip = "")
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
