using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Player;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Lift, Weld Tool, Paint Tool and Sledgehammer, host plus clients (chunk 15, D10, UI spec PR 11). A client builds
/// a three-block bar and a lone block, paints the bar (a click, then a drag over all three; the Q picker chooses the
/// colour by direction or number key), picks it up with its Lift, raises and turns it, saves it as a blueprint from the
/// Lift's menu, holds right click to take the Lift away (a short hold does nothing), loads the blueprint onto a new Lift
/// and deletes it after a confirmation, drag-welds the lone block onto the bar (outline, weld plane square, seam), smashes
/// a block off it with the Sledgehammer and blocks with it. The host's graphs, paint and Lifts must match on each client,
/// a late joiner gets the Lifts, and a player who leaves takes their Lift with them. Uses the building fixture mod in
/// <c>tests/building/mods</c>.
/// </summary>
public partial class ToolsTest : StagedTest
{
    private const string Block = "build_test:block";
    private const string Red = "#d03030";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private int _struck;
    private Vector3 _standing;
    private Instance _host = null!;
    private Instance _client = null!;
    private Instance _late = null!;
    private Vector3 _barAt;
    private int _bar;
    private int _lone;
    private float _ground;
    private bool _barRode;
    private int _sent;
    private BlueprintMenu _menu = null!;
    private string _blueprints = null!;
    private int _loaded;

    protected override string Tag => "tools";

    protected override int StageTimeoutFrames => 900;

    private ToolBelt Tools => _client.Main.Tools!;

    public override void _Ready()
    {
        _mods = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/building/mods"));
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);

