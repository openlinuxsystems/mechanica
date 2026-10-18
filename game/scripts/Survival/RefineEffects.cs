using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Sound;
using Mechanica.Game.World;

namespace Mechanica.Game.Survival;

/// <summary>
/// What every peer sees and hears of a player refining a resource node: the Refine Tool's grind (<c>refiner_start</c>
/// as it is drawn, the <c>refiner_work</c> loop while it is worked) and, at each stroke's bite, chips the colour of the
/// node flying off where the player aims, with sparks off metal. It follows the Refine Tool in each player's hand
/// (<see cref="CharacterAnimator.Prop"/>), which every peer already knows, so it costs no traffic. The host plays
/// <c>refiner_complete</c> when the node is refined (<see cref="Core.Survival.SurvivalActions.RefineCompleteSound"/>).
/// </summary>
public partial class RefineEffects : Node3D
{
    /// <summary>Played on every peer as a player draws the Refine Tool.</summary>
    public const string StartSound = "refiner_start";

    /// <summary>Looped on every peer while a player works the Refine Tool.</summary>
    public const string WorkSound = "refiner_work";

    // Where in the stroke the teeth bite (hold_refine shoves forward over its first 45%).
    private const float Bite = 0.3f;
    private const float Lifetime = 0.7f;

    private static readonly Color Dust = new(0.6f, 0.55f, 0.48f, 0.45f);
    private static readonly Color Spark = new(1f, 0.9f, 0.6f);

    private readonly Dictionary<int, Worker> _workers = [];

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Where the sounds play. Set before adding to the tree.</summary>
    public SoundSync Sounds { get; init; } = null!;

    /// <summary>The world's nodes and items, for the chips' colour. Set before adding to the tree.</summary>
    public SurvivalSync Survival { get; init; } = null!;

    /// <summary>Chip bursts thrown so far (tests count them).</summary>
    public int Bursts { get; private set; }

    /// <summary>Peers working the Refine Tool now, as this peer sees them.</summary>
    public IEnumerable<int> Working => _workers.Where(w => w.Value.Loop is not null).Select(w => w.Key);

    public override void _Process(double delta)
    {
        foreach (var (peer, player) in Players.Players)
        {
            var worker = _workers.GetValueOrDefault(peer);
            if (Shown(player) is not { } model || model.Animator.Prop != CharacterProp.RefineTool)
            {
                if (worker is not null)
                    Stop(peer, worker);
                continue;
            }
            if (worker is null)
            {
                _workers[peer] = worker = new Worker();
                Sounds.PlayLocal(StartSound, Identifiers.VanillaModId, player.GlobalPosition);
            }
            var phase = StrokePhase(model);
            if (phase is null)
            {
                // Drawn or being put away: no grind yet, or no more.
                StopLoop(worker);
                worker.Phase = null;
                continue;
            }
            if (worker.Loop is null && Sounds.Registry.Find(WorkSound) is { } work)
                worker.Loop = Sounds.Player.Play(work, player.GlobalPosition, () => IsInstanceValid(player) ? player.GlobalPosition : null);
            if (worker.Phase is { } before && Crossed(before, phase.Value, Bite))
                Burst(player, model);
            worker.Phase = phase;
        }
        foreach (var peer in _workers.Keys.Where(p => !Players.Players.ContainsKey(p)).ToList())
            Stop(peer, _workers[peer]);
    }

    public override void _ExitTree()
    {
        foreach (var (peer, worker) in _workers.ToList())
            Stop(peer, worker);
    }

    // The model whose hand shows: the first-person arms while the player looks out of its own eyes, else the body.
    private static CharacterModel? Shown(PlayerController player) =>
        player.Arms is CharacterModel { Visible: true } arms && arms.PropNode is not null ? arms : player.Character as CharacterModel;

    // How far through its stroke the working loop is, 0 to 1, or null while the tool is drawn or put away.
    private static float? StrokePhase(CharacterModel model)
    {
        foreach (var clip in model.Animator.Upper)
        {
            if (clip.Clip.StartsWith("hold_refine", StringComparison.Ordinal) && clip.Weight >= 0.5f
                && model.Assets.ClipLengths.TryGetValue(clip.Clip, out var length) && length > 0f)
                return clip.Time / length;
        }
        return null;
    }

    private static bool Crossed(float before, float after, float mark) =>
        after >= before ? before < mark && after >= mark : before < mark || after >= mark;

    private void Stop(int peer, Worker worker)
    {
        StopLoop(worker);
        _workers.Remove(peer);
    }

    private void StopLoop(Worker worker)
    {
        if (worker.Loop is { } loop)
            Sounds.Player.Stop(loop);
        worker.Loop = null;
    }

    // Chips off the node where the player aims, or off the blade when it aims at nothing it can refine.
    private void Burst(PlayerController player, CharacterModel model)
    {
        var reach = Survival.Settings?.Reach ?? 7.5f;
        var node = HandTool.Cast(player, reach, brush: true) is { } hit && TerrainChunk.ObjectHit(hit.Collider, hit.Shape) is { } id
            && Survival.FindObject(id) is { Definition.Refine: true } found
            ? (found, hit.Point)
            : ((WorldObjectPlacement, Vector3)?)null;
        var at = node?.Item2 ?? model.PropNode?.ToGlobal(new Vector3(0.035f, 0f, -0.17f)) ?? player.GlobalPosition;
        var colour = node?.Item1.Definition.Pieces?.FirstOrDefault()?.Color is { } hex && Color.HtmlIsValid(hex) ? Color.FromHtml(hex) : new Color(0.45f, 0.42f, 0.38f);
        var metal = node?.Item1.Definition.Drops?.Any(d => d.Item is { } item
            && Survival.Catalog.FindDefinition(item)?.SoundMaterial == "metal") == true;
        Bursts++;
        var root = new Node3D { Name = "Chips", Position = at };
        AddChild(root);
        root.AddChild(SledgeImpacts.Emitter(5, 0.55f, new Vector2(0.4f, 1.0f), -1.2f, 70f, SledgeImpacts.Quad(0.1f), Dust, grow: true));
        root.AddChild(SledgeImpacts.Emitter(7, 0.5f, new Vector2(1.2f, 2.6f), -9.8f, 55f, SledgeImpacts.Box(0.018f), colour.Darkened(0.1f)));
        if (metal)
            root.AddChild(SledgeImpacts.Emitter(6, 0.18f, new Vector2(3f, 5.5f), -4f, 70f, SledgeImpacts.Quad(0.022f), Spark, glow: true));
        GetTree().CreateTimer(Lifetime + 0.2f).Timeout += root.QueueFree;
    }

    private sealed class Worker
    {
        public SoundPlayer.Voice? Loop { get; set; }

        public float? Phase { get; set; }
    }
}
