using Godot;
using FileAccess = Godot.FileAccess;
using Mechanica.Core.Data;
using Mechanica.Game.Creations;
using Mechanica.Game.Motion;
using Mechanica.Game.Net;
using Mechanica.Scripting;

namespace Mechanica.Game.Scripting;

/// <summary>
/// Runs part scripts on the host (D2). Owns the <see cref="ScriptRuntime"/>, steps it every physics
/// tick before <see cref="MotionSync"/> applies the pushes it asked for, reloads scripts whose files
/// change, and calls <c>on_collision</c> when a scripted part's Body starts touching something. It
/// answers the scripts' physics queries (raycasts, overlaps, gravity) from Jolt. Clients run no
/// scripts: they see what the scripts do through <see cref="CreationSync"/> snapshots.
/// </summary>
public partial class ScriptSync : Node, IScriptPhysics
{
    /// <summary>Where the scripting limits are read from.</summary>
    public const string SettingsPath = "res://data/scripting.json";

    // Contacts each body reported last step, to call on_collision only when one begins.
    private readonly Dictionary<ulong, HashSet<(ulong Other, int Shape)>> _contacts = [];
    private double _sincePoll;

    /// <summary>Session, to run scripts only on the host. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The Creations. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>The motion world scripts read and push. Set before adding to the tree.</summary>
    public MotionSync Motion { get; init; } = null!;

    /// <summary>The host's Action API. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The loaded mods, whose parts' scripts run. Set before adding to the tree; see <see cref="SetContent"/>.</summary>
    public ModLoadResult Mods { get; set; } = null!;

    /// <summary>Scripting limits; null loads <see cref="SettingsPath"/>.</summary>
    public ScriptSettings? Settings { get; set; }

    /// <summary>False pauses every part script (for benchmarks and debugging); they keep their state.</summary>
    public bool Running { get; set; } = true;

    /// <summary>Wall time the last step's scripts took, in ms.</summary>
    public double LastStepMilliseconds { get; private set; }

    /// <summary>The runtime, once the node is ready.</summary>
    public ScriptRuntime Runtime { get; private set; } = null!;

    /// <inheritdoc/>
    public System.Numerics.Vector3 Gravity =>
        ((float)ProjectSettings.GetSetting("physics/3d/default_gravity") *
         ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3()).ToNumerics();