        Add("the client joins with its tools", _ =>
        {
            if (_client.Main.PlayerSync.Players.Count != 2 || _client.Main.Tools is null || _host.Main.PlayerSync.Players.Count != 2)
                return false;
            Check(Tools.Tools.Select(t => t.ToolName).SequenceEqual(["Hammer", "Lift", "Weld Tool", "Paint Tool", "Sledgehammer", "Item", "Connect Tool"]),
                "the belt holds the Hammer, Lift, Weld Tool, Paint Tool, Sledgehammer, the item in hand and the Connect Tool");
            Check(Tools.Current == Tools.Hammer, "the Hammer starts in hand");
            Tools.Use(Tools.Lift);
            Check(Tools.Current == Tools.Lift && !Tools.Hammer.Active, "a tool can be put in hand");
            return true;
        });
        Add("the client builds a bar and a lone block", frame =>
        {
            if (frame == 0)
            {
                var me = _client.Main.LocalPlayer!.GlobalPosition;
                _ground = 0f;
                _barAt = new Vector3(Mathf.Snapped(me.X, 0.25f) - 0.25f, 0.125f, Mathf.Snapped(me.Z, 0.25f) - 2.5f);
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, GridCell.Zero, Position: _barAt.ToNumerics().ToVec3()));
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, GridCell.Zero, Position: (_barAt + new Vector3(1.75f, 0, 0)).ToNumerics().ToVec3()));
            }
            if (_results.Count < 2)
                return false;
            if (_results.Count == 2 && _sent == 0)
            {
                _sent = 2;
                Check(_results.All(r => r.Ok), $"both start ({string.Join("; ", _results)})");
                _bar = Value(_results[0], "creation");
                _lone = Value(_results[1], "creation");
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, new GridCell(1, 0, 0), Creation: _bar));
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, new GridCell(2, 0, 0), Creation: _bar));
            }
            if (_results.Count < 4 || !SameEverywhere())
                return false;
            Check(_results.All(r => r.Ok) && _host.Main.Creations.Find(_bar)!.Parts.Count == 3, "the bar has three blocks");
            return true;
        });
        Wait("the blocks settle", 20);
        AddPaint();
        AddLift();
        AddWeld();
        AddSledge();
        AddLateJoinAndLeave();
    }

    private void AddPaint()
    {
        Add("aim at the bar's middle", frame => LookAt(_client, PartCentre(_client, _bar, 2), frame));
        Add("the Paint Tool paints a block on every peer", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Use(Tools.Paint);
                while (Tools.Paint.Color != Red)
                    Tools.Paint.Cycle(1);
                Check(Tools.Paint.Paint(_results.Add), "the Paint Tool sends Paint");
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok, $"the host paints it ({_results[0]})");
            Check(_host.Main.Creations.Find(_bar)!.FindPart(2)!.Color == Red, "the host's part is red");
            var mesh = Body(_client, _bar).GetNode<Node3D>("Part2/Model").GetChild<MeshInstance3D>(0);
            var albedo = ((BaseMaterial3D)mesh.GetSurfaceOverrideMaterial(0)).AlbedoColor;
            Check(albedo.IsEqualApprox(new Color(Red)), $"the client shows it red ({albedo})");
            return true;
        });
        Add("the Paint Tool takes the paint off", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Paint.Erase(_results.Add);
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok && _client.Main.Creations.Find(_bar)!.FindPart(2)!.Color is null, $"the paint is gone ({_results[0]})");
            return true;
        });
        Add("the colour picker opens round the cursor and picks by direction", _ =>
        {
            var picker = Tools.Paint.Picker;
            picker.Open(Tools.Paint.ColorIndex);
            Check(picker.IsOpen, "Q opens the picker");
            var count = _client.Main.Build.Settings!.Paint.Palette.Count;
            var at = Core.Ui.RadialMenu.Position(4, count, picker.Radius);
            Check(picker.PointAt(picker.Centre + new Vector2(at.X, at.Y)) == 4, $"pointing at the fifth swatch highlights it ({picker.Hovered})");
            picker.Choose(picker.Hovered);
            Check(!picker.IsOpen && Tools.Paint.ColorIndex == 4, $"left click takes it and closes ({Tools.Paint.ColorIndex})");
            picker.Open(Tools.Paint.ColorIndex);
            picker._Input(Key(Core.Input.InputActions.Hotbar(3)));
            Check(!picker.IsOpen && Tools.Paint.ColorIndex == 2, $"the third number key takes the third colour ({Tools.Paint.ColorIndex})");
            picker.Open(Tools.Paint.ColorIndex);
            picker._Input(Key(Core.Input.InputActions.Pause));
            Check(!picker.IsOpen && Tools.Paint.ColorIndex == 2, "Esc closes it with no change");
            picker.Open(Tools.Paint.ColorIndex);
            picker._Input(Key(Core.Input.InputActions.Rotate));
            Check(!picker.IsOpen, "and so does a second Q");
            while (Tools.Paint.Color != Red)
                Tools.Paint.Cycle(1);
            return true;
        });
        Add("aim at the bar's first block", frame => LookAt(_client, PartCentre(_client, _bar, 1), frame));
        Add("a drag starts on it", _ =>
        {
            _results.Clear();
            Tools.Paint.BeginStroke();
            Check(Tools.Paint.Stroke is { Count: 1 }, $"the first block is in the drag ({Tools.Paint.Stroke?.Count})");
            return true;
        });
        Add("the drag passes over the second block", frame => LookAt(_client, PartCentre(_client, _bar, 2), frame));
        Add("and the third", frame => LookAt(_client, PartCentre(_client, _bar, 3), frame));
        Add("letting go paints all three on every peer", frame =>
        {
            if (frame == 0)
            {
                Check(Tools.Paint.Stroke is { Count: 3 }, $"the drag covered three blocks ({Tools.Paint.Stroke?.Count})");
                Tools.Paint.EndStroke();
                Check(Tools.Paint.Stroke is null, "letting go ends the drag");
            }
            var bar = _host.Main.Creations.Find(_bar)!;
            if (bar.Parts.Any(p => p.Color != Red) || !SameEverywhere())
                return false;
            Check(_client.Main.Creations.Find(_bar)!.Parts.All(p => p.Color == Red), "the client shows all three red");
            return true;
        });
    }

    private void AddLift()
    {
        Add("the Lift picks the bar up", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Use(Tools.Lift);
                Tools.Lift.UpdateAim();
                Check(Tools.Lift.Aim is { Creation: { } c } && c == _bar, $"the Lift aims at the bar ({Tools.Lift.Aim})");
                Check(Tools.Lift.Place(_results.Add), "the Lift sends PlaceLift");
            }
            if (_results.Count == 0 || !SameEverywhere() || !_client.Main.Build.Lifts.ContainsKey(_client.Peer))
                return false;
            Check(_results[0].Ok, $"the host lifts it ({_results[0]})");
            var bar = _host.Main.Creations.Find(_bar)!;
            Check(bar.OnLift && Body(_host, _bar).Freeze, "the host holds the bar still");
            Check(Mathf.IsEqualApprox(Bottom(_host, _bar), _ground + _host.Main.Build.Settings!.Lift.TopAt(0), 1e-3f), $"it rests on the platform (bottom {Bottom(_host, _bar):F3})");
            Check(_client.Main.Creations.Find(_bar)!.OnLift, "the client knows it is lifted");
            Check(Mathf.IsEqualApprox(Bottom(_client, _bar), Bottom(_host, _bar), 1e-3f), "and shows it at the same height");
            var lift = _client.Main.Build.Lifts[_client.Peer];
            Check(lift.State.Creation == _bar && lift.State.Level == 0, $"the client shows its Lift holding the bar ({lift.State})");
            return true;
        });
        Add("the Lift raises the bar two levels", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Check(Tools.Lift.Raise(2, _results.Add), "the Lift sends SetLiftLevel");
            }
            var raised = _host.Main.Build.Settings!.Lift.TopAt(2);
            if (_results.Count == 0 || _client.Main.Build.Lifts[_client.Peer].State.Level != 2
                || !Mathf.IsEqualApprox(Bottom(_client, _bar), _ground + raised, 1e-3f))
                return false;
            Check(_results[0].Ok, $"the host raises it ({_results[0]})");
            Check(Mathf.IsEqualApprox(Bottom(_host, _bar), _ground + raised, 1e-3f), $"the bar is 0.5 m higher ({Bottom(_host, _bar):F3})");
            // Host and client ease the column up the same way and settle at the new height.
            var hostLift = _host.Main.Build.Lifts[_client.Peer];
            var clientLift = _client.Main.Build.Lifts[_client.Peer];
            if (clientLift.Moving && _client.Main.Creations.NodeOf(_bar)!.VisualOffset.Y < -1e-3f)
                _barRode = true;
            if (hostLift.Moving || clientLift.Moving)
                return false;
            Check(_barRode, "while the column rises the bar is drawn riding the deck, below where the host put it");
            Check(Mathf.IsEqualApprox(hostLift.ShownTop, raised, 1e-3f) && Mathf.IsEqualApprox(clientLift.ShownTop, raised, 1e-3f),
                $"both show the deck at the new height ({hostLift.ShownTop:F3}, {clientLift.ShownTop:F3})");
            var top = hostLift.GetNode<CollisionShape3D>("Shape");
            Check(Mathf.IsEqualApprox(top.Position.Y + ((BoxShape3D)top.Shape).Size.Y / 2, raised, 1e-3f), "the host's Lift platform rose with it");
            Check(_client.Main.Creations.NodeOf(_bar)!.VisualOffset == Vector3.Zero, "the bar is drawn where it is once the Lift stops");
            return true;
        });
        Add("the Lift turns the bar", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Check(Tools.Lift.Turn(_results.Add), "the Lift sends TurnLift");
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok, $"the host turns it ({_results[0]})");
            var box = Box(_host, _bar);
            Check(Mathf.IsEqualApprox(box.Max.Z - box.Min.Z, 0.75f, 1e-3f), $"the bar now runs along z ({box.Min} – {box.Max})");
            return true;
        });
        Add("look at the bar on the Lift", frame => LookAt(_client, PartCentre(_client, _bar, 2), frame));
        Add("Use on the Lift opens the blueprint menu and saves the bar", _ =>
        {
            Check(Tools.Lift.AimsAtOwnLift() && Tools.Lift.Interaction is not null, "Use (E) on the Lift offers the blueprints");
            _blueprints = Path.Combine(OS.GetUserDataDir(), "test-blueprints-" + Guid.NewGuid().ToString("N"));
            _menu = new BlueprintMenu
            {
                Name = "BlueprintMenu", Lift = () => Tools.Lift,
                Store = new Core.Saves.BlueprintStore(_blueprints, _client.Main.Build.Settings!.Lift.MaxBlueprints),
            };
            AddChild(_menu);
            Tools.Lift.OpenBlueprints = _menu.Open;
            Tools.Lift.Interaction!.Value.Run();
            Check(_menu.IsOpen && _menu.Listed.Count == 0, "the menu opens, empty");
            Check(_menu.Save("Test bar") is null, "saving a new name asks nothing");
            Check(_menu.Listed is [{ Name: "Test bar", Parts: 3, Error: null }], $"the bar is listed ({string.Join(", ", _menu.Listed)})");
            var dialog = _menu.Save("test bar");
            Check(dialog is not null && Ui.ConfirmDialog.Current == dialog, "saving over it asks first");
            dialog!.Confirm();
            Check(_menu.Listed.Count == 1, "and replaces it");
            // A picture beside it (drawn offscreen in the real game) shows in the row and loads fast.
            var image = Image.CreateEmpty(64, 36, false, Image.Format.Rgba8);
            image.Fill(Ui.UiTokens.Blue);
            image.SavePng(Core.Saves.BlueprintStore.ThumbnailFor(_menu.Listed[0].Path));
            _menu.Refresh();
            Check(_menu.Listed[0].Thumbnail is not null && FindChild<TextureRect>(_menu, "Thumbnail") is not null, "the row shows the picture");
            Check(_menu.LastRefreshSeconds < 0.5, $"the list and pictures load in under 0.5 s ({_menu.LastRefreshSeconds:F3} s)");
            _menu.Close();
            return true;
        });
        Add("a short right-click hold leaves the Lift", frame =>
        {
            if (frame == 0)
            {
                Check(Tools.Lift.PressRemove(), "right click starts the hold");
                return false;
            }
            if (frame < 5)
                return false;
            Check(Tools.Lift.Progress is > 0f and < 1f, $"the ring fills ({Tools.Lift.Progress})");
            Tools.Lift.ReleaseRemove();
            Check(Tools.Lift.Progress is null && _client.Main.Build.Lifts.ContainsKey(_client.Peer), "letting go early keeps the Lift");
            return true;
        });
        Add("holding right click takes the Lift away and drops the bar", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Check(Tools.Lift.PressRemove(), "right click starts the hold");
            }
            if (_client.Main.Build.Lifts.Count != 0 || frame < 60)
                return false;
            Check(!Tools.Lift.Removing, "the hold ended");
            Check(!_host.Main.Creations.Find(_bar)!.OnLift && !Body(_host, _bar).Freeze, "the host simulates the bar again");
            Check(Bottom(_host, _bar) < _ground + 0.1f, $"it falls to the ground (bottom {Bottom(_host, _bar):F3})");
            return true;
        });
        Add("the blueprint loads onto a new Lift on every peer", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(ToolActions.PlaceLift, new PlaceLiftArgs(new Vec3(_barAt.X + 0.25f, _ground, _barAt.Z - 2.5f)));
                return false;
            }
            if (frame == 1)
                return false;
            if (_results.Count == 1 && _client.Main.Build.Lifts.ContainsKey(_client.Peer) && _sent != -1)
            {
                Check(_results[0].Ok, $"a new Lift goes down ({_results[0]})");
                _sent = -1;
                _menu.Open();
                _menu.Select(0);
                Check(_menu.Load(_results.Add), "Load sends the blueprint");
            }
            if (_results.Count < 2 || _client.Main.Build.Lifts[_client.Peer].State.Creation is not { } id || !SameEverywhere())
                return false;
            Check(_results[1].Ok, $"the host builds it ({_results[1]})");
            _loaded = id;
            var loaded = _host.Main.Creations.Find(id)!;
            Check(loaded.OnLift && loaded.Parts.Count == 3 && loaded.Parts.All(p => p.Color == Red), "a red three-block bar stands on the Lift");
            Check(_client.Main.Creations.Find(id)?.OnLift == true, "the client shows it on the Lift");
            Check(!_menu.IsOpen, "the menu closes once it loads");
            return true;
        });
        Add("a blueprint is deleted only after the confirmation", _ =>
        {
            _menu.Open();
            _menu.Select(0);
            var path = _menu.Listed[0].Path;
            var dialog = _menu.Delete();
            Check(dialog is not null && File.Exists(path), "Delete asks first and keeps the file meanwhile");
            dialog!.Confirm();
            Check(_menu.Listed.Count == 0 && !File.Exists(path) && !File.Exists(Core.Saves.BlueprintStore.ThumbnailFor(path)), "confirming deletes it and its picture");
            _menu.Close();
            return true;
        });
        Add("the loaded copy goes away with its Lift", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(ToolActions.RemoveLift, new NoArgs());
                _host.Main.Build.Removed(_loaded);
            }
            if (_results.Count == 0 || _client.Main.Build.Lifts.Count != 0 || _client.Main.Creations.Find(_loaded) is not null || !SameEverywhere())
                return false;
            Directory.Delete(_blueprints, recursive: true);
            return true;
        });
    }

    private void AddWeld()
    {
        Add("aim at the lone block", frame => LookAt(_client, PartCentre(_client, _lone, 1), frame));
        Add("a click selects it with a blue outline", frame =>
        {
            if (frame == 0)
            {
                Tools.Use(Tools.Weld);
                Tools.Weld.Press();
                Check(Tools.Weld.Selected is { } s && s.Creation == _lone && Tools.Weld.Dragging, $"pressing selects the lone block and starts a drag ({Tools.Weld.Selected})");
                Check(!Tools.Weld.Release(), "letting go on the same part welds nothing");
                return false;
            }
            Check(Tools.Weld.Selected is { } kept && kept.Creation == _lone && !Tools.Weld.Dragging, "the selection stays for a second click");
            Check(Tools.Weld.Outline.Visible && Tools.Weld.Outline.GetChildCount() > 0, "the selected block has an outline");
            Tools.Weld.Deselect();
            Tools.Weld.UpdateAim();
            Check(!Tools.Weld.Outline.Visible, "right click drops it and its outline");
            Tools.Weld.Press();
            Check(Tools.Weld.Dragging, "pressing again starts a drag from it");
            return true;
        });
        Add("drag to the top of the bar", frame => LookAt(_client, PartCentre(_client, _bar, 1) + new Vector3(0, 0.125f, 0), frame));
        Add("letting go there welds the block on top", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Weld.UpdateAim();
                Check(Tools.Weld.Aim is { } aim && aim.Creation.Id == _bar, $"it aims at the bar ({Tools.Weld.Aim})");
                Check(Tools.Weld.Plane.Visible && Tools.Weld.Joining is { Faces.Count: > 0 }, "a blue square marks the weld plane");
                Check(Tools.Weld.Release(_results.Add), "letting go sends Weld");
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok, $"the host welds it ({_results[0]})");
            var bar = _host.Main.Creations.Find(_bar)!;
            Check(_host.Main.Creations.Find(_lone) is null && _host.Main.Creations.Count == 1, "the lone block's creation is gone");
            Check(bar.Parts.Count == 4 && bar.Bodies.Count == 1, $"the bar has four blocks in one body ({bar.Parts.Count}, {bar.Bodies.Count})");
            var welded = bar.Parts.Last();
            Check(bar.PartAt(welded.Cell - new GridCell(0, 1, 0)) is not null, $"the block sits on top of the bar ({welded.Cell})");
            Check(Tools.Weld.Selected is null, "the selection clears");
            Check(Tools.Weld.LastSeam is { Faces.Count: > 0 } && Tools.Weld.GetChildren().OfType<WeldSeamGlow>().Any(), "a seam glows where they joined");
            return true;
        });
        Wait("the bar settles", 30);
    }

    private void AddSledge()
    {
        // The Sledgehammer reaches 3 m (sledge.reach), less than the building tools: step up to the bar first.
        Add("step up to the bar", frame =>
        {
            var at = _client.Main.LocalPlayer!.GlobalPosition;
            if (frame == 0)
            {
                _standing = at;
                _host.Main.PlayerSync.HostTeleport(_client.Peer, new Vector3(at.X, at.Y, 2.4f));
            }
            return frame >= 20 && Mathf.Abs(at.Z - 2.4f) < 0.1f;
        });
        Add("aim at the welded block", frame =>
        {
            _struck = _client.Main.Creations.Find(_bar)!.Parts.Last().Id;
            return LookAt(_client, PartCentre(_client, _bar, _struck) + new Vector3(0, 0.1f, 0), frame);
        });
        Add("three Sledgehammer hits break it off", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _sent = 0;
                Tools.Use(Tools.Sledge);
            }
            // Block durability 3 at 10 health each, 10 per hit; the tool waits out each swing. Each hit shoves the bar, so
            // keep looking at the block until the strike lands on it.
            if (_results.Count < 3 && _client.Main.Creations.Find(_bar)?.FindPart(_struck) is not null)
                LookAt(_client, PartCentre(_client, _bar, _struck) + new Vector3(0, 0.1f, 0), frame);
            if (_results.Count < 3 && _results.Count == _sent)
            {
                if (Tools.Sledge.Swing(_results.Add))
                    _sent++;
            }
            if (_results.Count < 3 || !SameEverywhere())
                return false;
            Check(_results.All(r => r.Ok), $"every hit lands ({string.Join("; ", _results)})");
            Check(!_results[0].Value!.Value.GetProperty("broken").GetBoolean()
                && Mathf.IsEqualApprox(_results[1].Value!.Value.GetProperty("health").GetSingle(), 10f), "the first hits only damage it");
            Check(_results[2].Value!.Value.GetProperty("broken").GetBoolean(), "the third breaks it off");
            Check(_host.Main.Creations.Find(_bar)!.Parts.Count == 3, "the bar is back to three blocks");
            return true;
        });
        Add("look up at the sky", frame => Look(_client, 0f, 1.3f, frame) && !Tools.Sledge.Swinging);
        Add("a swing is one a second and lands after the wind-up", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Check(Tools.Sledge.Swing(_results.Add), "a fresh swing starts");
                Check(!Tools.Sledge.Swing(), "a second click during the swing does nothing");
                Check(_results.Count == 0, "nothing is hit at the click");
                return false;
            }
            // Aimed at the sky, nothing is in reach: the swing lands on air.
            if (_results.Count == 0 || Tools.Sledge.Swinging)
                return false;
            Check(!_results[0].Ok && _results[0].Code == "miss", $"a swing at nothing misses ({_results[0]})");
            return true;
        });
        Add("holding right click blocks on the host", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Sledge.Block(true, _results.Add);
                return false;
            }
            // The raised hammer reaches the host with the next input packet, which can trail the action's answer.
            if (_results.Count == 0 || (!_host.Main.PlayerSync.Players[_client.Peer].Body.HasFlag(Core.Net.BodyFlags.Guard) && frame < 60))
                return false;
            Check(_results[0].Ok && _host.Main.Build.Tools!.Blocking.Contains(_client.Peer), "the host knows the player blocks");
            Check(_host.Main.PlayerSync.Players[_client.Peer].Body.HasFlag(Core.Net.BodyFlags.Guard), "the host sees the hammer raised to block");
            Check(_host.Main.Build.Tools!.DamageScale(_client.Peer, "sledge") < 1f, "a hit would do less");
            _results.Clear();
            Tools.Use(Tools.Hammer);
            Check(!Tools.Sledge.Blocking, "putting the Sledgehammer away stops the block");
            return true;
        });
        Add("and the host hears the block end", _ => !_host.Main.Build.Tools!.Blocking.Contains(_client.Peer));
        Add("step back", frame =>
        {
            if (frame == 0)
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _standing);
            return frame >= 20 && _client.Main.LocalPlayer!.GlobalPosition.DistanceTo(_standing) < 0.1f;
        });
    }

    private void AddLateJoinAndLeave()
    {
        Add("the client lifts the bar again", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Use(Tools.Lift);
                Request(ToolActions.PlaceLift, new PlaceLiftArgs(new Vec3(_barAt.X + 0.25f, _ground, _barAt.Z), _bar));
            }
            if (_results.Count == 0 || !_client.Main.Build.Lifts.ContainsKey(_client.Peer))
                return false;
            Check(_results[0].Ok, $"the bar is lifted ({_results[0]})");
            return true;
        });
        Add("a late joiner gets the Lift and the creations", frame =>
        {
            if (frame == 0)
                _late = _harness.StartClient("Late", mods: _mods);
            if (frame < 20 || _late.Main.Creations.Count < 1 || !_late.Main.Build.Lifts.ContainsKey(_client.Peer))
                return false;
            Check(SameEverywhere(), "its creations match the host's");
            Check(_late.Main.Build.Lifts[_client.Peer].State == _host.Main.Build.Tools!.LiftOf(_client.Peer), "its Lift matches the host's");
            return true;
        });
        Add("a player who leaves takes their Lift along", frame =>
        {
            if (frame == 0)
                _harness.Stop(_client);
            if (_host.Main.Build.Lifts.Count != 0 || _late.Main.Build.Lifts.Count != 0 || _late.Main.Creations.Find(_bar)?.OnLift != false)
                return false;
            Check(_host.Main.Build.Tools!.Lifts.Count == 0, "the host forgets the Lift");
            Check(!_host.Main.Creations.Find(_bar)!.OnLift && !_late.Main.Creations.Find(_bar)!.OnLift, "the bar drops");
            return true;
        });
    }

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _results.Add);

    private static InputEvent Key(string action)
    {
        var e = Player.GameInput.ToEvent(Player.GameInput.Keymap.Get(action)!.Value)!;
        if (e is InputEventKey key)
            key.Pressed = true;
        return e;
    }

    private static T? FindChild<T>(Node root, string name) where T : Node =>
        root.FindChild(name, recursive: true, owned: false) as T;

    private static int Value(ActionResult result, string name) => result.Value!.Value.GetProperty(name).GetInt32();

    private static CreationBody Body(Instance who, int creation) => who.Main.Creations.NodeOf(creation)!.Bodies[0];

    private static WorldBounds Box(Instance who, int creation)
    {
        var node = who.Main.Creations.NodeOf(creation)!;
        return CreationBounds.World(node.LocalBounds, [.. node.Bodies.Select(b => b.GlobalTransform.ToPose())]);
    }

    private static float Bottom(Instance who, int creation) => Box(who, creation).Min.Y;

    private static Vector3 PartCentre(Instance who, int creation, int partId)
    {
        var graph = who.Main.Creations.Find(creation)!;
        var part = graph.FindPart(partId)!;
        var body = who.Main.Creations.NodeOf(creation)!.Bodies[graph.BodyIndexOf(partId)];
        return body.GlobalTransform * PartGeometry.CentreMetres(graph.DefinitionOf(partId), part.Cell, part.Rotation).ToGodot();
    }

    /// <summary>True once every client shows exactly the host's graphs (paint and Lift state included).</summary>
    private bool SameEverywhere()
    {
        foreach (var client in _harness.Instances.Skip(1))
        {
            var mine = client.Main.Creations;
            if (mine.Count != _host.Main.Creations.Count)
                return false;
            foreach (var creation in _host.Main.Creations.All)
            {
                if (mine.Find(creation.Id) is not { } copy
                    || copy.OnLift != creation.OnLift
                    || !copy.Parts.SequenceEqual(creation.Parts)
                    || !copy.Connections.SequenceEqual(creation.Connections))
                    return false;
            }
        }
        return true;
    }

    private static bool Look(Instance who, float yaw, float pitch, int frame)
    {
        who.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = yaw, Pitch = pitch };
        return frame >= 10;
    }

    private static bool LookAt(Instance who, Vector3 target, int frame)
    {
        var eye = who.Main.LocalPlayer!.Camera.GlobalPosition;
        var d = target - eye;
        return Look(who, Mathf.Atan2(-d.X, -d.Z), Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), frame);
    }
}
