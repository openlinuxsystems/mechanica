using System.Text.Json.Nodes;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting.Editor;

/// <summary>What <see cref="LuaSandbox.Eval"/> did.</summary>
/// <param name="Ok">True when the chunk ran to the end.</param>
/// <param name="Values">What it returned, as JSON; a value JSON can't hold (a function) is its type name in angle brackets.</param>
/// <param name="Output">Lines it printed, oldest first.</param>
/// <param name="Error">Why it stopped (a compile error, a runtime error or the instruction budget), or null.</param>
public sealed record LuaEvalResult(bool Ok, IReadOnlyList<JsonNode?> Values, IReadOnlyList<string> Output, string? Error);

/// <summary>
/// Runs a chunk of Lua in a throwaway state with exactly what part scripts start with (the safe core modules
/// and <c>vec3</c>, no io, os or file loading) under the host's instruction budget. Nothing it does reaches
/// the world: there is no <c>world</c>, <c>part</c> or Action API here. A chunk that is a single expression
/// returns its value, as in a REPL.
/// </summary>
public static class LuaSandbox
{
    /// <summary>Most printed lines kept.</summary>
    public const int MaxOutputLines = 200;

    /// <summary>Longest printed line kept, in characters.</summary>
    public const int MaxLineLength = 2000;

    /// <summary>Runs <paramref name="source"/> once in a fresh state.</summary>
    public static LuaEvalResult Eval(string source, ScriptSettings settings)
    {
        var output = new List<string>();
        var context = new LuaContext("eval", settings, line =>
        {
            if (output.Count < MaxOutputLines)
                output.Add(line.Length > MaxLineLength ? line[..MaxLineLength] + "…" : line);
        });
        DynValue chunk;
        try
        {
            chunk = Load(context.Script, source);
        }
        catch (SyntaxErrorException e)
        {
            return new LuaEvalResult(false, [], output, LuaSyntax.Parse(e.DecoratedMessage ?? e.Message).ToString());
        }
        var outcome = context.Call(chunk);
        if (outcome.Status != CallStatus.Ok)
            return new LuaEvalResult(false, [], output, outcome.Error);
        return new LuaEvalResult(true, [.. outcome.Values.Select(ToJson)], output, null);
    }

    // "1 + 2" runs as "return 1 + 2"; anything that isn't one expression runs as written.
    private static DynValue Load(Script script, string source)
    {
        try
        {
            return script.LoadString($"return {source}", null, "eval");
        }
        catch (SyntaxErrorException)
        {
            return script.LoadString(source, null, "eval");
        }
    }

    private static JsonNode? ToJson(DynValue value)
    {
        try
        {
            return LuaValues.ToJson(value, "result");
        }
        catch (ArgumentException)
        {
            return JsonValue.Create($"<{value.Type.ToLuaTypeString()}>");
        }
    }
}
