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

void Run(string name, IScriptSource source, int count, int stepCount)
{
    var world = new World();
    var creation = new Creation(1, mods);
    var side = (int)Math.Ceiling(Math.Sqrt(count));
    for (var i = 0; i < count; i++)
    {
        if (!creation.TryPlacePart("bench:wing", new GridCell(i % side, 0, i / side), default, out _, out var error))
            throw new InvalidOperationException(error);
    }
    creation.SetAllBodyPoses(new Pose(new Vector3(0, 100, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.1f)));
    creation.SetBodyVelocity(0, new BodyVelocity(new Vector3(30, -2, 0), new Vector3(0, 0.1f, 0)));
    world.Creations[1] = creation;
    var dispatcher = new ActionDispatcher();
    new MotionActions(new MotionSettings(), world).Register(dispatcher);
    var runtime = new ScriptRuntime(new ScriptSettings(), dispatcher, world, new NoPhysics(), source);
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

    public TextSource(string source) => File.WriteAllText(_path, source);

    public ScriptFile? FindScript(string partId) => new("bench:variant.lua", "bench", _path);
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
