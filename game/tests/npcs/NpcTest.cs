using Godot;
using Mechanica.Game.Player;
using Mechanica.Game.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Non-player characters, host plus a client, on the vanilla world. The farmer stands in his stall at the crash site's
/// <c>npc</c> marker on both peers: drawn as his own character model, facing out over the counter, with a capsule to
/// bump into. He is never on the character picker.
/// </summary>
public partial class NpcTest : StagedTest
{
    private const string Farmer = "vanilla:farmer";

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Vector3 _marker;
    private float _yaw;

    protected override string Tag => "npcs";

    protected override int StageTimeoutFrames => 1200;

    private IEnumerable<WorldStreamer> Worlds => [_host.Main.Streamer, _client.Main.Streamer];

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Add("the farmer is a character no player plays", _ =>
        {
            foreach (var main in new[] { _host.Main, _client.Main })
            {
                var book = main.Characters.Book;
                Check(book.FindAny(Farmer) is { Npc: true }, "the book knows the farmer as an npc");
                Check(book.Find(Farmer) is null && book.All.All(c => c.QualifiedId != Farmer), "he is not on the picker");
            }
            var marker = _host.Main.Streamer.Generator.MarkersOfType("npc").Single(m => m.Poi.Poi.Prefab.Id == "crash_site");
            Check(marker.Marker.Ref == "farmer", $"the crash site's npc marker names him ({marker.Marker.Ref})");
            _marker = marker.Position.ToGodot();
            _yaw = marker.Yaw;
            return true;
        });
        Add("the client's player walks up to the stall", frame =>
        {
            if (frame == 0)
            {
                var stand = _marker + new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw)) * 2.5f + Vector3.Up;
                _host.Main.PlayerSync.HostTeleport(_client.Peer, stand, 0f);
                return false;
            }
            return frame > 30 && Worlds.All(w => Figure(w) is not null);
        });
        Add("both peers draw him at his marker, facing out over the counter", _ =>
        {
            foreach (var world in Worlds)
            {
                var who = world == _host.Main.Streamer ? "host" : "client";
                var figure = Figure(world)!;
                Check(figure.GlobalPosition.DistanceTo(_marker) < 0.05f, $"{who}: he stands on the marker ({figure.GlobalPosition} vs {_marker})");
                var model = figure.GetNodeOrNull<CharacterModel>("Turn/Character");
                Check(model is { Player: null } && model.Character.QualifiedId == Farmer, $"{who}: as the farmer's own model, with no player");
                // Characters face -Z, so his front is his model's -Z.
                var facing = -(model?.GlobalBasis.Z ?? Vector3.Zero);
                var outward = new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw));
                Check(facing.Dot(outward) > 0.95f, $"{who}: he faces out of the stall ({facing} vs {outward})");
                var chunk = world.Chunks[world.ChunkAt(_marker)];
                Check(chunk.Structures.GetChildren().OfType<CollisionShape3D>().Any(s => s.Shape is CapsuleShape3D && (chunk.Structures.ToGlobal(s.Position) - _marker).Length() < 1.2f),
                    $"{who}: he has a capsule to bump into");
            }
            return true;
        });
    }

    private Node3D? Figure(WorldStreamer world) =>
        world.Chunks.GetValueOrDefault(world.ChunkAt(_marker))?.Npcs?.GetNodeOrNull<Node3D>("farmer");
}
