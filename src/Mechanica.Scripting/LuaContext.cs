using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

/// <summary>How one call into Lua ended.</summary>
internal enum CallStatus
{
    /// <summary>It returned.</summary>
    Ok,

    /// <summary>It raised an error.</summary>
    Error,

    /// <summary>It ran past the instruction budget and was stopped.</summary>
    OverBudget,

    /// <summary>It yielded out of a hook, which hooks may not do.</summary>
    Yielded,
}

/// <summary>Result of <see cref="LuaContext.Call"/>.</summary>
internal readonly record struct CallOutcome(CallStatus Status, DynValue[] Values, string? Error);

/// <summary>
/// The Lua state of one mod. Every script of the mod runs here; each part instance gets its own
/// environment table, so script globals are per part. Mods never share a state, so one mod cannot
/// change another's libraries.
/// <para>
/// Calls run on a runner coroutine with MoonSharp's auto-yield counter as the instruction budget: a call
/// that runs too long is forced to yield, the runner is dropped, and the caller learns it was stopped.
/// </para>
/// </summary>
internal sealed class LuaContext
{
    // Loaded before any mod code, so the runner keeps the original library functions even if a
    // script replaces them later.
    private const string RunnerSource = """
        local pack, unpack, yield = table.pack, table.unpack, coroutine.yield
        return function(marker)
          return function(...)
            local args = pack(...)
            while true do
              args = pack(yield(marker, args[1](unpack(args, 2, args.n))))
            end
          end
        end
        """;

    private readonly DynValue _runnerFactory;
    private readonly DynValue _marker;
    private readonly int _budget;
    private DynValue? _runner;

    public LuaContext(string modId, ScriptSettings settings, Action<string> print)
    {
        ModId = modId;
        _budget = settings.InstructionBudget;
        Script = new Script(LuaHost.SandboxModules);
        Script.Options.DebugPrint = print;
        Script.Options.CheckThreadAccess = false;
        _marker = DynValue.NewTable(new Table(Script));
        _runnerFactory = Script.Call(Script.DoString(RunnerSource, null, "=runner"), _marker);
        LuaPrelude.Install(Script, settings);
        Vec3Meta = Script.Globals.Get("vec3").Table.MetaTable.Get("__instance").Table;
    }

    /// <summary>The mod whose scripts run here.</summary>
    public string ModId { get; }

    /// <summary>The MoonSharp state.</summary>
    public Script Script { get; }

    /// <summary>Metatable of <c>vec3</c> values.</summary>
    public Table Vec3Meta { get; }

    /// <summary>
    /// Compiles a script into a factory that takes an environment table and runs the script's top level
    /// in it. Line numbers match the file. Throws <see cref="SyntaxErrorException"/>.
    /// </summary>
    public DynValue Compile(string source, string key) =>
        // The wrapper sits on the script's first line so error lines match the file.
        Script.DoString($"return function(_ENV) {source}\nend", null, key);

    /// <summary>A new environment whose missing globals come from the mod's shared globals.</summary>
    public Table NewEnvironment()
    {
        var env = new Table(Script);
        var meta = new Table(Script);
        meta.Set("__index", DynValue.NewTable(Script.Globals));
        env.MetaTable = meta;
        return env;
    }

    /// <summary>Calls a Lua function under the instruction budget. Never throws for Lua errors.</summary>
    public CallOutcome Call(DynValue function, params DynValue[] args)
    {
        var runner = _runner ??= Script.CreateCoroutine(_runnerFactory);
        runner.Coroutine.AutoYieldCounter = _budget;
        var resumeArgs = new DynValue[args.Length + 1];
        resumeArgs[0] = function;
        Array.Copy(args, 0, resumeArgs, 1, args.Length);
        DynValue result;
        try
        {
            result = runner.Coroutine.Resume(resumeArgs);
        }
        catch (InterpreterException e)
        {
            _runner = null;
            return new CallOutcome(CallStatus.Error, [], e.DecoratedMessage ?? e.Message);
        }
        catch (Exception e)
        {
            // A host function failed (a bug on the C# side); the runner died with it.
            _runner = null;
            return new CallOutcome(CallStatus.Error, [], $"internal error in a host function: {e.GetType().Name}: {e.Message}");
        }
        if (result.Type == DataType.YieldRequest || runner.Coroutine.State == CoroutineState.ForceSuspended)
        {
            _runner = null;
            return new CallOutcome(CallStatus.OverBudget, [], $"ran more than {_budget} Lua instructions in one call and was stopped (an endless loop?)");
        }
        var values = result.Type == DataType.Tuple ? result.Tuple : [result];
        if (values.Length == 0 || !ReferenceEquals(values[0].Table, _marker.Table) || runner.Coroutine.State != CoroutineState.Suspended)
        {
            _runner = null;
            return new CallOutcome(CallStatus.Yielded, [], "yielded out of a hook; use coroutine.wrap or coroutine.create inside the script instead");
        }
        return new CallOutcome(CallStatus.Ok, values[1..], null);
    }
}
