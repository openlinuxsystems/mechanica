using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Paint Tool in Survival, host plus client, on a flat world (Chase's paint tool spec): the client builds a two-block
/// bar, the host refuses paint until the tool is crafted and then until there is Paint Ammo, a click costs one Paint Ammo
/// and a drag over both blocks costs one too, taking paint off is free, and the host draws the client's tool in the chosen
/// colour, spraying while the drag is held (a pale mist when taking paint off) and twisting its tank off through a reload.
/// </summary>
public partial class PaintTest : StagedTest
{
    private const string Block = "vanilla:scrap_metal_block";
    private const string Tool = "vanilla:paint_tool";
    private const string Ammo = "vanilla:paint_ammo";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _bar;
    private int _sent;
    private bool _sawMist;
    private float _mostTwist;

    protected override string Tag => "paint";

    protected override int StageTimeoutFrames => 900;

    private int Peer => _client.Peer;

    private ToolBelt Tools => _client.Main.Tools!;

    private PaintTool Paint => Tools.Paint;

    private int HostAmmo => _host.Main.Inventory.Items!.PlayerInventory(Peer).Count(Ammo);

    // A flat dry world.
    private static WorldGenData Flat => WorldGenData.FromDefinition(new WorldGenDefinition
    {
        Terrain = new TerrainDefinition { CoastWidth = 0f, CoastBiomeWidth = 0f, SeaLevel = -100f, SeaFloor = -100f },
        Biomes = [new BiomeDefinition { Id = "flat", Name = "Flat", Weight = 1f, Color = "#5c7350" }],
    });

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins with two blocks and no Paint Tool", _ =>
        {
            if (_client.Main.Survival.Own is null || _client.Main.Inventory.Own is null || _client.Main.Tools is null
                || _client.Main.LocalPlayer is not { } player || !player.IsOnFloor() || !_host.Main.PlayerSync.Players.ContainsKey(Peer))
                return false;
            var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
            pockets.Remove(Tool, pockets.Count(Tool));
            pockets.Remove(Ammo, pockets.Count(Ammo));
            pockets.Add(Block, 2);
            return true;
        });
        Add("the client builds a bar of two blocks", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _sent = 0;
                var me = _client.Main.LocalPlayer!.GlobalPosition;
                var at = new Vector3(Mathf.Snapped(me.X, 0.25f), me.Y + 0.125f, Mathf.Snapped(me.Z, 0.25f) - 2.5f);
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, GridCell.Zero, Position: at.ToNumerics().ToVec3()));
            }
            if (_results.Count == 1 && _sent == 0)
            {
                _sent = 1;
                Check(_results[0].Ok, $"the first block goes down ({_results[0]})");
                _bar = Value(_results[0], "creation");
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, new GridCell(1, 0, 0), Creation: _bar));
            }
            if (_results.Count < 2 || _client.Main.Creations.Find(_bar) is not { Parts.Count: 2 })
                return false;
            Check(_results[1].Ok, $"the second block goes on ({_results[1]})");
            return true;
        });
        Wait("the bar settles", 30);
        Add("aim at the first block", frame => LookAt(PartCentre(_client, _bar, 1), frame));
        Add("with no Paint Tool the host refuses paint", frame =>
        {
            if (frame == 0)
            {
                Tools.Use(Paint);
                Check(!Paint.Owns && Paint.Prop == CharacterProp.None, "the tool is not owned and not in hand");
                Check(!Paint.BeginStroke(), "a drag does not start");
                _results.Clear();
                Check(Paint.Paint(_results.Add), "a click is still sent for the host to decide");
            }
            if (_results.Count == 0)
                return false;
            Check(!_results[0].Ok && _results[0].Message!.Contains("Fabricator", StringComparison.Ordinal), $"the host wants it crafted ({_results[0]})");
            return true;
        });
        Add("with the tool and no Paint Ammo the host still refuses", frame =>
        {
            if (frame == 0)
            {
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Tool, 1);
                _sent = 0;
            }
            if (!Paint.Owns)
                return false;
            if (_sent == 0)
            {
                _sent = 1;
                Check(Paint.Prop == CharacterProp.PaintTool, "the tool is in hand");
                Check(Paint.AmmoText() is { } text && text.Contains("Paint Ammo 0", StringComparison.Ordinal), $"the counter shows no ammo ({Paint.AmmoText()})");
                _results.Clear();
                Check(Paint.Paint(_results.Add), "a click is sent");
            }
            if (_results.Count == 0)
                return false;
            Check(!_results[0].Ok && _results[0].Message!.Contains("Paint Ammo", StringComparison.Ordinal), $"the host wants Paint Ammo ({_results[0]})");
            return true;
        });
        Add("a click paints a block for one Paint Ammo", frame =>
        {
            if (frame == 0)
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Ammo, 3);
            if (frame == 1)
            {
                Paint.Choose(1);
                _results.Clear();
                Paint.Paint(_results.Add);
            }
            if (frame < 1 || _results.Count == 0 || _client.Main.Creations.Find(_bar)!.FindPart(1)!.Color is null)
                return false;
            Check(_results[0].Ok, $"the host paints it ({_results[0]})");
            Check(_client.Main.Creations.Find(_bar)!.FindPart(1)!.Color == Palette[1], "in the chosen colour");
            Check(HostAmmo == 2, $"one Paint Ammo is spent ({HostAmmo} left)");
            return true;
        });
        Add("the host draws the client's tool in that colour", frame =>
        {
            if (_host.Main.PaintLook.LookOf(Peer) is not { } look || !look.Tint.IsEqualApprox(new Color(Palette[1])))
                return frame > 120 && Fail($"the host never tinted the tool ({_host.Main.PaintLook.LookOf(Peer)})");
            Check(!look.Spraying, "it is not spraying");
            return true;
        });
        Add("a drag over both blocks sprays and costs one Paint Ammo", frame =>
        {
            if (frame == 0)
            {
                Paint.Choose(25);
                _results.Clear();
                Check(Paint.BeginStroke(), "the drag starts");
            }
            if (frame is > 0 and < 20)
                LookAt(PartCentre(_client, _bar, 2), 0);
            _sawMist |= _host.Main.PaintLook.LookOf(Peer) is { Spraying: true, Erasing: false } look && look.Tint.IsEqualApprox(new Color(Palette[25]));
            if (frame == 30)
                Paint.EndStroke();
            var bar = _client.Main.Creations.Find(_bar)!;
            if (frame < 30 || bar.Parts.Any(p => p.Color != Palette[25]))
                return false;
            Check(_sawMist, "the host saw the tool spray in the colour");
            Check(HostAmmo == 1, $"the whole drag costs one Paint Ammo ({HostAmmo} left)");
            return true;
        });
        Add("a right drag takes the paint off for free with a pale mist", frame =>
        {
            if (frame == 0)
            {
                _sawMist = false;
                _results.Clear();
                Check(Paint.BeginStroke(erase: true), "the drag starts");
            }
            if (frame is > 0 and < 20)
                LookAt(PartCentre(_client, _bar, 1), 0);
            _sawMist |= _host.Main.PaintLook.LookOf(Peer) is { Spraying: true, Erasing: true };
            if (frame == 30)
                Paint.EndStroke();
            if (frame < 30 || _client.Main.Creations.Find(_bar)!.Parts.Any(p => p.Color is not null))
                return false;
            Check(_sawMist, "the host saw the pale mist");
            Check(HostAmmo == 1, $"no Paint Ammo is spent ({HostAmmo} left)");
            return true;
        });
        Add("R twists the tank off and back on, seen on the host", frame =>
        {
            if (frame == 0)
                Check(Paint.Reload(), "the reload starts");
            if (_host.Main.PaintLook.LookOf(Peer) is { } look)
                _mostTwist = Mathf.Min(_mostTwist, look.Twist);
            if (Paint.Reloading || frame < 20)
                return false;
            Check(_mostTwist < -60f, $"the tank turned off its collar ({_mostTwist:F0}°)");
            return true;
        });
        Add("the last Paint Ammo goes on one click, then the host refuses", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Paint.Paint(_results.Add);
            }
            if (_results.Count == 1 && _sent != 2)
            {
                _sent = 2;
                Check(_results[0].Ok && HostAmmo == 0, $"the last one is spent ({_results[0]}, {HostAmmo} left)");
                Paint.Paint(_results.Add);
            }
            if (_results.Count < 2)
                return false;
            Check(!_results[1].Ok, $"with none left the host refuses ({_results[1]})");
            return true;
        });
    }

    private IReadOnlyList<string> Palette => _host.Main.Build.Settings!.Paint.Palette;

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _results.Add);

    private static int Value(ActionResult result, string name) => result.Value!.Value.GetProperty(name).GetInt32();

    private static Vector3 PartCentre(Instance who, int creation, int partId)
    {
        var graph = who.Main.Creations.Find(creation)!;
        var part = graph.FindPart(partId)!;
        var body = who.Main.Creations.NodeOf(creation)!.Bodies[graph.BodyIndexOf(partId)];
        return body.GlobalTransform * PartGeometry.CentreMetres(graph.DefinitionOf(partId), part.Cell, part.Rotation).ToGodot();
    }

    private bool LookAt(Vector3 target, int frame)
    {
        var player = _client.Main.LocalPlayer!;
        var d = target - player.Camera.GlobalPosition;
        player.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()) };
        return frame >= 10;
    }

    private bool Fail(string message)
    {
        Check(false, message);
        return true;
    }
}
