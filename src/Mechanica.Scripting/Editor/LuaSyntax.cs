using System.Text.RegularExpressions;
using Mechanica.Core.ModTools;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting.Editor;

/// <summary>A compile error in a script.</summary>
/// <param name="Line">1-based line.</param>
/// <param name="Column">0-based column where the error starts.</param>
/// <param name="Message">What is wrong, without the location.</param>
public sealed record LuaSyntaxError(int Line, int Column, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"line {Line}: {Message}";
}

/// <summary>Checks scripts the way the host compiles them, without running them (for the editor).</summary>
public static partial class LuaSyntax
{
    /// <summary>The first compile error in <paramref name="source"/>, or null when it compiles.</summary>
    public static LuaSyntaxError? Check(string source)
    {
        const string prefix = "return function(_ENV) ";
        var script = new Script(LuaHost.SandboxModules);
        try
        {
            // Wrapped exactly as the runtime wraps it, so what passes here loads there.
            script.LoadString($"{prefix}{source}\nend", null, "script");
            return null;
        }
        catch (SyntaxErrorException e)
        {
            // An error the wrapper's closing line reports (a missing 'end') belongs on the file's last line.
            var error = Parse(e.DecoratedMessage ?? e.Message);
            var lines = source.Split('\n').Length;
            // Columns on the first line count the wrapper too.
            return error.Line > lines ? error with { Line = lines, Column = 0 }
                : error.Line == 1 ? error with { Column = Math.Max(0, error.Column - prefix.Length) }
                : error;
        }
    }

    /// <summary><see cref="Check"/> in the shape the mod tools' validation takes.</summary>
    public static ScriptProblem? Problem(string source) =>
        Check(source) is { } error ? new ScriptProblem(error.Line, error.Column, error.Message) : null;

    /// <summary>
    /// Splits a MoonSharp message such as <c>mod:scripts/x.lua:(3,4-9): unexpected symbol</c> into its
    /// place and text; a message without a place is reported on line 1.
    /// </summary>
    public static LuaSyntaxError Parse(string message)
    {
        var match = Location().Match(message);
        return match.Success
            ? new LuaSyntaxError(int.Parse(match.Groups["line"].Value), int.Parse(match.Groups["column"].Value), match.Groups["text"].Value.Trim())
            : new LuaSyntaxError(1, 0, message);
    }

    [GeneratedRegex(@"\((?<line>\d+),(?<column>\d+)(?:-\d+)?\):\s*(?<text>.*)$", RegexOptions.Singleline)]
    private static partial Regex Location();
}
