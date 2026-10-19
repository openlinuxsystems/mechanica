using Godot;
using Mechanica.Core.Ui;

namespace Mechanica.Game.Ui;

/// <summary>
/// The string tables (UI spec §16 Interface, Language). English text is the key: every Label, Button,
/// tooltip, placeholder, tab and list item shows through Godot's auto-translation, which asks the
/// language in use for it, and text built at run time goes through <see cref="T"/> with <c>{0}</c> slots.
/// Languages are the files in <c>res://data/lang/</c> plus any the player drops into <c>user://lang/</c>
/// (that folder wins for the same code). Text the player or a mod wrote (names, chat, saves, part names)
/// skips the game's tables with <see cref="Verbatim"/>. Mods add their own tables (<c>lang/&lt;code&gt;.json</c> in a mod,
/// <see cref="UseMods"/>): those translate mod text wherever it shows whole, and may translate or reword the game's.
/// </summary>
public static partial class Loc
{
    /// <summary>The shipped languages.</summary>
    public const string BuiltIn = "res://data/lang";

    /// <summary>Where the player drops more.</summary>
    public const string UserFolder = "user://lang";

    /// <summary>The translation domain of <see cref="Verbatim"/> controls: only mods' tables translate their text.</summary>
    public static readonly StringName ModDomain = "mod";

    private static readonly Table _table = new();
    private static readonly Table _modTable = new();
    private static IReadOnlyList<Language> _modLanguages = [];
    private static IReadOnlyDictionary<string, string> _modStrings = new Dictionary<string, string>();
    private static Language _english = null!;
    private static HashSet<string>? _collected;
    private static HashSet<string>? _built;
    private static List<Language>? _languages;

    /// <summary>The language in use.</summary>
    public static Language Current => _table.Language ?? English;

    /// <summary>The English table: every key the game shows.</summary>
    public static Language English => _english ??= Read($"{BuiltIn}/en.json") ?? new Language(Core.Ui.Language.English, "English", new Dictionary<string, string>());

    /// <summary>
    /// Every language there is, English first then by name: <c>(code, own name, file)</c>. A file that does not
    /// read is left out and logged.
    /// </summary>
    public static IReadOnlyList<Language> Languages() => _languages ??= Find();

    /// <summary>Looks in the folders again, for files dropped in since.</summary>
    public static IReadOnlyList<Language> Refresh() => _languages = Find();

