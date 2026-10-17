using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;

namespace Mechanica.Scripting.Tests;

/// <summary>
/// A host world for script tests: Creations in memory, the real motion actions behind a real
/// dispatcher, and part scripts written to a temporary folder.
/// </summary>
internal sealed class ScriptRig : IDisposable
{
    public const string Block = "test:block";
    public const string Thruster = "test:thruster";
    public const string Bearing = "test:bearing";
    public const string Piston = "test:piston";
    public const string Other = "other:gadget";

    public ScriptRig(ScriptSettings? settings = null, IScriptPhysics? physics = null)
    {
        Directory.CreateDirectory(Root);
        Catalog = new PartCatalog()
            .Add(Block, Part(PartCategory.Block, 4))
            .Add(Thruster, Part(PartCategory.Interactive, 2, "scripts/thruster.lua") with
            {
                Properties = new System.Text.Json.Nodes.JsonObject { ["thrust"] = 50, ["name"] = "jet" },
            })
            .Add(Bearing, Part(PartCategory.Interactive, 3, "scripts/bearing.lua",
                new SocketDefinition { Id = "base", Type = SocketType.Structural, Position = new Vec3(0, -0.125f, 0), Normal = new Vec3(0, -1, 0) },
                new SocketDefinition { Id = "rotor", Type = SocketType.Rotational, Position = new Vec3(0, 0.125f, 0), Normal = new Vec3(0, 1, 0) }))
            .Add(Piston, Part(PartCategory.Interactive, 4, "scripts/piston.lua",
                new SocketDefinition { Id = "base", Type = SocketType.Structural, Position = new Vec3(0, -0.125f, 0), Normal = new Vec3(0, -1, 0) },
                new SocketDefinition
                {
                    Id = "head", Type = SocketType.Linear, Position = new Vec3(0, 0.125f, 0), Normal = new Vec3(0, 1, 0),
                    Joint = new JointDefinition { Max = 1 },
                }))
            .Add(Other, Part(PartCategory.Interactive, 1, "scripts/gadget.lua"));
        Motion = new MotionActions(new MotionSettings(), World);
        Motion.Register(Dispatcher);
        Runtime = new ScriptRuntime(settings ?? new ScriptSettings(), Dispatcher, World, physics ?? new NoPhysics(), new Source(this));
        Runtime.Message += m => Messages.Add(m);
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "mechanica-script-tests", Guid.NewGuid().ToString("N"));

    public PartCatalog Catalog { get; }

    public FakeWorld World { get; } = new();

    public ActionDispatcher Dispatcher { get; } = new();

    public MotionActions Motion { get; }

    public ScriptRuntime Runtime { get; }

    public List<ScriptMessage> Messages { get; } = [];

    public IEnumerable<ScriptMessage> Errors => Messages.Where(m => m.Kind == ScriptMessageKind.Error);

    public IEnumerable<string> Prints => Messages.Where(m => m.Kind == ScriptMessageKind.Print).Select(m => m.Text);

    /// <summary>Writes a script file of <paramref name="mod"/>.</summary>
    public void Write(string relative, string source, string mod = "test")
    {
        var path = Path.Combine(Root, mod, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        // Make sure a rewrite within the clock's resolution still looks changed.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddTicks(Interlocked.Increment(ref _stamp)));
    }

    private static long _stamp;

    /// <summary>A Creation with a block at the origin and <paramref name="parts"/> stacked on top.</summary>
    public Creation AddCreation(int id, params string[] parts)
    {
        var creation = new Creation(id, Catalog);
        Place(creation, Block, new GridCell(0, 0, 0));
        for (var i = 0; i < parts.Length; i++)
            Place(creation, parts[i], new GridCell(0, i + 1, 0));
        World.Creations[id] = creation;
        return creation;
    }

    public static PartInstance Place(Creation creation, string part, GridCell cell)
    {
        Assert.True(creation.TryPlacePart(part, cell, default, out var placed, out var error), error);
        return placed;
    }

    public static int PartOf(Creation creation, string type) => creation.Parts.First(p => p.Part == type).Id;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static PartDefinition Part(PartCategory category, float mass, string? script = null, params SocketDefinition[] sockets) => new()
    {
        Id = "x",
        Name = "X",
        Category = category,
        Mass = mass,
        Size = GridSize.One,
        Script = script,
        Sockets = sockets.Length == 0 ? null : [.. sockets],
    };

    private sealed class Source(ScriptRig rig) : IScriptSource
    {
        public ScriptFile? FindScript(string partId)
        {
            var qualified = Identifiers.Qualify(partId);
            if (rig.Catalog.FindDefinition(qualified)?.Script is not { } script)
                return null;
            var mod = qualified.Split(':')[0];
            return new ScriptFile($"{mod}:{script}", mod, Path.Combine(rig.Root, mod, script));
        }
    }

    public sealed class FakeWorld : IMotionWorld
    {
        public Dictionary<int, Creation> Creations { get; } = [];
        public HashSet<int> Parked { get; } = [];
        public List<(int Creation, int Body, Vector3 Force, Vector3 Torque, int? Part)> Pushes { get; } = [];
        public List<(int Part, float? Velocity, float MaxTorque)> Motors { get; } = [];

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public IEnumerable<Creation> AllCreations => Creations.Values;

        public bool IsLive(int id) => Creations.ContainsKey(id) && !Parked.Contains(id);

        public void ApplyForce(Creation creation, int body, Vector3 force, Vector3 torque, float? duration, int? part, Vector3? at, Vector3 ownTorque)
        {
            Pushes.Add((creation.Id, body, force, torque, part));
            Applied.Add((part, force, at, ownTorque));
        }

        /// <summary>Every push as the world got it: the part, the force, where it acts and the torque asked for on top.</summary>
        public List<(int? Part, Vector3 Force, Vector3? At, Vector3 Torque)> Applied { get; } = [];

        public List<JointMotor> JointMotors { get; } = [];

        public Dictionary<int, float> Positions { get; } = [];

        public void SetJointMotor(Creation creation, JointLink joint, JointMotor motor)
        {
            Motors.Add((joint.Connection.A, motor.Velocity, motor.MaxEffort));
            JointMotors.Add(motor);
        }

        public float? JointPosition(Creation creation, JointLink joint) =>
            Positions.TryGetValue(joint.Connection.A, out var p) ? p : null;
    }
}
