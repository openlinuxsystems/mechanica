using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.World;
using Mechanica.Game.Bots;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The vanilla Scuttlers' swimming and effects, host plus client, on a flat world under 1.5 m of water. The Blue
/// Scuttler (a swimmer) floats up to its swim depth and swims, while the Green one walks along the bottom; the Yellow
/// one's stomp sends a shockwave and the Red one's blast an explosion, each seen once on the client.
/// </summary>
public partial class BotModelsTest : StagedTest
{
    private const float Sea = 1.5f;

    private static readonly ActionCaller Driver = new(Creation.HostPeer, ActionSource.Bot);

    private readonly HashSet<string> _clips = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _blue;
    private int _green;
    private int _yellow;
    private int _red;

    protected override string Tag => "botmodels";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        var flat = WorldGenData.Flat.Definition;
        _harness = new NetTestHarness
        {
            Name = "Harness",
            WorldGen = WorldGenData.FromDefinition(flat with { Terrain = flat.Terrain with { SeaLevel = Sea } }),
        };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Add("the host spawns a Blue and a Green Scuttler in the water", _ =>
        {
            _blue = Spawn("vanilla:scuttler_blue", new Vector3(12, 0, 0));
            _green = Spawn("vanilla:scuttler_green", new Vector3(12, 0, 5));
            return true;
        });
        Add("the Blue Scuttler floats up to its swim depth and the Green one stays on the bottom", frame =>
        {
            if (Body(_client, _blue) is not { } blue || Body(_client, _green) is not { } green || frame < 60)
                return false;
            var type = blue.Type;
            var depth = Sea - type.SwimDepth!.Value - type.Height / 2;
            if (Mathf.Abs(Body(_host, _blue)!.GlobalPosition.Y - depth) > 0.1f)
                return false;
            Check(blue.View is { Swimming: true }, "the client sees the Blue Scuttler swimming");
            Check(Mathf.Abs(blue.GlobalPosition.Y - depth) < 0.15f, $"it floats at its swim depth on the client ({blue.GlobalPosition.Y:0.00} m, {depth:0.00} m)");
            Check(green.View is { Swimming: false }, "the Green Scuttler is not a swimmer");
            Check(green.GlobalPosition.Y < 0.15f, $"it walks on the bottom ({green.GlobalPosition.Y:0.00} m)");
            Check(green.Model!.Legs == 6 && blue.Model!.Legs == 6, "both show their models");
            return true;
        });
        Add("the swimming Blue Scuttler plays its swim clips", frame =>
        {
            if (frame == 0)
                Check(Act(BotActions.MoveBot, new MoveBotArgs(_blue, new Vec3(0, 0, 1), 3f)).Ok, "the host swims it along");
            if (Body(_client, _blue)?.Model?.Clip is { } clip)
                _clips.Add(clip);
            if (!_clips.Contains("swim") && frame < 240)
                return false;
            Check(_clips.Contains("swim"), $"the client plays swim ({string.Join(", ", _clips)})");
            Check(!_clips.Contains("patrol") && !_clips.Contains("chase") && !_clips.Contains("fall"),
                $"and never walks or falls while swimming ({string.Join(", ", _clips)})");
            Act(BotActions.MoveBot, new MoveBotArgs(_blue));
            return true;
        });
        Add("the Yellow Scuttler's stomp sends a shockwave", frame =>
        {
            if (frame == 0)
                _yellow = Spawn("vanilla:scuttler_yellow", new Vector3(-12, 0, 0));
            if (frame == 30)
                Check(Act(BotActions.BotAttack, new BotAttackArgs(_yellow, "stomp")).Ok, "the host has it stomp");
            if (Body(_client, _yellow)?.Model is not { Effects.Count: > 0 } model)
                return false;
            Check(model.Effects.SequenceEqual([BotEffects.Shockwave]), $"the client shows one shockwave ({string.Join(", ", model.Effects)})");
            var effect = Effects(_client).SingleOrDefault();
            Check(effect is { Kind: BotEffects.Shockwave }, "the shockwave plays in the client's world");
            var stomp = Body(_client, _yellow)!.Type.Attack("stomp")!;
            Check(effect is not null && Mathf.IsEqualApprox(effect.Radius, stomp.Radius), $"as wide as the stomp ({effect?.Radius} m, {stomp.Radius} m)");
            return true;
        });
        Add("the shockwave plays out and goes", _ => Effects(_client).Count == 0);
        Add("the Red Scuttler blows itself up in one explosion", frame =>
        {
            if (frame == 0)
                _red = Spawn("vanilla:scuttler_red", new Vector3(-12, 0, 8));
            if (frame == 30)
                Check(Act(BotActions.BotAttack, new BotAttackArgs(_red, "explode")).Ok, "the host sets it off");
            if (Body(_client, _red) is not { View.Dead: true } red || frame < 60)
                return false;
            Check(red.Model!.Effects.SequenceEqual([BotEffects.Explosion]), $"the client shows one explosion ({string.Join(", ", red.Model.Effects)})");
            Check(Effects(_client).Count(e => e.Kind == BotEffects.Explosion) == 1, "it plays in the client's world");
            Check(Effects(_host).Count(e => e.Kind == BotEffects.Explosion) == 1, "and in the host's");
            Check(red.Model.Clip == "death", "the Red Scuttler bursts apart");
            return true;
        });
        Add("the explosion burns out and goes", _ => Effects(_client).Count == 0 && Effects(_host).Count == 0);
    }

    private int Spawn(string type, Vector3 at)
    {
        var result = Act(BotActions.SpawnBot, new SpawnBotArgs(type, [at.X, at.Y, at.Z]));
        Check(result.Ok, $"{type} spawns ({result})");
        return result.Ok ? Value<SpawnBotResult>(result).Bot : -1;
    }

    private ActionResult Act<TArgs>(string action, TArgs args) => _host.Main.Actions.Dispatcher.Dispatch(Driver, action, args);

    private static T Value<T>(ActionResult result) => result.Value!.Value.Deserialize<T>(ActionJson.Options)!;

    private static BotBody? Body(Instance instance, int id) => instance.Main.Bots.Bodies.GetValueOrDefault(id);

    private static List<BotEffect> Effects(Instance instance) => [.. instance.Main.Bots.GetChildren().OfType<BotEffect>()];
}
