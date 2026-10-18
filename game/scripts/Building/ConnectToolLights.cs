using Godot;
using Mechanica.Core.Characters;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Building;

/// <summary>
/// The Connect Tool's cyan lights (its rings, display, status slot and sensor tip: the prop's <c>connect_tool_glow</c>
/// node) in every player's hand, as every peer sees them. They glow softly while the tool is out, pulse every
/// <see cref="PulseSeconds"/> while it is raised over a node or dragging a link, flash for <see cref="ClickSeconds"/>
/// as a link is made or taken away (<c>connect_click</c>), and dip then flash as a bearing is reversed
/// (<c>connect_reverse</c>); a small light at the tip lights the hand and whatever is near it. It reads only what the
/// character's animator already plays from the prop, guard and gesture bytes, so it costs no traffic.
/// </summary>
public partial class ConnectToolLights : Node
{
    /// <summary>The glowing node in the Connect Tool prop.</summary>
    public const string GlowNode = "connect_tool_glow";

    /// <summary>Seconds of one pulse while the tool is raised (the node pulse of UI spec §12).</summary>
    public const float PulseSeconds = 0.8f;

    /// <summary>Seconds a click's flash lasts.</summary>
    public const float ClickSeconds = 0.2f;

    /// <summary>Emission while the tool is out and idle.</summary>
    public const float Idle = 0.8f;

    // The sensor tip in the prop's own space: connect_tip() in tools/characters/build_characters.py, glTF y-up.
    private static readonly Vector3 Tip = new(0.142f, 0f, -0.095f);
    private static readonly Color Cyan = new("#2fd8ff");

    private readonly Dictionary<CharacterModel, Lit> _lit = [];

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>How bright a player's Connect Tool glows in the model shown for them now, or null when they do not hold it.</summary>
    public float? GlowOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) && Shown(player) is { } model && _lit.TryGetValue(model, out var lit) ? lit.Energy : null;

    public override void _Process(double delta)
    {
        var seen = new HashSet<CharacterModel>();
        foreach (var player in Players.Players.Values)
        {
            foreach (var model in new[] { player.Character as CharacterModel, player.Arms as CharacterModel })
            {
                if (model is null || !IsInstanceValid(model) || model.Animator.Prop != CharacterProp.ConnectTool || model.PropNode is not { } prop)
                    continue;
                if (!_lit.TryGetValue(model, out var lit) || lit.Prop != prop || !IsInstanceValid(lit.Light))
                    _lit[model] = lit = Light(prop);
                if (lit.Glow is null)
                    continue;
                seen.Add(model);
                lit.Energy = Energy(model.Animator, (float)Time.GetTicksMsec() / 1000f);
                lit.Material.EmissionEnergyMultiplier = lit.Energy;
                lit.Light.LightEnergy = Mathf.Max(0f, lit.Energy - Idle) * 0.35f;
                lit.Light.Visible = lit.Light.LightEnergy > 0.01f;
            }
        }
        foreach (var model in _lit.Keys.Where(m => !seen.Contains(m)).ToList())
            _lit.Remove(model);
    }

    /// <summary>
    /// How bright the lights are for a character playing <paramref name="animator"/>'s clips at
    /// <paramref name="time"/> seconds: <see cref="Idle"/>, pulsing while raised, flashing on a click and dipping
    /// then flashing on a reverse.
    /// </summary>
    public static float Energy(CharacterAnimator animator, float time)
    {
        var energy = Idle;
        var raised = 0f;
        foreach (var clip in animator.Upper)
        {
            var name = clip.Clip.EndsWith(CharacterRig.FirstPersonSuffix, StringComparison.Ordinal) ? clip.Clip[..^CharacterRig.FirstPersonSuffix.Length] : clip.Clip;
            if (name == "hold_connect_aim")
                raised = Math.Max(raised, clip.Weight);
            else if (name == "connect_click" && clip.Time < ClickSeconds)
                energy += 4f * (1f - clip.Time / ClickSeconds);
            else if (name == "connect_reverse")
                energy = clip.Time < 0.08f ? energy * (1f - clip.Time / 0.08f * 0.85f)
                    : clip.Time < 0.08f + ClickSeconds ? energy + 4f * (1f - (clip.Time - 0.08f) / ClickSeconds) : energy;
        }
        var pulse = 0.5f + 0.5f * MathF.Sin(MathF.Tau * time / PulseSeconds);
        return energy + raised * (0.6f + 1.2f * pulse);
    }

    // The model whose hand shows: the first-person arms while the player looks out of its own eyes, else the body.
    private static CharacterModel? Shown(PlayerController player) =>
        player.Arms is CharacterModel { Visible: true } arms && arms.PropNode is not null ? arms : player.Character as CharacterModel;

    // Gives a prop's glow a material of its own (so one player's pulse is not everyone's) and a light at the tip.
    private static Lit Light(Node3D prop)
    {
        var glow = prop.FindChild(GlowNode, recursive: true, owned: false) as MeshInstance3D;
        var material = new StandardMaterial3D
        {
            AlbedoColor = Cyan,
            EmissionEnabled = true,
            Emission = Cyan,
            EmissionEnergyMultiplier = Idle,
            Roughness = 0.3f,
        };
        if (glow is not null)
        {
            for (var i = 0; i < glow.GetSurfaceOverrideMaterialCount(); i++)
                glow.SetSurfaceOverrideMaterial(i, material);
        }
        var light = new OmniLight3D { Name = "TipLight", Position = Tip, LightColor = Cyan, OmniRange = 0.35f, LightEnergy = 0f, Visible = false, ShadowEnabled = false };
        prop.AddChild(light);
        return new Lit(prop, glow, material, light);
    }

    private sealed class Lit(Node3D prop, MeshInstance3D? glow, StandardMaterial3D material, OmniLight3D light)
    {
        public Node3D Prop { get; } = prop;

        public MeshInstance3D? Glow { get; } = glow;

        public StandardMaterial3D Material { get; } = material;

        public OmniLight3D Light { get; } = light;

        public float Energy { get; set; }
    }
}
