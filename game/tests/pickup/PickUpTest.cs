using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Right click picks up by default, host plus a client in Survival: with a carrot in hand (an item without a right
/// click of its own) holding right click on a block fills the ring and puts the block in the client's inventory on
/// both machines, and the HUD says so. A weapon with an <c>aim</c> section keeps right click to aim, the Paint Tool keeps
/// its own, and the Sledgehammer (no block in Survival) picks up. Switching away mid-hold picks nothing up.
/// </summary>
public partial class PickUpTest : StagedTest
{
    private const string Block = "vanilla:scrap_wood_block";
    private const string Carrot = "vanilla:carrot";
    private const string Blaster = "vanilla:potato_blaster";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Vector3 _at;
    private int _creation;
    private int _blocks;
    private bool _sawRing;

    protected override string Tag => "pickup";

    protected override int StageTimeoutFrames => 900;

    private int Peer => _client.Peer;

    private ToolBelt Tools => _client.Main.Tools!;

    private ItemTool Item => Tools.Item;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins with blocks, carrots and a Potato Blaster", _ =>
        {
            if (_client.Main.Inventory.Own is null || _client.Main.Tools is null || _client.Main.LocalPlayer is not { } player
                || !player.IsOnFloor() || !_host.Main.PlayerSync.Players.ContainsKey(Peer) || _client.Main.Survival.Own is null)
                return false;
            var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
            pockets.Add(Block, 3);
            pockets.Add(Carrot, 3);
            pockets.Add(Blaster, 1);
            return true;
        });
        Add("the client places a block ahead", PlaceBlock);
        Wait("it settles", 60);
        Add("a carrot in hand lends right click to picking up", frame =>
        {
            if (!InHand(Carrot, frame))
                return false;
            LookAt(BlockCentre);
            if (frame < 20)
                return false;
            Check(!Item.OwnsRightClick, "a carrot has no right click of its own");
            return Check(Item.HudLine().Contains("pick it up"), $"the HUD offers picking up ({Item.HudLine()})");
        });
        Add("holding right click with the carrot picks the block up", frame =>
        {
            if (frame == 0)
            {
                _blocks = Count(_client, Block);
                return Check(Item.PressPickUp(), "the press starts a pick-up") && false;
            }
            _sawRing |= Item.Ring is > 0f and < 1f;
            if (_host.Main.Creations.Find(_creation) is not null || Count(_client, Block) != _blocks + 1)
                return frame < 300 ? false : Check(false, $"the block comes off into the inventory ({Count(_client, Block)} of {_blocks + 1}, creation {_host.Main.Creations.Find(_creation) is not null}, phase {Tools.Builder.RemovePhase}, ring {Item.Ring}, answer {Tools.Builder.LastResult})");
            Check(_sawRing, "the ring filled on the way");
            Check(Count(_host, Block) == _blocks + 1, "the host's inventory has it too");
            Item.ReleasePickUp();
            return Check(Item.Ring is null, "the ring goes");
        });
        Add("a weapon keeps right click to aim", frame =>
        {
            if (!InHand(Blaster, frame))
                return false;
            Check(Item.OwnsRightClick && !Item.PressPickUp(), "the blaster's right click is its own");
            return Check(Item.HudLine().Contains("aim") && !Item.HudLine().Contains("pick it up"), $"its HUD offers aiming ({Item.HudLine()})");
        });
        Add("tools with a right click keep it, the Sledgehammer in Survival picks up", _ =>
        {
            Check(Tools.Paint.OwnsRightClick && Tools.Weld.OwnsRightClick && Tools.Lift.OwnsRightClick, "the Paint Tool, Weld Tool and Lift keep theirs");
            Check(!Tools.Sledge.OwnsRightClick, "the Sledgehammer does not block in Survival");
            Check(Tools.Sledge.HudText().Contains("pick it up"), $"so its HUD offers picking up ({Tools.Sledge.HudText()})");
            Tools.Builder.Clear();
            return Check(Tools.Builder.HudText().Contains("pick it up"), $"so does the empty hand ({Tools.Builder.HudText()})");
        });
        Add("the client places another block", PlaceBlock);
        Wait("it settles too", 60);
        Add("switching away mid-hold picks nothing up", frame =>
        {
            if (!InHand(Carrot, frame))
                return false;
            LookAt(BlockCentre);
            if (frame < 10)
                return false;
            if (frame == 10)
                return Check(Item.PressPickUp(), "the press starts a pick-up") && false;
            Tools.Use(Tools.Paint);
            Check(Tools.Builder.RemovePhase == GesturePhase.Idle, "the pick-up is dropped with the carrot");
            return true;
        });
        Wait("long enough for any ring to fill", 120);
        Add("the block stays", _ => Check(_host.Main.Creations.Find(_creation) is not null, "the block is still there"));
    }

    // Places a block 2 m ahead of the client on the ground; true once both machines have it.
    private bool PlaceBlock(int frame)
    {
        if (frame == 0)
        {
            _results.Clear();
            _creation = 0;
            _at = _client.Main.LocalPlayer!.GlobalPosition + new Vector3(0, 0.125f, -2);
            _client.Main.Actions.Request(BuildActions.PlacePart, new PlacePartArgs(Block, GridCell.Zero, Position: _at.ToNumerics().ToVec3()), _results.Add);
            return false;
        }
        if (_results.Count == 0)
            return false;
        if (_creation == 0)
        {
            Check(_results[0].Ok, $"placed ({_results[0].Message})");
            _creation = _results[0].Value!.Value.Deserialize<PlacePartResult>(ActionJson.Options)!.Creation;
        }
        return _client.Main.Creations.Find(_creation) is not null;
    }

    // Where the block is now on the client (it may have settled since it was placed).
    private Vector3 BlockCentre
    {
        get
        {
            if (_client.Main.Creations.NodeOf(_creation) is not { } node || _client.Main.Creations.Find(_creation) is not { } c || c.Parts.FirstOrDefault() is not { } p)
                return _at;
            return node.Bodies[0].GlobalTransform * PartGeometry.CentreMetres(c.DefinitionOf(p.Id), p.Cell, p.Rotation).ToGodot();
        }
    }

    // Puts item in hand; true once it is there.
    private bool InHand(string item, int frame)
    {
        if (Tools.Current == Item && Item.InHand?.Item == item)
            return true;
        Item.Choose(item);
        if (frame % 30 == 0)
            Tools.Use(Item);
        return false;
    }

    // What the client carries, as each machine sees it.
    private int Count(Instance instance, string item) => instance == _host
        ? _host.Main.Inventory.Items!.PlayerInventory(Peer).Count(item)
        : _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private void LookAt(Vector3 target)
    {
        var player = _client.Main.LocalPlayer!;
        var d = target - player.AimOrigin;
        player.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()) };
    }
}
