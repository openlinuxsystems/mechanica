using Godot;
using Mechanica.Core.Data;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Mods;

/// <summary>
/// Reads an engine settings file with the enabled mods' game rules on top (<see cref="ModRules"/>): the
/// <c>rules/&lt;name&gt;.patch.json</c> of every mod, in load order. A rule that would make the settings invalid is
/// left out and logged; the engine's own file must be valid.
/// </summary>
public static class GameRules
{
    /// <summary>
    /// <paramref name="path"/> (a <c>res://data/&lt;name&gt;.json</c>) parsed with <paramref name="parse"/>, after the
    /// rules of <paramref name="mods"/> (none when null).
    /// </summary>
    public static T Load<T>(string path, string name, ModLoadResult? mods, Func<string, T> parse)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {path}: {FileAccess.GetOpenError()}");
        var diagnostics = new List<DataDiagnostic>();
        var text = ModRules.Apply(name, file.GetAsText(), mods, json =>
        {
            try
            {
                parse(json);
                return null;
            }
            catch (Exception e) when (e is FormatException or ArgumentException or System.Text.Json.JsonException)
            {
                return e.Message;
            }
        }, diagnostics);
        foreach (var diagnostic in diagnostics)
            GD.PushWarning($"[rules] {diagnostic}");
        return parse(text);
    }
}
