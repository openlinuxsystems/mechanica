using Godot;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Core.Sound;
using Mechanica.Core.Survival;
using Mechanica.Game.Creations;
using Mechanica.Game.Sound;
using Mechanica.Core;

namespace Mechanica.Game.Survival;

/// <summary>
/// How soil mounds look on every peer. Each mound's material (<see cref="SoilMaterial"/>) blends towards the look of its
/// wetness (<see cref="SoilView.Wetness"/>, sent by <see cref="SurvivalSync"/>; a mound with no view is dry) and
/// darkens for a moment when it takes water. A mound that appears where its place sound just played was spilled out
/// of a bag: the bag shows, tips over and empties, dirt falls and the mound settles. That rides on the sound
/// (<see cref="SoundSync.Played"/>), so it costs no traffic of its own.
/// </summary>
public partial class SoilLooks : Node3D
{
    // Set on soil meshes whenever their look changes: one StringName each rather than one per call.
    private static readonly StringName WetName = SoilMaterial.Wet;
    private static readonly StringName DryingName = SoilMaterial.Drying;
    private static readonly StringName FlashName = SoilMaterial.Flash;
    private static readonly StringName FertilizedName = SoilMaterial.Fertilized;

    /// <summary>Seconds a change of wetness takes to blend in.</summary>
    public const float BlendTime = 1.5f;

    /// <summary>Seconds from the bag showing to the mound settled.</summary>
    public const float SpillTime = 0.9f;

    private const float SoundReach = 0.3f;
    private const float SoundAge = 0.5f;

    private static readonly Color Dirt = new(0.36f, 0.26f, 0.17f);
    private static readonly Color Dust = new(0.55f, 0.45f, 0.34f, 0.5f);

    private readonly Dictionary<(int Creation, int Part), Look> _looks = [];
    private readonly List<(Vector3 At, double Time)> _sounds = [];
    private readonly HashSet<(int Creation, int Part)> _seen = [];
    private readonly HashSet<(int Creation, int Part)> _live = [];
    private bool _started;

    private sealed class Look
    {
        public float Wet;
        public float Drying;
        public float Flash;
        public float Fertilized;
        public float Water;
        public double SpillStart = -1;
        public Node3D? Bag;

        // The part's model and meshes, found once per part node (a rebuilt Creation has new ones), and the look last
        // set on them, so a settled mound costs no engine calls.
        public Node3D? PartNode;
        public Node3D? Model;
        public MeshInstance3D[] Meshes = [];
        public (float Wet, float Drying, float Flash, float Fertilized) Shown = (float.NaN, 0, 0, 0);
    }

    /// <summary>The live Creations. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>The soil views. Set before adding to the tree.</summary>
    public SurvivalSync Survival { get; init; } = null!;

    /// <summary>The world's sounds. Set before adding to the tree.</summary>
    public SoundSync Sounds { get; init; } = null!;

    /// <summary>Every part definition, for the bag a mound spills out of. Set before adding to the tree.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>Spills shown so far (tests count them).</summary>
    public int Spills { get; private set; }

    // Game time (it follows Engine.TimeScale), in seconds.
    private double _now;

    /// <summary>How wet a mound looks right now (0 dry to 1 soaked) and how much drying crust it shows; null for no mound.</summary>
    public (float Wet, float Drying)? LookOf(int creation, int part) =>
        _looks.TryGetValue((creation, part), out var look) ? (look.Wet, look.Drying) : null;

    /// <summary>How much of a fertilized crop's granules a mound shows right now (0 to 1); null for no mound.</summary>
    public float? FertilizedOf(int creation, int part) => _looks.TryGetValue((creation, part), out var look) ? look.Fertilized : null;

    /// <summary>The look a wetness blends towards: how wet, and how much drying crust.</summary>
    public static (float Wet, float Drying) Target(SoilWetness wetness) => wetness switch
    {
        SoilWetness.Watered => (0.45f, 0f),
        SoilWetness.Wet => (1f, 0f),
        SoilWetness.Drying => (0.55f, 1f),
        _ => (0f, 0f),
    };

