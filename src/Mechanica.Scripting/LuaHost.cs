using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

/// <summary>
/// Minimal sandboxed Lua host. Scripts get the safe MoonSharp core modules only:
/// no io, os, load-from-file or dynamic module loading.
/// </summary>
public sealed class LuaHost
{
    public const CoreModules SandboxModules = CoreModules.Preset_SoftSandbox;

    private readonly Script _script = new(SandboxModules);

    /// <summary>Runs a chunk of Lua source and returns its result as a CLR object.</summary>
    public object? Run(string source, string chunkName = "chunk")
    {
        var result = _script.DoString(source, codeFriendlyName: chunkName);
        return result.ToObject();
    }

    /// <summary>Calls a global Lua function by name.</summary>
    public object? Call(string function, params object[] args)
    {
        var fn = _script.Globals.Get(function);
        if (fn.Type != DataType.Function)
            throw new InvalidOperationException($"Lua global '{function}' is not a function.");
        return _script.Call(fn, args).ToObject();
    }
}
