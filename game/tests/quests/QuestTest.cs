using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Player;
using Mechanica.Core.Quests;
using Mechanica.Core.Saves;
using Mechanica.Game.Quests;
using Mechanica.Scripting;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Quests with a host plus a client (chunk 24, D10), on the vanilla story. The client gets the story's state
/// on join, puts out the crash site's three fires with buckets of water and restores power through
/// <c>QuestInteract</c>; the host counts each one, both players get the rewards and the
/// client hears the outro. With the story moved on to the faction envoy, the client talks to the envoy and
/// picks a side in the quest log, which sets the flag on every peer. A save keeps the story's state.
/// </summary>
public partial class QuestTest : StagedTest
{
    private const string GettingStarted = "vanilla:getting_started";
    private const string FactionContact = "vanilla:faction_contact";

    private readonly List<ActionResult> _results = [];
    private readonly List<QuestDialogue> _clientHeard = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private List<QuestMarker> _targets = [];
    private bool _asked;
    private QuestHud? _hud;

    protected override string Tag => "quests";

    private QuestSync HostQuests => _host.Main.Quests;

    private QuestSync ClientQuests => _client.Main.Quests;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");
        ClientQuests.Said += _clientHeard.Add;

        Add("the client joins and gets the story", _ =>
            _host.Main.PlayerSync.Players.Count == 2 && ClientQuests.State.Of(GettingStarted) is { Status: QuestStatus.Active });
        Add("the crash site's fires are quest markers", _ =>
        {
            var site = HostQuests.SitesOf("vanilla:crash_site");
            Check(site.Count == 1, $"one crash site ({site.Count})");
            _targets = [.. site[0].Markers.Where(m => m.Type == "fire")];
            Check(_targets.Count == 3, $"three fires ({_targets.Count})");
            Check(ClientQuests.SitesOf("vanilla:crash_site").Count == 1, "the client's world has the same site");
            return true;
        });
        for (var i = 0; i < 3; i++)
        {
            var fire = i;
            Add($"the client puts out fire {fire + 1} with a bucket of water", frame =>
            {
                var at = _targets[fire].Position.ToGodot() + new Vector3(0, 0.3f, 0);
                if (frame == 0)
                {
                    _asked = false;
                    _host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Add("vanilla:water_bucket_full", 1);
                    _host.Main.PlayerSync.HostTeleport(_client.Peer, new Vector3(at.X + 2.5f, at.Y, at.Z));
                }
                // Keep looking at it while the teleport settles, so the aim the host checks matches.
                if (frame is >= 10 and <= 40)
                    LookAt(at);
                if (frame < 40)
                    return false;
                if (frame == 40)
                {
                    Check(ClientQuests.Prompt() is not { Marker: "fire" }, "E does nothing to a fire");
                    var aim = (at - _client.Main.LocalPlayer!.Head.GlobalPosition).Normalized();
                    var slot = _client.Main.Inventory.Own!.Items.First(s => s.Item == "vanilla:water_bucket_full").Slot;
                    AskOnce(ScriptRuntime.UseItem, new UseItemArgs(slot, new Vec3(aim.X, aim.Y, aim.Z)));
                }
                if (_results.Count <= fire)
                    return false;
                if (!_results[fire].Ok)
                    Check(false, $"the bucket failed ({_results[fire]})");
                return ClientQuests.State.Of(GettingStarted) is { } p && (fire < 2 ? p.Count == fire + 1 : p.Step == 1);
            });
        }
        Add("the client restores power and both players are rewarded", frame =>
        {
            var battery = HostQuests.SitesOf("vanilla:crash_site")[0].Markers.First(m => m.Ref == "master_battery").Position;
            var near = new Vector3(battery.X + 1f, battery.Y - 0.5f, battery.Z);
            if (frame == 0)
            {
                _asked = false;
                _host.Main.PlayerSync.HostTeleport(_client.Peer, near);
            }
            if (!_asked && (_client.Main.LocalPlayer!.GlobalPosition.DistanceTo(near) > 0.6f || ClientQuests.Prompt() is not { Ref: "master_battery" }))
                return false;
            AskOnce(QuestTracker.QuestInteract, new NoArgs());
            if (!ClientQuests.State.IsDone(GettingStarted) || _client.Main.Inventory.Own?.Count("vanilla:component_kit") != 2
                || _host.Main.Inventory.Own?.Count("vanilla:component_kit") != 2)
                return false;
            Check(_client.Main.Inventory.Own.Count("vanilla:battery_pack") == 1, "the client got the battery pack");
            Check(ClientQuests.State.Of("vanilla:workshop_outpost") is { Status: QuestStatus.Active }, "the next quest started");
            return true;
        });
        Add("the client hears the outro", _ =>
            _clientHeard.Any(d => d.Quest == GettingStarted && d.Lines.Any(l => l.Text!.StartsWith("Power restored", StringComparison.Ordinal))));
        Add("the client talks to the faction envoy and picks a side", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _asked = false;
                // The story so far: everything up to the envoy is done, and the envoy waits on a choice.
                var state = new QuestState();
                foreach (var done in new[] { GettingStarted, "vanilla:workshop_outpost", "vanilla:first_vehicle", "vanilla:first_farm" })
                    state.Quests[done] = new QuestProgress { Status = QuestStatus.Done, Step = HostQuests.Book.Find(done)!.Steps.Count };
                var choose = HostQuests.Book.Find(FactionContact)!.Steps.ToList().FindIndex(s => s.Kind == QuestObjectiveKind.Choose);
                state.Quests[FactionContact] = new QuestProgress { Status = QuestStatus.Active, Step = choose };
                HostQuests.Load(state);
                var envoy = HostQuests.SitesOf("vanilla:faction_outpost").SelectMany(s => s.Markers).First(m => m.Type == "npc").Position;
                _host.Main.PlayerSync.HostTeleport(_client.Peer, new Vector3(envoy.X + 1f, envoy.Y, envoy.Z));
            }
            if (!_asked && (ClientQuests.PendingChoices().FirstOrDefault() is not { Quest.QualifiedId: FactionContact } || ClientQuests.Prompt() is not { Marker: "npc" }))
                return false;
            if (!_asked)
            {
                Request(_client, QuestTracker.QuestInteract, new NoArgs());
                _hud = new QuestHud { Quests = ClientQuests, Actions = _client.Main.Actions, Players = _client.Main.PlayerSync };
                AddChild(_hud);
                _hud.Open();
                var buttons = _hud.FindChildren("Choice_*", "Button", owned: false);
                Check(buttons.Count == 2, $"the Logbook offers both choices ({buttons.Count})");
                Check(_hud.Logbook.Visible && _hud.Logbook.List.FindChild(LogbookWindow.NodeName("Quest", FactionContact), owned: false) is Button, "the Logbook lists the quest");
                Check(_hud.LogText.Contains("Faction Contact", StringComparison.Ordinal), $"the log shows the quest ({_hud.LogText})");
                _hud.Choose(FactionContact, "ally");
                _asked = true;
            }
            if (_results.Count < 1)
                return false;
            if (!_results.All(r => r.Ok))
                Check(false, $"talking failed ({string.Join("; ", _results)})");
            if (ClientQuests.State.Flags.GetValueOrDefault("faction_stance") != "ally" || !ClientQuests.State.IsDone(FactionContact))
                return false;
            _hud!.Logbook.Refresh();
            Check(_hud.IsOpen && _hud.FindChildren("Choice_*", "Button", owned: false).Count == 0, "the Logbook stays open with the choices gone once decided");
            Check(_hud.Logbook.List.FindChild(LogbookWindow.NodeName("Done", FactionContact), owned: false) is Label, "the finished quest is listed greyed out");
            _hud.Close();
            _hud.QueueFree();
            return true;
        });
        Add("a save keeps the story", _ =>
        {
            var save = _host.Main.Saves.Capture("Quest World");
            var diagnostics = new List<DataDiagnostic>();
            var read = SaveGame.Read(save.Write(), "quest-world.json", diagnostics);
            Check(read?.Quests is { } quests && quests.Flags.GetValueOrDefault("faction_stance") == "ally" && quests.IsDone(FactionContact),
                $"the read save has the flag and the finished quest ({string.Join("; ", diagnostics)})");
            return true;
        });
    }

    // Sends one request per stage: results take a few frames to come back.
    private void AskOnce<TArgs>(string action, TArgs args)
    {
        if (_asked)
            return;
        _asked = true;
        Request(_client, action, args);
    }

    private void Request<TArgs>(Instance who, string action, TArgs args) => who.Main.Actions.Request(action, args, _results.Add);

    private void LookAt(Vector3 target)
    {
        var d = (target - _client.Main.LocalPlayer!.Head.GlobalPosition).Normalized();
        _client.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
