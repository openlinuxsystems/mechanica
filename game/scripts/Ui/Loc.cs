using Godot;
using Mechanica.Core.Ui;

namespace Mechanica.Game.Ui;

/// <summary>
/// The string tables (UI spec §16 Interface, Language). English text is the key: every Label, Button,
/// tooltip, placeholder, tab and list item shows through Godot's auto-translation, which asks the
/// language in use for it, and text built at run time goes through <see cref="T"/> with <c>{0}</c> slots.
/// Languages are the files in <c>res://data/lang/</c> plus any the player drops into <c>user://lang/</c>
/// (that folder wins for the same code). Text the player or a mod wrote (names, chat, saves, part names)
/// turns auto-translation off with <see cref="Verbatim"/>.
/// </summary>
public static partial class Loc
{
    /// <summary>The shipped languages.</summary>
    public const string BuiltIn = "res://data/lang";

    /// <summary>Where the player drops more.</summary>
    public const string UserFolder = "user://lang";

    private static readonly Table _table = new();
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
        return [English, .. found.Values.Where(l => l != English).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Shows the UI in <paramref name="code"/>; English when no file has that code.</summary>
    public static void Use(string code)
    {
        var language = Languages().FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? English;
        if (language == _table.Language && TranslationServer.GetLocale() == language.Code)
            return;
        _table.Language = language == English ? null : language;
        if (!TranslationServer.HasTranslation(_table))
            TranslationServer.AddTranslation(_table);
        _table.Locale = language.Code;
        TranslationServer.SetLocale(language.Code);
    }

    /// <summary><paramref name="key"/> in the language in use, with <c>{0}</c>, <c>{1}</c>, … filled from <paramref name="args"/>.</summary>
    public static string T(string key, params object?[] args)
    {
        _collected?.Add(key);
        var text = Language.Format(Current[key], args);
        if (args.Length > 0)
            _built?.Add(text);
        return text;
    }

    /// <summary>Turns auto-translation off on <paramref name="node"/>, for text the player or a mod wrote.</summary>
    public static T Verbatim<T>(T node) where T : Node
    {
        node.AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled;
        return node;
    }

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

    // One translation whose messages are the language in use; Godot asks it for every auto-translated text.
    private sealed partial class Table : Translation
    {
        public Language? Language { get; set; }

        public override StringName _GetMessage(StringName srcMessage, StringName context)
        {
            var key = srcMessage.ToString();
            return Language is { } language && language.Strings.TryGetValue(key, out var text) && text.Length > 0 ? text : srcMessage;
        }

        public override StringName _GetPluralMessage(StringName srcMessage, StringName srcPluralMessage, int n, StringName context) =>
            _GetMessage(n == 1 ? srcMessage : srcPluralMessage, context);
    }
}
