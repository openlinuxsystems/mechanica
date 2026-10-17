using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

/// <summary>Lua code and guards every mod's state starts with: the <c>vec3</c> type and a bounded <c>string.rep</c>.</summary>
internal static class LuaPrelude
{
    /// <summary>The <c>vec3</c> library, in Lua so vector maths stays inside the VM.</summary>
    public const string Vec3Source = """
        local sqrt, setmetatable, type, format = math.sqrt, setmetatable, type, string.format
        local V = {}
        V.__index = V
        local function new(x, y, z) return setmetatable({ x = x or 0, y = y or 0, z = z or 0 }, V) end
        V.__add = function(a, b) return new(a.x + b.x, a.y + b.y, a.z + b.z) end
        V.__sub = function(a, b) return new(a.x - b.x, a.y - b.y, a.z - b.z) end
        V.__unm = function(a) return new(-a.x, -a.y, -a.z) end
        V.__mul = function(a, b)
          if type(a) == "number" then return new(a * b.x, a * b.y, a * b.z) end
          if type(b) == "number" then return new(a.x * b, a.y * b, a.z * b) end
          return new(a.x * b.x, a.y * b.y, a.z * b.z)
        end
        V.__div = function(a, b) return new(a.x / b, a.y / b, a.z / b) end
        V.__eq = function(a, b) return a.x == b.x and a.y == b.y and a.z == b.z end
        V.__tostring = function(a) return format("(%g, %g, %g)", a.x, a.y, a.z) end
        function V.dot(a, b) return a.x * b.x + a.y * b.y + a.z * b.z end
        function V.cross(a, b) return new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x) end
        function V.length(a) return sqrt(a.x * a.x + a.y * a.y + a.z * a.z) end
        function V.length_squared(a) return a.x * a.x + a.y * a.y + a.z * a.z end
        function V.normalized(a)
          local l = sqrt(a.x * a.x + a.y * a.y + a.z * a.z)
          if l == 0 then return new(0, 0, 0) end
          return new(a.x / l, a.y / l, a.z / l)
        end
        function V.lerp(a, b, t) return new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t) end
        vec3 = setmetatable({ new = new, is = function(v) return getmetatable(v) == V end },
          { __call = function(_, x, y, z) return new(x, y, z) end, __instance = V })
        """;

    /// <summary>Adds the prelude to a fresh state.</summary>
    public static void Install(Script script, ScriptSettings settings)
    {
        script.DoString(Vec3Source, null, "=vec3");
        var max = settings.MaxStringLength;
        var stringTable = script.Globals.Get("string").Table;
        stringTable.Set("rep", DynValue.NewCallback((_, args) =>
        {
            var text = args.AsType(0, "rep", DataType.String, allowNil: false).CastToString();
            var count = (long)Math.Floor(args.AsType(1, "rep", DataType.Number).Number);
            var separator = args.Count > 2 && !args[2].IsNil() ? args.AsType(2, "rep", DataType.String).CastToString() : "";
            if (count <= 0)
                return DynValue.NewString("");
            if ((text.Length + separator.Length) * (double)count > max)
                throw new ScriptRuntimeException($"string.rep would build more than {max} characters");
            return DynValue.NewString(string.Join(separator, Enumerable.Repeat(text, (int)count)));
        }, "rep"));
    }
}