    public override void _Ready() => Sounds.Played += OnPlayed;

    public override void _ExitTree() => Sounds.Played -= OnPlayed;

    private void OnPlayed(SoundEvent sound, Vector3 at) => _sounds.Add((at, _now));

    public override void _Process(double delta)
    {
        var now = _now += delta;
        _sounds.RemoveAll(s => now - s.Time > SoundAge);
        var step = (float)delta / BlendTime;
        _live.Clear();
        foreach (var node in Creations.Nodes)
        {
            foreach (var part in node.SoilParts)
            {
                if (node.PartNode(part) is not { } partNode)
                    continue;
                var key = (node.Creation.Id, part);
                _looks.TryGetValue(key, out var look);
                var model = look is not null && look.PartNode == partNode && IsInstanceValid(look.Model) ? look.Model : partNode.GetNodeOrNull<Node3D>("Model");
                if (model is null)
                    continue;
                _live.Add(key);
                if (look is null)
                {
                    look = _looks[key] = new Look();
                    // Mounds there when the world loads were not just spilled, nor is soil that comes out of no bag (a Growbed).
                    if (_started && !_seen.Contains(key) && node.Creation.DefinitionOf(part).PlacedFrom is not null
                        && _sounds.Any(s => s.At.DistanceTo(partNode.GlobalPosition) < SoundReach))
                        StartSpill(look, node, part, partNode, now);
                }
                if (look.Model != model)
                    Take(look, partNode, model);
                _seen.Add(key);
                var view = Survival.Soil.GetValueOrDefault(key);
                var (wet, drying) = Target(view?.Wetness ?? SoilWetness.Dry);
                var water = view?.Water ?? 0;
                if (water > look.Water + 1f)
                    look.Flash = 1f;
                look.Water = water;
                look.Wet = Mathf.MoveToward(look.Wet, wet, step);
                look.Drying = Mathf.MoveToward(look.Drying, drying, step);
                look.Flash = Mathf.MoveToward(look.Flash, 0f, (float)delta * 2f);
                look.Fertilized = Mathf.MoveToward(look.Fertilized, view is { Seed: not null, Boost: > 1f } ? 1f : 0f, step);
                Apply(look);
                Settle(look, node, part, model, now);
            }
        }
        if (_looks.Count != _live.Count)
        {
            foreach (var gone in _looks.Keys.Where(k => !_live.Contains(k)).ToList())
            {
                _looks[gone].Bag?.QueueFree();
                _looks.Remove(gone);
            }
        }
        _seen.IntersectWith(_looks.Keys);
        _started = true;
    }

    // Swaps the soil meshes of a part's new model to the soil material (the nodes are remade whenever the Creation is rebuilt).
    private static void Take(Look look, Node3D partNode, Node3D model)
    {
        look.PartNode = partNode;
        look.Model = model;
        look.Meshes = SoilMeshes(model);
        look.Shown = (float.NaN, 0, 0, 0);
        foreach (var mesh in look.Meshes)
        {
            for (var i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
            {
                var material = mesh.GetSurfaceOverrideMaterial(i) ?? mesh.Mesh?.SurfaceGetMaterial(i);
                if (material is BaseMaterial3D source)
                    mesh.SetSurfaceOverrideMaterial(i, SoilMaterial.For(source));
            }
        }
    }

    /// <summary>
    /// The meshes of a soil part's model that are soil: those named <c>Soil…</c> anywhere in it (a Growbed's fill, not its
    /// planks), or every mesh of a model that names none (a mound is all soil).
    /// </summary>
    public static MeshInstance3D[] SoilMeshes(Node3D model)
    {
        var all = model.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false).OfType<MeshInstance3D>().ToArray();
        var named = all.Where(m => m.Name.ToString().StartsWith("Soil", StringComparison.Ordinal)).ToArray();
        return named.Length > 0 ? named : all;
    }

