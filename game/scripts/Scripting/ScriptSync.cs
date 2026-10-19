using Godot;
using FileAccess = Godot.FileAccess;
using Mechanica.Core.Data;
using Mechanica.Game.Creations;
using Mechanica.Game.Motion;
using Mechanica.Game.Net;
using Mechanica.Game.Sound;
using Mechanica.Game.Survival;
using Mechanica.Scripting;

namespace Mechanica.Game.Scripting;

/// <summary>
/// Runs part scripts on the host (D2). Owns the <see cref="ScriptRuntime"/>, steps it every physics
/// tick before <see cref="MotionSync"/> applies the pushes it asked for, reloads scripts whose files
/// change, and calls <c>on_collision</c> when a scripted part's Body starts touching something. It
/// answers the scripts' physics queries (raycasts, overlaps, gravity) from Jolt and their sounds through
/// <see cref="SoundSync"/>. With <see cref="Survival"/> set it also runs held items' scripts (chunk 20b:
/// <c>UseItem</c>, <c>EquipItem</c>). Clients run no scripts: they see what the scripts do through
/// <see cref="CreationSync"/> snapshots and survival sync.
/// </summary>
public partial class ScriptSync : Node, IScriptPhysics, IScriptSound
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

    /// <summary>Sound, for the scripts' <c>sound</c> table. Set before adding to the tree.</summary>
    public SoundSync Sounds { get; init; } = null!;

    /// <summary>The loaded mods, whose parts' scripts run. Set before adding to the tree; see <see cref="SetContent"/>.</summary>
    public ModLoadResult Mods { get; set; } = null!;

    /// <summary>Survival, whose items' scripts run here (weapons, food). Set before adding to the tree; null runs part scripts only.</summary>
    public SurvivalSync? Survival { get; init; }

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
        Runtime.Sound = this;
        Runtime.Message += Log;
        if (Survival?.Actions is { } survival)
        {
            Runtime.EnableItems(Survival.Catalog, survival, Survival.Inventory.Items!);
            // A weapon's rounds loaded go to its holder's hotbar and HUD (UI spec §10).
            Runtime.ItemCounterChanged += Survival.SendCounter;
        }
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
    public string? Play(string sound, string ownerModId, System.Numerics.Vector3 position) =>
        Sounds.Emit(sound, ownerModId, position.ToGodot()) ? null : $"there is no sound event '{sound}'";

    /// <inheritdoc/>
    public int StartLoop(string sound, string ownerModId, int creation, int part, IReadOnlyDictionary<string, float>? parameters, out string? error)
    {
        error = Sounds.Registry.Find(sound, ownerModId) switch
        {
            null => $"there is no sound event '{sound}'",
            { Loop: false } => $"'{sound}' is not a loop; play it with sound.play",
            var e => parameters?.Keys.FirstOrDefault(name => e.FindParameter(name) is null) is { } unknown
                ? $"'{sound}' has no parameter '{unknown}'"
                : null,
        };
        if (error is not null)
            return 0;
        var handle = Sounds.StartLoop(sound, ownerModId, SoundAnchor.OnPart(creation, part), parameters);
        if (handle == 0)
            error = "only the host plays loops";
        return handle;
    }

    /// <inheritdoc/>
    public string? SetLoopParameter(int handle, string name, float value) =>
        Sounds.SetLoopParameter(handle, name, value) ? null
        : Sounds.Loops.TryGetValue(handle, out var loop) && loop.Event.FindParameter(name) is null ? $"'{loop.Event.QualifiedId}' has no parameter '{name}'"
        : Sounds.Loops.ContainsKey(handle) ? $"{name} must be a finite number"
        : "the loop has stopped";

    /// <inheritdoc/>
    public void StopLoop(int handle) => Sounds.StopLoop(handle);

    /// <inheritdoc/>
    public ScriptRayHit? Raycast(System.Numerics.Vector3 from, System.Numerics.Vector3 to, int? ignoreCreation)
    {
        var query = PhysicsRayQueryParameters3D.Create(from.ToGodot(), to.ToGodot());
        query.Exclude = Exclusions(ignoreCreation);
        var hit = GetViewport().FindWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return null;
        var position = hit["position"].AsVector3();
        var collider = hit["collider"].AsGodotObject();
        var (creation, part) = Identify(collider, hit["shape"].AsInt32());
        return new ScriptRayHit(position.ToNumerics(), hit["normal"].AsVector3().ToNumerics(), position.DistanceTo(from.ToGodot()), creation, part,
            (collider as Player.PlayerController)?.PeerId, (collider as Bots.BotBody)?.BotId);
    }

    // One sphere for every script's overlap query (they run at once); a new shape per call only goes when collected.
    private readonly SphereShape3D _sphere = new();
    private const int MaxOverlaps = 64;

    /// <inheritdoc/>
    public IReadOnlyList<ScriptOverlap> OverlapSphere(System.Numerics.Vector3 centre, float radius, int? ignoreCreation)
    {
        if (_sphere.Radius != radius)
            _sphere.Radius = radius;
        var exclude = Exclusions(ignoreCreation);
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _sphere,
            Transform = new Transform3D(Basis.Identity, centre.ToGodot()),
            Exclude = exclude,
        };
        var results = new List<ScriptOverlap>();
        var seen = new HashSet<(ulong Collider, int Shape)>();
        while (true)
        {
            var hits = GetViewport().FindWorld3D().DirectSpaceState.IntersectShape(query, MaxOverlaps);
            foreach (var hit in hits)
            {
                var collider = hit["collider"].AsGodotObject();
                if (!seen.Add((collider.GetInstanceId(), hit["shape"].AsInt32())))
                    continue;
                var (creation, part) = Identify(collider, hit["shape"].AsInt32());
                results.Add(new ScriptOverlap(creation, part, (collider as Player.PlayerController)?.PeerId, (collider as Bots.BotBody)?.BotId));
            }
            // A triangle mesh (a structure's walls) answers once per triangle and can fill every slot, hiding the
            // parts beside it: ask again without the bodies already found until a query comes back short.
            var found = hits.Select(h => h["rid"].AsRid()).Distinct().Where(r => !exclude.Contains(r)).ToList();
            if (hits.Count < MaxOverlaps || found.Count == 0)
                return results;
            foreach (var rid in found)
                exclude.Add(rid);
            query.Exclude = exclude;
        }
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

    private readonly List<(int Creation, int Part)> _listening = [];

    // on_collision(other, impulse) for each scripted part whose Body began touching something this step.
    private void ReportCollisions()
    {
        // Only parts whose script listens: contact monitoring costs Jolt work on every body it is on.
        Runtime.Defining(ScriptRuntime.Hooks.OnCollision, _listening);
        var now = new Dictionary<ulong, HashSet<(ulong Other, int Shape)>>();
        foreach (var info in _listening)
        {
            if (Creations.NodeOf(info.Creation) is not { } node
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
