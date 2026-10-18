using Godot;
using Mechanica.Core.Settings;
using Mechanica.Game.Settings;
using Mechanica.Game.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Lakes and the sea, host plus client. Both peers draw the water of the chunks at a shore at sea level with the one
/// shared water material, on a mesh fine enough for the waves; the Water quality setting (Options, Graphics) switches
/// the material to that level's shader (ripple layers, waves, depth and foam) and the quality preset sets it; and each
/// peer tints its screen only while its own camera is under the surface.
/// </summary>
public partial class WaterTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Vector3 _shore;
    private Vector3 _sea;
    private UserSettings _settings = null!;

    protected override string Tag => "water";

    protected override int StageTimeoutFrames => 1200;

    public override void _Ready()
    {
        _settings = GameSettings.Current;
        _harness = new NetTestHarness { Name = "Harness", Seed = 20261018 };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Wait("players settle", 30);
        Add("both players go to the shore", frame =>
        {
            if (frame == 0)
            {
                var gen = _host.Main.Streamer.Generator;
                var dir = new Vector3(1, 0, 0);
                for (var t = 0f; t < 3000f; t += 2f)
                {
                    var p = dir * t;
                    if (gen.HeightAt(p.X, p.Z) > gen.SeaLevel + 0.2f)
                        _shore = new Vector3(p.X, gen.HeightAt(p.X, p.Z), p.Z);
                    else if (_shore != default && gen.HeightAt(p.X + 20f, p.Z) < gen.SeaLevel - 1f)
                    {
                        _sea = new Vector3(p.X + 20f, gen.SeaLevel, p.Z);
                        break;
                    }
                }
                Check(_shore != default && _sea != default, $"a shore is found at {_shore}, open water at {_sea}");
                _host.Main.PlayerSync.HostTeleport(_host.Peer, _shore + Vector3.Up * 0.5f);
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _shore + new Vector3(-2, 0.5f, 0));
                return false;
            }
            return frame > 90 && _client.Main.Streamer.PendingChunks == 0 && _host.Main.Streamer.PendingChunks == 0
                && _client.Main.Streamer.Streamer.IsLoaded(_client.Main.Streamer.ChunkAt(_sea));
        });
        Add("both peers draw the sea with the shared water material", _ =>
        {
            foreach (var instance in new[] { _host, _client })
            {
                var chunk = instance.Main.Streamer.Chunks[instance.Main.Streamer.ChunkAt(_sea)];
                var water = chunk.GetNodeOrNull<MeshInstance3D>("Water");
                Check(water is not null, $"{instance.Name}: the chunk at sea has water");
                Check(water!.MaterialOverride == WaterSurface.Material, $"{instance.Name}: drawn with the water material");
                Check(Mathf.IsEqualApprox(water.GlobalPosition.Y, instance.Main.Streamer.Generator.SeaLevel), $"{instance.Name}: at sea level ({water.GlobalPosition.Y})");
                var mesh = (PlaneMesh)water.Mesh;
                Check(mesh.SubdivideWidth >= 31 && mesh.SubdivideDepth >= 31, $"{instance.Name}: fine enough for waves ({mesh.SubdivideWidth} cuts)");
            }
            return true;
        });
        Add("Water quality switches the shader", _ =>
        {
            string Code(Quality preset, Quality water)
            {
                GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Preset = preset, Water = water } });
                _host.Main.Water._Process(0);
                return WaterSurface.Material.Shader.Code;
            }
            var low = Code(Quality.Custom, Quality.Low);
            Check(WaterSurface.Quality == Quality.Low, "Custom Low draws Low");
            Check(low.Contains("#define LAYERS 1") && low.Contains("#define WAVES 0") && low.Contains("#define FOAM 0") && low.Contains("#define DEPTH 0"),
                "Low: one ripple layer, no waves, foam or depth");
            Check((float)WaterSurface.Material.GetShaderParameter("wave_amplitude") == 0f, "Low: flat");
            var medium = Code(Quality.Custom, Quality.Medium);
            Check(medium.Contains("#define LAYERS 2") && medium.Contains("#define WAVES 1") && medium.Contains("#define FOAM 1") && medium.Contains("#define DEPTH 1"),
                "Medium: two layers, low waves and soft foam");
            var mediumWaves = (float)WaterSurface.Material.GetShaderParameter("wave_amplitude");
            var high = Code(Quality.Custom, Quality.High);
            Check(high.Contains("#define LAYERS 3"), "High: three layers");
            var highWaves = (float)WaterSurface.Material.GetShaderParameter("wave_amplitude");
            Check(mediumWaves > 0f && highWaves > mediumWaves, $"waves grow with quality ({mediumWaves} < {highWaves})");
            Code(Quality.Low, Quality.High);
            Check(WaterSurface.Quality == Quality.Low, "the Low preset sets Low water");
            Code(Quality.Ultra, Quality.Low);
            Check(WaterSurface.Quality == Quality.High, "the Ultra preset draws High water");
            return true;
        });
        Add("the client tints its screen only under the surface", _ =>
        {
            var camera = new Camera3D { Name = "WaterTestCamera" };
            _client.Main.AddChild(camera);
            camera.MakeCurrent();
            camera.GlobalPosition = _sea + Vector3.Down * 1.5f;
            _client.Main.Water._Process(0);
            _host.Main.Water._Process(0);
            Check(_client.Main.Water.Underwater, "under the surface the client's screen is tinted");
            Check(!_host.Main.Water.Underwater, "the host's camera is not under water, so its screen is not");
            camera.GlobalPosition = _sea + Vector3.Up * 2f;
            _client.Main.Water._Process(0);
            Check(!_client.Main.Water.Underwater, "above it the tint goes");
            camera.QueueFree();
            return true;
        });
        Add("done", _ =>
        {
            GameSettings.Apply(_settings);
            Finish(0);
            return true;
        });
    }
}