    // Sets the look on the meshes when it changed.
    private static void Apply(Look look)
    {
        var shown = (look.Wet, look.Drying, look.Flash, look.Fertilized);
        if (shown == look.Shown)
            return;
        look.Shown = shown;
        foreach (var mesh in look.Meshes)
        {
            if (!IsInstanceValid(mesh))
                continue;
            mesh.SetInstanceShaderParameter(WetName, look.Wet);
            mesh.SetInstanceShaderParameter(DryingName, look.Drying);
            mesh.SetInstanceShaderParameter(FlashName, look.Flash);
            mesh.SetInstanceShaderParameter(FertilizedName, look.Fertilized);
        }
    }

    private void StartSpill(Look look, CreationNode node, int part, Node3D partNode, double now)
    {
        Spills++;
        look.SpillStart = now;
        var item = PartItems.ItemFor(Catalog, node.Creation.FindPart(part)!.Part);
        if (item != Identifiers.Qualify(node.Creation.FindPart(part)!.Part) && Catalog.FindDefinition(item) is { } bag)
        {
            var shown = new Node3D { Name = "SpillBag", TopLevel = true };
            foreach (var mesh in node.Assets.ModelFor(item, bag).Meshes)
                shown.AddChild(mesh.Instantiate());
            AddChild(shown);
            look.Bag = shown;
        }
        var root = new Node3D { Name = "Spill", Position = partNode.GlobalPosition };
        AddChild(root);
        var fall = Building.SledgeImpacts.Emitter(26, 0.55f, new Vector2(0.4f, 1.4f), -9.8f, 35f, Building.SledgeImpacts.Box(0.03f), Dirt);
        fall.Position = new Vector3(0, 0.35f, 0);
        fall.Direction = Vector3.Down;
        fall.EmissionSphereRadius = 0.12f;
        root.AddChild(fall);
        var puff = Building.SledgeImpacts.Emitter(10, 0.8f, new Vector2(0.3f, 0.9f), -0.8f, 85f, Building.SledgeImpacts.Quad(0.22f), Dust, grow: true);
        puff.EmissionSphereRadius = 0.3f;
        root.AddChild(puff);
        GetTree().CreateTimer(SpillTime + 0.6f).Timeout += root.QueueFree;
    }

    // The spill: the bag tips over above the mound and shrinks away while the mound rises from the ground and settles.
    private static void Settle(Look look, CreationNode node, int part, Node3D model, double now)
    {
        if (look.SpillStart < 0)
            return;
        var t = (float)((now - look.SpillStart) / SpillTime);
        var half = (node.Creation.DefinitionOf(part).Size?.Y ?? 1) * MechanicaInfo.GridSize / 2;
        if (t >= 1f)
        {
            model.Transform = Transform3D.Identity;
            look.SpillStart = -1;
            look.Bag?.QueueFree();
            look.Bag = null;
            return;
        }
        var rise = Mathf.Clamp((t - 0.25f) / 0.6f, 0f, 1f);
        // Overshoots a little and settles back.
        var height = Mathf.Max(0.04f, 1f + 2.70158f * Mathf.Pow(rise - 1f, 3) + 1.70158f * Mathf.Pow(rise - 1f, 2));
        var width = 0.8f + 0.2f * Mathf.Min(1f, rise * 1.4f);
        model.Transform = new Transform3D(Basis.FromScale(new Vector3(width, height, width)), new Vector3(0, -half * (1f - height), 0));
        if (look.Bag is { } bag)
        {
            var tip = Mathf.Clamp(t / 0.4f, 0f, 1f);
            var shrink = 1f - Mathf.Clamp((t - 0.3f) / 0.15f, 0f, 1f);
            bag.Visible = shrink > 0f;
            var frame = model.GetParent<Node3D>().GlobalTransform;
            var turn = new Basis(Vector3.Right, Mathf.DegToRad(125f * tip)).Scaled(Vector3.One * 0.6f * Mathf.Max(shrink, 0.001f));
            bag.GlobalTransform = frame * new Transform3D(turn, new Vector3(0, 0.3f + 0.1f * tip, 0));
        }
    }
}
