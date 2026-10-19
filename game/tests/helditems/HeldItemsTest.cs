using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// A modded item's <c>held</c> model, host plus client: the client takes the test mod's lantern in hand and both machines
/// draw its model in the client's right hand (the client in first person), placed and scaled as the part file says; putting it away removes it on both.
/// </summary>
public partial class HeldItemsTest : StagedTest
{
    private const string Lantern = "held_test:lantern";

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;

    protected override string Tag => "helditems";

    protected override int StageTimeoutFrames => 900;

    private int Peer => _client.Peer;

    // The client's player as the host draws it, and its first-person arms on the client.
    private CharacterModel HostView => (CharacterModel)_host.Main.PlayerSync.Players[Peer].Character!;

    private CharacterModel ClientView => (CharacterModel)_client.Main.LocalPlayer!.Arms!;

    public override void _Ready()
    {
        var mods = ModLoader.Load([Path.Combine(ModPaths.ModsRoot, "vanilla"), ProjectSettings.GlobalizePath("res://tests/helditems/mods/held_test")]);
        Check(!mods.HasErrors, $"the test mod loads ({string.Join("; ", mods.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))})");
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost(mods: mods);
        _client = _harness.StartClient("Client", mods: mods);

        Add("the client joins and is given the lantern", _ =>
        {
            if (_client.Main.Inventory.Own is null || _client.Main.Tools is null || _client.Main.LocalPlayer is not { } player
                || !player.IsOnFloor() || !_host.Main.PlayerSync.Players.ContainsKey(Peer))
                return false;
            Check(_host.Main.HeldItems.IndexOf(Lantern) == 1 && _client.Main.HeldItems.IndexOf(Lantern) == 1,
                "both machines number the lantern the same");
            _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Lantern, 1);
            return true;
        });
        Add("in hand, both machines draw its model in the right hand", frame =>
        {
            var tools = _client.Main.Tools!;
            var slot = tools.Slots.ToList().IndexOf(Lantern);
            if (slot < 0)
            {
                if (frame % 30 == 0 && _client.Main.Inventory.Own!.Items.Any(s => s.Item == Lantern))
                    tools.Hotbar!.Set(tools.Slots.Count - 1, Lantern); // the last slot holds a starting item
                return false;
            }
            if (tools.Item.InHand?.Item != Lantern)
            {
                tools.Select(slot);
                return false;
            }
            if (HostView.HeldNode is not { } host || ClientView.HeldNode is not { } client)
                return false;
            foreach (var (held, who) in new[] { (host, "host"), (client, "client") })
            {
                Check(held.GetParent() is BoneAttachment3D, $"the {who} draws it on the hand bone ({held.GetParent()?.GetType().Name})");
                Check(held.GetChildCount() == 1 && held.FindChild("*", true, false) is Node3D, $"the {who} draws the model");
                Check(held.Scale.IsEqualApprox(Vector3.One * 0.25f), $"the {who} scales it ({held.Scale})");
                Check(held.Position.IsEqualApprox(new Vector3(0, 0.1f, 0)), $"the {who} offsets it ({held.Position})");
                Check(Mathf.IsEqualApprox(Mathf.RadToDeg(held.Rotation.Y), 90f, 0.1f), $"the {who} turns it ({held.RotationDegrees})");
            }
            return true;
        });
        Add("putting it away removes it on both", frame =>
        {
            var tools = _client.Main.Tools!;
            if (frame == 0)
                tools.Select(0); // the Sledgehammer
            return tools.Current != tools.Item && HostView.HeldNode is null && ClientView.HeldNode is null;
        });
    }
}
