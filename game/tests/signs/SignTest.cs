using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Signs with words, host plus a client, in the vanilla world. The signs the world placed carry their POI marker's words,
/// with the way to the nearest Workshop filled in, and both peers draw them inside the sign's face. The client puts down a
/// Signboard and writes on it: the host takes the words, both peers draw them, and words that do not fit are refused.
/// </summary>
public partial class SignTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private WorldPartSpot _spot = null!;
    private int _world;
    private int _board;
    private readonly List<ActionResult> _results = [];

    protected override string Tag => "signs";

    protected override int StageTimeoutFrames => 1800;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins", _ => _client.Main.LocalPlayer is { } player && player.IsOnFloor());
        Add("the world's signs carry their words", _ =>
        {
            var spots = WorldParts.SpotsOf(_host.Main.Streamer.Generator).Where(s => s.Settings?.ContainsKey("text") == true).ToList();
            Check(spots.Count >= 20, $"the world has signs with words ({spots.Count})");
            var spawn = _client.Main.LocalPlayer!.GlobalPosition.ToNumerics();
            _spot = spots.MinBy(s => System.Numerics.Vector3.Distance(s.Floor, spawn))!;
            _world = _host.Main.Creations.All.Single(c => c.WorldPart == _spot.Key).Id;
            GD.Print($"[signs] the nearest is {_spot.Part} at {_spot.Floor} ({_spot.Key}): {_spot.Settings!["text"].GetString()!.Replace('\n', '|')}");
            return true;
        });
        Add("both peers draw a world sign's words inside its face", frame =>
        {
            if (frame == 0)
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _spot.Floor.ToGodot() + new Vector3(0, 1, 4));
            if (frame < 120 || Label(_host, _world) is not { } hosted || Label(_client, _world) is not { } shown)
                return false;
            var words = _spot.Settings!["text"].GetString()!;
            Check(hosted.Text == words && shown.Text == words, $"both show \"{words.Replace('\n', '|')}\"");
            CheckFits(shown, _client.Main.Creations.Find(_world)!.DefinitionOf(_client.Main.Creations.Find(_world)!.Parts.Single().Id));
            return true;
        });
        Add("the client puts down a Signboard", frame =>
        {
            if (frame == 0)
            {
                var at = _client.Main.LocalPlayer!.GlobalPosition + new Vector3(3, 0.5f, 0);
                Request(BuildActions.PlacePart, new PlacePartArgs("vanilla:signboard", Core.Creations.GridCell.Zero, Position: at.ToNumerics().ToVec3()));
                return false;
            }
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"placed ({_results[0]})");
            _board = _results[0].Value!.Value.Deserialize<PlacePartResult>(ActionJson.Options)!.Creation;
            return true;
        });
        Add("the client writes on it and both peers draw the words", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Configure("GONE FISHING\nback at dusk");
                return false;
            }
            if (_results.Count == 0 || Label(_host, _board)?.Text != "GONE FISHING\nback at dusk" || Label(_client, _board)?.Text != "GONE FISHING\nback at dusk")
                return _results.Count > 0 && !_results[0].Ok && Fail($"refused: {_results[0]}");
            Check(_results[0].Ok, "the host takes the words");
            var board = _client.Main.Creations.Find(_board)!;
            CheckFits(Label(_client, _board)!, board.DefinitionOf(board.Parts.Single().Id));
            return true;
        });
        Add("words that do not fit are refused", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Configure("1\n2\n3\n4\n5");
                Configure(new string('x', 81));
                return false;
            }
            if (_results.Count < 2)
                return false;
            Check(!_results[0].Ok && _results[0].Message!.Contains("at most 4 lines"), $"five lines ({_results[0]})");
            Check(!_results[1].Ok && _results[1].Message!.Contains("at most 80 characters"), $"81 characters ({_results[1]})");
            Check(Label(_client, _board)?.Text == "GONE FISHING\nback at dusk", "the words stay as they were");
            return true;
        });
    }

    private void Configure(string text) =>
        Request(LogicActions.ConfigurePart, new ConfigurePartArgs(_board, _client.Main.Creations.Find(_board)!.Parts.Single().Id,
            new Dictionary<string, JsonElement?> { ["text"] = JsonSerializer.SerializeToElement(text) }));

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, r => _results.Add(r));

    private bool Fail(string why)
    {
        Check(false, why);
        return true;
    }

    private static Label3D? Label(Instance peer, int creation) =>
        peer.Main.Creations.NodeOf(creation)?.Texts.Select(t => t.Label).FirstOrDefault();

    // The drawn words, at the label's scale, stay inside the face's area.
    private void CheckFits(Label3D label, Core.Data.PartDefinition definition)
    {
        var area = definition.Display!.Text!.Size!;
        var size = label.Font.GetMultilineStringSize(label.Text, HorizontalAlignment.Center, -1, label.FontSize) * label.PixelSize;
        Check(label.Visible && size.X <= area[0] + 1e-3f && size.Y <= area[1] + 1e-3f && size.Y > area[1] * 0.2f,
            $"the words fit the {area[0]:0.00} x {area[1]:0.00} m face ({size.X:0.00} x {size.Y:0.00} m)");
    }
}
