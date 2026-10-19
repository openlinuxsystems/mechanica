using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using Mechanica.Core.Tests.Survival;

namespace Mechanica.Scripting.Tests;

/// <summary>
/// The survival test world with item scripts running: the vanilla food and potato gun scripts from
/// <c>mods/vanilla/scripts</c>, and any script a test writes (which wins over vanilla's).
/// </summary>
internal sealed class ItemRig : IDisposable
{
    public static readonly string VanillaRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "mods", "vanilla"));

    public ItemRig(SurvivalFixture? fixture = null, ScriptSettings? settings = null, IScriptPhysics? physics = null)
    {
        Directory.CreateDirectory(Root);
        F = fixture ?? new SurvivalFixture();
        Runtime = new ScriptRuntime(settings ?? new ScriptSettings(), F.Dispatcher, new Motion(this), physics ?? new NoPhysics(), new Source(this));
        Runtime.EnableItems(F.Catalog, F.Survival, F.Inventories);
        Runtime.Fire = F.Fire;
        Runtime.Message += Messages.Add;
    }

    public SurvivalFixture F { get; }

    public ScriptRuntime Runtime { get; }

    public List<ScriptMessage> Messages { get; } = [];

    /// <summary>Mod scripts of the test mod that run for the world (<c>scripts/x.lua</c>, relative to <see cref="Root"/>).</summary>
    public List<string> ModScripts { get; } = [];

    /// <summary>Creations that are parked (not live), so their parts get no <c>on_update</c>.</summary>
    public HashSet<int> Parked { get; } = [];

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "mechanica-item-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Writes a script of the test mod.</summary>
    public void Write(string relative, string source)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddTicks(Interlocked.Increment(ref _stamp)));
    }

    private static long _stamp;

    /// <summary>Adds an item that runs <paramref name="script"/> with <paramref name="properties"/>.</summary>
    public string AddItem(string id, string script, JsonObject? properties = null, PartCategory category = PartCategory.Consumable)
    {
        var qualified = $"test:{id}";
        F.Catalog.Add(qualified, new PartDefinition
        {
            Id = id,
            Name = char.ToUpperInvariant(id[0]) + id[1..],
            Category = category,
            StackSize = 10,
            Script = script,
            Properties = properties,
        });
        return qualified;
    }

    public ActionResult Use(ActionCaller caller, int slot, Vec3? direction = null, (int Creation, int Part)? on = null)
    {
        var args = new Dictionary<string, object> { ["slot"] = slot };
        if (direction is { } d)
            args["direction"] = d;
        if (on is { } target)
            (args["creation"], args["part"]) = (target.Creation, target.Part);
        return F.Dispatch(caller, ScriptRuntime.UseItem, args);
    }

    public JsonNode Used(ActionCaller caller, int slot, Vec3? direction = null, (int Creation, int Part)? on = null)
    {
        var result = Use(caller, slot, direction, on);
        Assert.True(result.Ok, result.ToString());
        return JsonNode.Parse(result.Value!.Value.GetRawText())!;
    }

    public string Refused(ActionCaller caller, int slot, Vec3? direction = null, (int Creation, int Part)? on = null)
    {
        var result = Use(caller, slot, direction, on);
        Assert.False(result.Ok, "the use should be refused");
        return result.Message!;
    }

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

    private sealed class Source(ItemRig rig) : IScriptSource
    {
        public ScriptFile? FindScript(string partId)
        {
            var qualified = Identifiers.Qualify(partId);
            if (rig.F.Catalog.FindDefinition(qualified)?.Script is not { } script)
                return null;
            var own = Path.Combine(rig.Root, script);
            var path = File.Exists(own) ? own : Path.Combine(VanillaRoot, script);
            return new ScriptFile($"test:{script}", "test", path);
        }

        public IReadOnlyList<ScriptFile> WorldScripts => [.. rig.ModScripts.Select(s => new ScriptFile($"test:{s}", "test", Path.Combine(rig.Root, s)))];
    }

    // Item scripts move nothing; the runtime still wants a motion world for part scripts.
    private sealed class Motion(ItemRig rig) : IMotionWorld
    {
        public Creation? FindCreation(int id) => rig.F.World.FindCreation(id);

        public IEnumerable<Creation> AllCreations => rig.F.World.AllCreations;

        public bool IsLive(int id) => rig.F.World.Creations.ContainsKey(id) && !rig.Parked.Contains(id);

        public void ApplyForce(Creation creation, int body, Vector3 force, Vector3 torque, float? duration, int? part, Vector3? at, Vector3 ownTorque)
        {
        }

        public void SetJointMotor(Creation creation, JointLink joint, JointMotor motor)
        {
        }

        public float? JointPosition(Creation creation, JointLink joint) => null;
    }
}
