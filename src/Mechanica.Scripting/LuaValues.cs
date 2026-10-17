using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

/// <summary>Conversions between Lua values, vectors and JSON for the bindings.</summary>
internal static class LuaValues
{
    /// <summary>A Lua <c>vec3</c> table.</summary>
    public static DynValue Vector(Script script, Table meta, Vector3 v)
    {
        var table = new Table(script);
        table.Set("x", DynValue.NewNumber(v.X));
        table.Set("y", DynValue.NewNumber(v.Y));
        table.Set("z", DynValue.NewNumber(v.Z));
        table.MetaTable = meta;
        return DynValue.NewTable(table);
    }

    /// <summary>Reads a vector from <c>{x=, y=, z=}</c> or <c>{1, 2, 3}</c>; throws a Lua error otherwise.</summary>
    public static Vector3 ToVector(DynValue value, string function, int argument)
    {
        if (value.Type == DataType.Table)
        {
            var t = value.Table;
            var x = t.Get("x");
            if (x.Type == DataType.Nil)
                return new Vector3(Number(t.Get(1), function, argument), Number(t.Get(2), function, argument), Number(t.Get(3), function, argument));
            return new Vector3(Number(x, function, argument), Number(t.Get("y"), function, argument), Number(t.Get("z"), function, argument));
        }
        throw new ScriptRuntimeException($"bad argument #{argument} to '{function}' (vector expected, got {value.Type.ToLuaTypeString()})");
    }

    /// <summary>A finite number, or a Lua error.</summary>
    public static float Number(DynValue value, string function, int argument)
    {
        if (value.Type != DataType.Number || !double.IsFinite(value.Number))
            throw new ScriptRuntimeException($"bad argument #{argument} to '{function}' (finite number expected, got {Describe(value)})");
        return (float)value.Number;
    }

    /// <summary>A finite number, null for nil, or a Lua error.</summary>
    public static float? OptionalNumber(DynValue value, string function, int argument) =>
        value.IsNil() ? null : Number(value, function, argument);

    /// <summary>A Lua table (or value) from JSON.</summary>
    public static DynValue FromJson(Script script, JsonNode? node)
    {
        switch (node)
        {
            case null:
                return DynValue.Nil;
            case JsonObject obj:
            {
                var table = new Table(script);
                foreach (var (key, value) in obj)
                    table.Set(key, FromJson(script, value));
                return DynValue.NewTable(table);
            }
            case JsonArray array:
            {
                var table = new Table(script);
                for (var i = 0; i < array.Count; i++)
                    table.Set(i + 1, FromJson(script, array[i]));
                return DynValue.NewTable(table);
            }
            default:
            {
                // A value node may wrap any CLR type; its JSON form reads back uniformly.
                var element = JsonSerializer.SerializeToElement(node);
                return element.ValueKind switch
                {
                    JsonValueKind.String => DynValue.NewString(element.GetString()),
                    JsonValueKind.Number => DynValue.NewNumber(element.GetDouble()),
                    JsonValueKind.True => DynValue.True,
                    JsonValueKind.False => DynValue.False,
                    _ => DynValue.Nil,
                };
            }
        }
    }

    /// <summary>
    /// Plain data (nil, booleans, finite numbers, strings and tables of them, no cycles) as JSON. A table
    /// whose keys are exactly 1..n becomes an array; any other table becomes an object with string keys
    /// (number keys are written as text). Throws <see cref="ArgumentException"/> on anything else.
    /// </summary>
    public static JsonNode? ToJson(DynValue value, string path = "state", HashSet<Table>? seen = null)
    {
        switch (value.Type)
        {
            case DataType.Nil:
            case DataType.Void:
                return null;
            case DataType.Boolean:
                return JsonValue.Create(value.Boolean);
            case DataType.Number:
                if (!double.IsFinite(value.Number))
                    throw new ArgumentException($"{path} is {value.Number}, which JSON can't hold.");
                return JsonValue.Create(value.Number);
            case DataType.String:
                return JsonValue.Create(value.String);
            case DataType.Table:
            {
                seen ??= new HashSet<Table>(ReferenceEqualityComparer.Instance);
                if (!seen.Add(value.Table))
                    throw new ArgumentException($"{path} refers back to a table that contains it.");
                var pairs = value.Table.Pairs.ToList();
                JsonNode result;
                if (pairs.Count > 0 && IsSequence(pairs))
                {
                    var array = new JsonArray();
                    foreach (var pair in pairs.OrderBy(p => p.Key.Number))
                        array.Add(ToJson(pair.Value, $"{path}[{pair.Key.Number}]", seen));
                    result = array;
                }
                else
                {
                    var obj = new JsonObject();
                    foreach (var pair in pairs.OrderBy(p => KeyText(p.Key, path), StringComparer.Ordinal))
                        obj[KeyText(pair.Key, path)] = ToJson(pair.Value, $"{path}.{KeyText(pair.Key, path)}", seen);
                    result = obj;
                }
                seen.Remove(value.Table);
                return result;
            }
            default:
                throw new ArgumentException($"{path} is a {value.Type.ToLuaTypeString()}; state can only hold nil, booleans, numbers, strings and tables.");
        }
    }

    /// <summary>A short description of a value for error messages.</summary>
    public static string Describe(DynValue value) => value.Type switch
    {
        DataType.Number => value.Number.ToString(CultureInfo.InvariantCulture),
        _ => value.Type.ToLuaTypeString(),
    };

    private static bool IsSequence(List<TablePair> pairs)
    {
        if (pairs.Any(p => p.Key.Type != DataType.Number))
            return false;
        var keys = pairs.Select(p => p.Key.Number).Order().ToList();
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i] != i + 1)
                return false;
        }
        return true;
    }

    private static string KeyText(DynValue key, string path) => key.Type switch
    {
        DataType.String => key.String,
        DataType.Number => key.Number.ToString(CultureInfo.InvariantCulture),
        DataType.Boolean => key.Boolean ? "true" : "false",
        _ => throw new ArgumentException($"{path} has a {key.Type.ToLuaTypeString()} key; state keys must be strings or numbers."),
    };
}
