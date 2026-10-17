using System.Diagnostics;
using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using Mechanica.Scripting;

// D2 benchmark, Lua side only: N scripted wings on one Creation, stepped at 60 Hz without physics.
// Usage: dotnet run -c Release --project bench/Mechanica.Scripting.Bench [-- parts [steps]]
var parts = args.Length > 0 ? int.Parse(args[0]) : 200;
var steps = args.Length > 1 ? int.Parse(args[1]) : 3000;
var root = FindRepoRoot();
var mods = ModLoader.Load([Path.Combine(root, "game", "tests", "luabench", "mods", "bench")]);
foreach (var d in mods.Diagnostics)
    Console.WriteLine(d);

Console.WriteLine($"Lua benchmark: {parts} parts, {steps} steps at 60 Hz, {Environment.ProcessorCount} CPUs, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
var wing = File.ReadAllText(Path.Combine(root, "game", "tests", "luabench", "mods", "bench", "scripts", "wing.lua"));
Run("empty on_update", new TextSource("function on_update(dt) end"), parts, steps);
Run("wing maths, no push", new TextSource(wing.Replace("part.apply_force(lift + drag)", "local f = lift + drag")), parts, steps);
Run("wing (lift + drag, ApplyForce)", new ModScriptSource(mods), parts, steps);
Run("wing, maths per component", new TextSource(File.ReadAllText(Path.Combine(root, "bench", "Mechanica.Scripting.Bench", "wing_components.lua"))), parts, steps);

// Chunk 11: the shipped vanilla aero scripts, which also read settings, sanitize inputs and drive sounds.
var vanilla = ModLoader.Load([Path.Combine(root, "mods", "vanilla")]);
foreach (var d in vanilla.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
    Console.WriteLine(d);
// The rotor, thruster and hover jet run at full power (their command gets 1 added) in Creative (no fuel),
// and sounds are accepted.
Run("vanilla:wing", new ModScriptSource(vanilla), parts, steps, vanilla, "vanilla:wing", 4);
Run("vanilla:control_surface", new ModScriptSource(vanilla), parts, steps, vanilla, "vanilla:control_surface", 4);
Run("vanilla:rotor", Powered("rotor"), parts, steps, vanilla, "vanilla:rotor", 3);
Run("vanilla:thruster", Powered("thruster"), parts, steps, vanilla, "vanilla:thruster", 1);
Run("vanilla:hover_jet", Powered("hover_jet"), parts, steps, vanilla, "vanilla:hover_jet", 2);

TextSource Powered(string script) => new(File.ReadAllText(Path.Combine(root, "mods", "vanilla", "scripts", script + ".lua"))
    .Replace("clamp(command(dt), 0, 1)", "clamp(command(dt) + 1, 0, 1)"), "vanilla:" + script);

void Run(string name, IScriptSource source, int count, int stepCount, ModLoadResult? set = null, string partId = "bench:wing", int spacing = 1)
{
    var world = new World();
    var creation = new Creation(1, set ?? mods);
    var side = (int)Math.Ceiling(Math.Sqrt(count));
    // Vanilla parts sit on a cardboard deck one cell below, which they attach to by their bottom socket.
    for (var x = 0; set is not null && x < side * spacing; x++)
    {
        for (var z = 0; z < side * spacing; z++)
            creation.TryPlacePart("vanilla:cardboard_block", new GridCell(x, -1, z), default, out _, out _);
    }
    for (var i = 0; i < count; i++)
    {
        if (!creation.TryPlacePart(partId, new GridCell(i % side * spacing, 0, i / side * spacing), default, out _, out var error))
            throw new InvalidOperationException(error);
    }
    creation.SetAllBodyPoses(new Pose(new Vector3(0, 100, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.1f)));
    creation.SetBodyVelocity(0, new BodyVelocity(new Vector3(30, -2, 0), new Vector3(0, 0.1f, 0)));
    world.Creations[1] = creation;
    var dispatcher = new ActionDispatcher();
    new MotionActions(new MotionSettings(), world).Register(dispatcher);
    var runtime = new ScriptRuntime(new ScriptSettings(), dispatcher, world, new NoPhysics(), source);
    runtime.Sound = new SilentSound();
    runtime.Creative = true;
    runtime.Message += m => { if (m.Kind != ScriptMessageKind.Print) Console.WriteLine(m); };

    for (var i = 0; i < 300; i++)
        runtime.Update(1 / 60f);
    var times = new double[stepCount];
    var gc = GC.CollectionCount(0);
    var total = Stopwatch.StartNew();
    for (var i = 0; i < stepCount; i++)
    {
        var start = Stopwatch.GetTimestamp();
        runtime.Update(1 / 60f);
        times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    total.Stop();
    Array.Sort(times);
    var mean = total.Elapsed.TotalMilliseconds / stepCount;
    Console.WriteLine($"  {name}: mean {mean:0.000} ms/step ({mean * 1000 / count:0.00} µs/part), p50 {times[stepCount / 2]:0.000}, p99 {times[(int)(stepCount * 0.99)]:0.000}, max {times[^1]:0.000} ms; {mean / (1000.0 / 60) * 100:0.0}% of a 16.7 ms frame; gen0 GCs {GC.CollectionCount(0) - gc}; pushes/step {world.Pushes / (double)(stepCount + 300):0}");
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir is not null && !File.Exists(Path.Combine(dir, "Mechanica.sln")))
        dir = Path.GetDirectoryName(dir);
    return dir ?? throw new InvalidOperationException("Run from inside the repository.");
}

// A benchmark variant of the script, written to a temporary file.
sealed class TextSource : IScriptSource
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mechanica-bench-{Guid.NewGuid():N}.lua");
    private readonly string? _partId;

    // With a part id, only that part runs it (the deck's blocks run nothing).
    public TextSource(string source, string? partId = null)
    {
        File.WriteAllText(_path, source);
        _partId = partId;
    }

    public ScriptFile? FindScript(string partId) => _partId is null || partId == _partId ? new("bench:variant.lua", "bench", _path) : null;
}

// Accepts every sound and plays nothing, so the scripts' sound calls cost what they would in game.
sealed class SilentSound : IScriptSound
{
    private int _next;

    public string? Play(string sound, string ownerModId, Vector3 position) => null;

    public int StartLoop(string sound, string ownerModId, int creation, int part, IReadOnlyDictionary<string, float>? parameters, out string? error)
    {
        error = null;
        return ++_next;
    }

    public string? SetLoopParameter(int handle, string name, float value) => null;

    public void StopLoop(int handle)
    {
    }
}

sealed class World : IMotionWorld
{
    public Dictionary<int, Creation> Creations { get; } = [];
    public long Pushes { get; private set; }
    public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);
    public IEnumerable<Creation> AllCreations => Creations.Values;
    public bool IsLive(int id) => true;
    public void ApplyForce(Creation creation, int body, Vector3 force, Vector3 torque, float? duration, int? part, Vector3? at, Vector3 ownTorque) => Pushes++;
    public void SetJointMotor(Creation creation, JointLink joint, JointMotor motor) { }
    public float? JointPosition(Creation creation, JointLink joint) => null;
}