    private static List<Language> Find()
    {
        var found = new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase) { [English.Code] = English };
        foreach (var folder in new[] { BuiltIn, UserFolder }.Where(DirAccess.DirExistsAbsolute))
        {
            foreach (var file in DirAccess.GetFilesAt(folder).Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                if (folder == BuiltIn && file == "en.json")
                    continue;
                if (Read($"{folder}/{file}") is { } language && !language.Code.Equals(English.Code, StringComparison.OrdinalIgnoreCase))
                    found[language.Code] = language;
            }
        }
        // A language only mods translate into can be picked too; the game's own text then shows in English.
        foreach (var language in _modLanguages.Where(l => !found.ContainsKey(l.Code)))
            found[language.Code] = language with { Strings = new Dictionary<string, string>() };
        return [English, .. found.Values.Where(l => l != English).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Takes the language files of the mods in <paramref name="modDirectories"/> (load order; later mods win) and
    /// applies them to the language in use. Files that do not read are logged and left out.
    /// </summary>
    public static void UseMods(IEnumerable<string> modDirectories)
    {
        var diagnostics = new List<Core.Data.DataDiagnostic>();
        _modLanguages = ModLanguages.Read(modDirectories, diagnostics);
        foreach (var problem in diagnostics)
            GD.PushWarning(problem.ToString());
        var code = Current.Code;
        _languages = Find();
        _table.Language = null; // so Use applies the new tables
        Use(code);
    }

    /// <summary>The mods' translation of <paramref name="text"/> in the language in use, or the text itself.</summary>
    public static string Mod(string text) => _modStrings.TryGetValue(text, out var translated) ? translated : text;

    /// <summary>Shows the UI in <paramref name="code"/>; English when no file has that code.</summary>
    public static void Use(string code)
    {
        var language = Languages().FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? English;
        if (language == _table.Language && TranslationServer.GetLocale() == language.Code)
            return;
        _table.Language = language == English ? null : language;
        _modStrings = ModLanguages.For(_modLanguages, language.Code);
        _table.Mods = _modTable.Mods = _modStrings;
        if (!TranslationServer.HasTranslation(_table))
            TranslationServer.AddTranslation(_table);
        var domain = TranslationServer.GetOrAddDomain(ModDomain);
        if (!domain.HasTranslation(_modTable))
            domain.AddTranslation(_modTable);
        _table.Locale = _modTable.Locale = language.Code;
        TranslationServer.SetLocale(language.Code);
    }

    /// <summary><paramref name="key"/> in the language in use, with <c>{0}</c>, <c>{1}</c>, … filled from <paramref name="args"/>.</summary>
    public static string T(string key, params object?[] args)
    {
        _collected?.Add(key);
        // A mod's name for something passed in (a part or item name) shows in the mod's translation.
        if (_modStrings.Count > 0)
            args = [.. args.Select(a => a is string value ? Mod(value) : a)];
        var text = Language.Format(_modStrings.TryGetValue(key, out var reworded) ? reworded : Current[key], args);
        if (args.Length > 0)
            _built?.Add(text);
        return text;
    }

    /// <summary>
    /// Keeps the game's tables off <paramref name="node"/>, for text the player or a mod wrote: it shows as written,
    /// or in a mod's translation when a mod's table has the whole text (<see cref="ModDomain"/>).
    /// </summary>
    public static T Verbatim<T>(T node) where T : Node
    {
        node.SetTranslationDomain(ModDomain);
        return node;
    }

    /// <summary>Puts the game's tables back on <paramref name="node"/>, for UI text inside a <see cref="Verbatim"/> control.</summary>
    public static T Game<T>(T node) where T : Node
    {
        node.AutoTranslateMode = Node.AutoTranslateModeEnum.Always;
        node.SetTranslationDomain("");
        return node;
    }

    /// <summary>Whether <paramref name="node"/> shows its text through the game's tables (not <see cref="Verbatim"/>).</summary>
    public static bool Translated(Node node) => node.CanAutoTranslate() && node.GetTranslationDomain() != ModDomain;

    /// <summary>
    /// Runs <paramref name="apply"/> on <paramref name="control"/> now and again whenever the settings change, for
    /// text built with values that follow the language or the key bindings. The control shows its text verbatim.
    /// </summary>
    public static T Live<T>(T control, Action<T> apply) where T : Control
    {
        Verbatim(control);
        apply(control);
        void Refresh()
        {
            if (GodotObject.IsInstanceValid(control))
                apply(control);
        }
        // Follows the settings while in the tree, and catches up on entering it again.
        var following = false;
        void Follow()
        {
            if (!following)
                Settings.GameSettings.Changed += Refresh;
            following = true;
        }
        control.TreeEntered += () =>
        {
            Follow();
            apply(control);
        };
        control.TreeExiting += () =>
        {
            Settings.GameSettings.Changed -= Refresh;
            following = false;
        };
        if (control.IsInsideTree())
            Follow();
        return control;
    }

    /// <summary>Starts recording every key <see cref="T"/> is asked for and the text it builds, to build <c>en.json</c> from a run (the language test's collect mode).</summary>
    public static void Collect()
    {
        _collected ??= new HashSet<string>(StringComparer.Ordinal);
        _built ??= new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>The keys <see cref="T"/> was asked for since <see cref="Collect"/>.</summary>
    public static IEnumerable<string> Collected => _collected ?? [];

    /// <summary>True when <see cref="T"/> built <paramref name="text"/> from a key and values since <see cref="Collect"/>.</summary>
    public static bool Built(string text) => _built?.Contains(text) == true;

    private static Language? Read(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
            return null;
        try
        {
            return Language.Parse(file.GetAsText(), path);
        }
        catch (FormatException e)
        {
            GD.PushWarning(e.Message);
            return null;
        }
    }

    // One translation whose messages are the mods' strings, then the language in use; Godot asks it for every
    // auto-translated text. The mod domain's has no language, only the mods' strings.
    private sealed partial class Table : Translation
    {
        public Language? Language { get; set; }

        public IReadOnlyDictionary<string, string> Mods { get; set; } = new Dictionary<string, string>();

        public override StringName _GetMessage(StringName srcMessage, StringName context)
        {
            if (Mods.Count == 0 && Language is null)
                return srcMessage;
            var key = srcMessage.ToString();
            if (Mods.TryGetValue(key, out var modded))
                return modded;
            return Language is { } language && language.Strings.TryGetValue(key, out var text) && text.Length > 0 ? text : srcMessage;
        }

        public override StringName _GetPluralMessage(StringName srcMessage, StringName srcPluralMessage, int n, StringName context) =>
            _GetMessage(n == 1 ? srcMessage : srcPluralMessage, context);
    }
}