    public override void _Ready()
    {
        // Before MotionSync, so a push asked for this step is applied this step.
        ProcessPhysicsPriority = Motion.ProcessPhysicsPriority - 1;
        Settings ??= LoadSettings();
        // The graphs alone keep the instances in step; poses are read (and written back) only for parts that ask.
        Runtime = new ScriptRuntime(Settings, Actions.Dispatcher, Motion, this, new ModScriptSource(Mods), () => Creations.All);
        Runtime.Message += Log;
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree()
    {
        Session.StateChanged -= OnStateChanged;
        Runtime.Clear();
    }

    /// <summary>Reads <see cref="SettingsPath"/>, failing loudly on bad data.</summary>
    public static ScriptSettings LoadSettings()
    {
        using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {SettingsPath}: {FileAccess.GetOpenError()}");
        return ScriptSettings.FromJson(file.GetAsText());
    }

    /// <summary>Switches to newly loaded mods: parts whose script changed restart on the new one.</summary>
    public void SetContent(ModLoadResult mods)
    {
        Mods = mods;
        Runtime.Source = new ModScriptSource(mods);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost)
            return;
        _sincePoll += delta;
        if (_sincePoll >= Settings!.ReloadPollSeconds)
        {
            _sincePoll = 0;
            Runtime.CheckForChanges();
        }
        if (!Running)
        {
            LastStepMilliseconds = 0;
            return;
        }
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        Runtime.Update((float)delta);
        ReportCollisions();
        LastStepMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <inheritdoc/>
    public ScriptRayHit? Raycast(System.Numerics.Vector3 from, System.Numerics.Vector3 to, int? ignoreCreation)
    {
        var query = PhysicsRayQueryParameters3D.Create(from.ToGodot(), to.ToGodot());
        query.Exclude = Exclusions(ignoreCreation);
        var hit = GetViewport().FindWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return null;
        var position = hit["position"].AsVector3();
        var (creation, part) = Identify(hit["collider"].AsGodotObject(), hit["shape"].AsInt32());
        return new ScriptRayHit(position.ToNumerics(), hit["normal"].AsVector3().ToNumerics(), position.DistanceTo(from.ToGodot()), creation, part);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ScriptOverlap> OverlapSphere(System.Numerics.Vector3 centre, float radius, int? ignoreCreation)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, centre.ToGodot()),
            Exclude = Exclusions(ignoreCreation),
        };
        var results = new List<ScriptOverlap>();
        foreach (var hit in GetViewport().FindWorld3D().DirectSpaceState.IntersectShape(query, 64))
        {
            var (creation, part) = Identify(hit["collider"].AsGodotObject(), hit["shape"].AsInt32());
            results.Add(new ScriptOverlap(creation, part));
        }
        return results;
    }

    private Godot.Collections.Array<Rid> Exclusions(int? creation)
    {
        var rids = new Godot.Collections.Array<Rid>();
        if (creation is { } id && Creations.NodeOf(id) is { } node)
        {
            foreach (var body in node.Bodies)
                rids.Add(body.GetRid());
        }
        return rids;
    }

    // The Creation and part behind a collider and shape index; nulls for terrain and props.
    private static (int? Creation, int? Part) Identify(GodotObject? collider, int shape)
    {
        if (collider is not CreationBody body || body.GetParent() is not CreationNode node)
            return (null, null);
        var owner = body.ShapeFindOwner(shape);
        var part = body.ShapeOwnerGetOwner(owner) is CollisionShape3D { Name: var name } && name.ToString() is var text
            && text.StartsWith("Shape", StringComparison.Ordinal) && int.TryParse(text.AsSpan(5), out var id)
            ? id
            : (int?)null;
        return (node.Creation.Id, part);
    }

    // on_collision(other, impulse) for each scripted part whose Body began touching something this step.
    private void ReportCollisions()
    {
        var now = new Dictionary<ulong, HashSet<(ulong Other, int Shape)>>();
        foreach (var info in Runtime.Instances)
        {
            if (info.Fault is not null || Creations.NodeOf(info.Creation) is not { } node
                || Creations.Find(info.Creation) is not { } creation || creation.FindPart(info.Part) is null)
                continue;
            var index = creation.BodyIndexOf(info.Part);
            if (index >= node.Bodies.Count)
                continue;
            var body = node.Bodies[index];
            if (!body.ContactMonitor)
            {
                // Bodies are rebuilt on every graph change, so this is set again as needed.
                body.ContactMonitor = true;
                body.MaxContactsReported = 8;
            }
            if (PhysicsServer3D.BodyGetDirectState(body.GetRid()) is not { } state)
                continue;
            var key = body.GetInstanceId();
            if (!now.TryGetValue(key, out var touching))
            {
                now[key] = touching = [];
                for (var i = 0; i < state.GetContactCount(); i++)
                    touching.Add((state.GetContactColliderId(i), state.GetContactLocalShape(i)));
            }
            var before = _contacts.GetValueOrDefault(key);
            for (var i = 0; i < state.GetContactCount(); i++)
            {
                var shape = state.GetContactLocalShape(i);
                var pair = (state.GetContactColliderId(i), shape);
                if (before?.Contains(pair) == true)
                    continue;
                before ??= [];
                before.Add(pair); // once per pair, however many contact points it has
                var (otherCreation, otherPart) = Identify(state.GetContactColliderObject(i), state.GetContactColliderShape(i));
                var other = new System.Text.Json.Nodes.JsonObject();
                if (otherCreation is { } c)
                    other["creation"] = c;
                if (otherPart is { } p)
                    other["part"] = p;
                Runtime.Invoke(info.Creation, info.Part, ScriptRuntime.Hooks.OnCollision, other, (double)state.GetContactImpulse(i).Length());
            }
        }
        _contacts.Clear();
        foreach (var (key, touching) in now)
            _contacts[key] = touching;
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client mirrors the host and runs nothing; parts start afresh if this peer hosts again.
        if (state is NetSession.SessionState.Connecting or NetSession.SessionState.Connected)
            Runtime.Clear();
    }

    private static void Log(ScriptMessage message)
    {
        switch (message.Kind)
        {
            case ScriptMessageKind.Error:
                GD.PushWarning($"[lua] {message}");
                break;
            case ScriptMessageKind.Warning:
                GD.PushWarning($"[lua] {message}");
                break;
            default:
                GD.Print($"[lua] {message}");
                break;
        }
    }
}
