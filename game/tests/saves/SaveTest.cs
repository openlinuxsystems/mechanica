using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Saves;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Saving and loading a world, host plus client (chunk 24, D10). A client places a chest, stores scrap in
/// it, keeps scrap of its own and walks away from its spawn; the host keeps wood. The client leaves (the
/// host remembers it), the host saves, and both instances stop. A new host loads the save: the chest is back
/// with its scrap and the host has its wood. The client joins again with the same profile and gets its scrap
/// and position back, and sees the chest through late-join sync. A second instance with the same profile is
/// refused. Uses the fixture mod in <c>tests/items/mods</c>.
/// </summary>
public partial class SaveTest : StagedTest
{
    private const string Scrap = "items_test:scrap";
    private const string Wood = "items_test:wood";
    private const string Chest = "items_test:chest";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private SaveStore _store = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Instance? _twin;
    private int _chest;
    private Vector3 _walkedTo;
    private SaveGame _save = null!;

    protected override string Tag => "saves";

    private InventoryRef ChestRef => InventoryRef.OfPart(_chest, 1);

    private InventoryView? ClientOwn => _client.Main.Inventory.Own;

    public override void _Ready()
    {
        _mods = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/items/mods"));
        var folder = ProjectSettings.GlobalizePath("user://test_saves");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        _store = new SaveStore(folder);
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        StartWorld();

        Add("the client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && ClientOwn is not null);
        Add("the client gets scrap and the host wood", frame =>
        {
            if (frame == 0)
            {
                Request(_client, InventoryActions.GiveItems, new GiveItemsArgs(Scrap, 7));
                Request(_host, InventoryActions.GiveItems, new GiveItemsArgs(Wood, 3));
            }
            return _results.Count == 2 && ClientOwn?.Count(Scrap) == 7 && _host.Main.Inventory.Own?.Count(Wood) == 3;
        });
        Add("the client places a chest", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var me = _client.Main.LocalPlayer!.GlobalPosition;
                var at = new Vector3(Mathf.Snapped(me.X, 0.25f), 0.25f, Mathf.Snapped(me.Z, 0.25f) - 3f);
                Request(_client, BuildActions.PlacePart, new PlacePartArgs(Chest, GridCell.Zero, Position: at.ToNumerics().ToVec3()));
            }
            if (_results.Count < 1)
                return false;
            Check(_results[0].Ok, $"the chest is placed ({_results[0]})");
            _chest = _results[0].Value!.Value.GetProperty("creation").GetInt32();
            return true;
        });
        Add("the client stores two scrap in the chest", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var slot = ClientOwn!.Items.First(i => i.Item == Scrap).Slot;
                Request(_client, InventoryActions.MoveItems, new MoveItemsArgs(InventoryRef.OfPlayer(_client.Peer), slot, ChestRef, 3, 2));
            }
            return _results.Count == 1 && ClientOwn?.Count(Scrap) == 5;
        });
        Add("the client walks away from its spawn", frame =>
        {
            if (frame == 0)
            {
                var spawn = _host.Main.PlayerSync.SpawnSpotOf(_client.Peer)!.Value;
                _walkedTo = spawn + new Vector3(1.5f, 0, -1f);
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _walkedTo);
            }
            return frame > 30 && _client.Main.LocalPlayer!.GlobalPosition.DistanceTo(_walkedTo) < 0.5f;
        });
        Add("the client leaves and the host remembers it", frame =>
        {
            if (frame == 0)
                _harness.Stop(_client);
            if (_host.Main.PlayerSync.Players.Count != 1)
                return false;
            var saved = _host.Main.Saves.Roster.Find(PlayerProfile.Fixed("Client").Id);
            Check(saved is { Name: "Client", Health: > 0 } && saved.Inventory.Sum(s => s.Count) == 5, $"the roster has the client's five scrap ({saved})");
            return true;
        });
        Add("the host saves the world", _ =>
        {
            _host.Main.Saves.Store = _store;
            var path = _host.Main.Saves.SaveNow("Test World");
            Check(path is not null && File.Exists(path) && Path.GetFileName(path) == "test-world.json", $"the save is written ({path})");
            var diagnostics = new List<DataDiagnostic>();
            _save = SaveStore.Read(path!, diagnostics)!;
            Check(_save is not null && diagnostics.Count == 0, $"the save reads back ({string.Join("; ", diagnostics)})");
            Check(_save!.Name == "Test World" && _save.Seed == _harness.Seed && _save.Creations.Count == 1 && _save.Players.Count == 2,
                $"it holds the seed, the chest and both players ({_save.Creations.Count} creations, {_save.Players.Count} players)");
            Check(_store.List() is [{ Name: "Test World", Error: null }], "the load list shows it");
            return true;
        });
        Add("the save screen lists and loads it", _ =>
        {
            var screen = new Saves.SaveScreen { Store = _store, Saves = _host.Main.Saves };
            SaveGame? picked = null;
            screen.LoadRequested += save => picked = save;
            AddChild(screen);
            Check(screen.Listed is [{ Name: "Test World" }], $"one world is listed ({screen.Listed.Count})");
            screen.Load(0);
            Check(picked is { Name: "Test World", Creations.Count: 1 }, "Load reads the picked save");
            screen.QueueFree();
            return true;
        });
        Add("both instances stop and a new host loads the save", frame =>
        {
            if (frame == 0)
            {
                _harness.Stop(_host);
                _harness.Save = _save;
                StartWorld();
            }
            if (_host.Main.PlayerSync.Players.Count != 2 || ClientOwn is null)
                return false;
            var chest = _host.Main.Creations.Find(_chest);
            Check(chest?.InventoryOf(1)?[3] is { Item: Scrap, Count: 2 }, "the chest is back with its scrap");
            Check(_host.Main.Inventory.Own?.Count(Wood) == 3, "the host has its wood back");
            Check(_host.Main.Saves.WorldName == "Test World", "the world keeps its name");
            return true;
        });
        Add("the returning client gets its things and place back", _ =>
        {
            if (ClientOwn?.Count(Scrap) != 5 || _client.Main.LocalPlayer is not { } me || me.GlobalPosition.DistanceTo(_walkedTo) > 0.5f)
                return false;
            Check(ClientOwn.Items.Count == 1, $"only its own scrap ({ClientOwn.Items.Count} stacks)");
            return true;
        });
        Add("the client sees the loaded chest", frame =>
        {
            if (frame == 0)
                _client.Main.Inventory.Open(ChestRef);
            return _client.Main.Creations.Find(_chest) is not null && _client.Main.Inventory.Shown.GetValueOrDefault(ChestRef)?.At(3) is { Count: 2 };
        });
        Add("a second instance with the same profile is refused", frame =>
        {
            if (frame == 0)
                _twin = _harness.StartClient("Client", mods: _mods);
            if (_twin!.Main.Session.State != Net.NetSession.SessionState.Offline || frame < 5)
                return false;
            Check(_twin.Main.Session.LastMessage.Contains("already in this session"), $"refused: {_twin.Main.Session.LastMessage}");
            Check(_host.Main.PlayerSync.Players.Count == 2, "the session still has two players");
            return true;
        });
    }

    private void StartWorld()
    {
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);
    }

    private void Request<TArgs>(Instance who, string action, TArgs args) => who.Main.Actions.Request(action, args, _results.Add);
}
