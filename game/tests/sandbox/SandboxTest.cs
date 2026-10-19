using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Game.Editor;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// End-to-end test of chunk 5's "done when": a block authored in the Part Editor loads in the Test
/// Sandbox, with a host and local clients (D10). The editor makes and exports a block into a fresh mod;
/// "Test in Sandbox" opens the sandbox, which spawns it through the Action API and drops it on the
/// ground. Two local clients join and receive it; a client's own spawn is validated by the host and
/// shows up everywhere, a bad request is refused, and clearing removes it for all. Exits 0 on success.
/// </summary>
public partial class SandboxTest : StagedTest
{
    private const string Mod = "sandbox_test";
    private const string Part = $"{Mod}:test_crate";

    private PartEditorMode _mode = null!;
    private SandboxScreen _sandbox = null!;
    private SandboxScreen.Instance _client1 = null!;
    private SandboxScreen.Instance _client2 = null!;
    private ActionResult? _clientSpawn;
    private ActionResult? _badSpawn;
    private ActionResult? _clear;
    private Task<ExportResult?>? _export;

    protected override string Tag => "sandbox";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        var modsRoot = ProjectSettings.GlobalizePath("user://tests/sandbox_mods");
        if (Directory.Exists(modsRoot))
            Directory.Delete(modsRoot, recursive: true);
        Directory.CreateDirectory(modsRoot);
        _mode = new PartEditorMode { Name = "Mode", ModsRoot = modsRoot };
        AddChild(_mode);

        Add("the editor makes and exports a block", frame =>
        {
            if (frame == 0)
            {
                Check(_mode.Editor.NewPart(Mod, "test_crate", "Test Crate", "block") is null, "a block starts from the template");
                var doc = _mode.Editor.Document!;
                doc.AddPiece(ModelPiece.NewBox(new Vec3(0.25f, 0.25f, 0.25f)));
                doc.AddPiece(new ModelPiece { Kind = ModelPieceKind.Box, Operation = CsgOperation.Subtract, Size = new Vec3(0.2f, 0.2f, 0.3f) });
                _export = _mode.Editor.ExportAsync();
            }
            if (_export is not { IsCompleted: true })
                return false;
            Check(_export.Result is { Succeeded: true }, $"export succeeds ({string.Join("; ", _export.Result?.Diagnostics.Select(d => d.ToString()) ?? [])})");
            _mode.Test(Part);
            _sandbox = _mode.Sandbox!;
            Check(_sandbox is not null && !_mode.Editor.Visible, "Test in Sandbox hides the editor and opens the sandbox");
            return true;
        });
        Add("the sandbox lists the part and spawns it in front of the player", _ =>
            HostCreations().Count == 1);
        Add("the block falls onto the ground and rests", frame =>
        {
            var body = _sandbox.Host.Main.Creations.NodeOf(HostCreations()[0])!.Bodies[0];
            if (frame < 30 || (!body.Sleeping && frame < 400))
                return false;
            Check(_sandbox.PartIds.Contains(Part), "the exported part is in the spawner list");
            Check(body.GlobalPosition.Y is > 0.09f and < 0.15f, $"it rests on the ground (y={body.GlobalPosition.Y:F3})");
            Check(_sandbox.LastResult.StartsWith("Spawned", StringComparison.Ordinal), $"the spawn result is shown ({_sandbox.LastResult})");
            return true;
        });

        Add("two local clients join", frame =>
        {
            if (frame == 0)
            {
                var first = _sandbox.AddLocalClient();
                var second = _sandbox.AddLocalClient();
                Check(first is not null && second is not null && _sandbox.Instances.Count == 3, "two clients start");
                (_client1, _client2) = (first!, second!);
            }
            return _sandbox.Host.Main.PlayerSync.Players.Count == 3 && _client1.Main.LocalPlayer is not null && _client2.Main.LocalPlayer is not null;
        });
        Add("late joiners receive the block", _ => Count(_client1) == 1 && Count(_client2) == 1);
        Add("a client's spawn is validated by the host and reaches everyone", frame =>
        {
            if (frame == 0)
            {
                _sandbox.Activate(0);
                _sandbox._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.F6, Keycode = Key.F6, Pressed = true });
                Check(_sandbox.Active == _client1, "the switch player key moves control to client 1");
                _sandbox.Spawn(Part, r => _clientSpawn = r);
                _client1.Main.Actions.Request(CreationActions.SpawnPart, new SpawnPartArgs($"{Mod}:missing", [0, 1, 0]), r => _badSpawn = r);
            }
            return _clientSpawn is not null && _badSpawn is not null && Count(_sandbox.Host) == 2 && Count(_client1) == 2 && Count(_client2) == 2;
        });
        Add("results come back to the client", _ =>
        {
            Check(_clientSpawn!.Ok, $"the client's spawn is accepted ({_clientSpawn})");
            Check(!_badSpawn!.Ok && _badSpawn.Message!.Contains("no part"), $"an unknown part is refused ({_badSpawn})");
            var hostNode = _sandbox.Host.Main.Creations.NodeOf(HostCreations()[1])!;
            var clientNode = _client2.Main.Creations.NodeOf(HostCreations()[1])!;
            Check(hostNode.IsSimulated && !clientNode.IsSimulated, "only the host simulates the block (D5)");
            Check(clientNode.Bodies[0].GlobalPosition.DistanceTo(hostNode.Creation.BodyPose(0).Position.ToGodot()) < 0.01f,
                "clients place it where the host spawned it");
            return true;
        });
        Add("a host request without a callback still runs", frame =>
        {
            if (frame == 0)
                _sandbox.Host.Main.Actions.Request(CreationActions.SpawnPart, new SpawnPartArgs(Part, [4, 1, 4]));
            return Count(_sandbox.Host) == 3 && Count(_client1) == 3 && Count(_client2) == 3;
        });
        Add("Clear all and Remove client ask first", frame =>
        {
            if (frame == 0)
            {
                _sandbox.AskClear();
                Check(ConfirmDialog.Current?.Window.Title == "Clear the sandbox", "Clear all asks");
                ConfirmDialog.Current?.Cancel();
                Check(_sandbox.AskRemoveClient() is not null && ConfirmDialog.Current?.Window.Title == "Remove client", "Remove client asks");
                ConfirmDialog.Current?.Cancel();
                return false;
            }
            if (frame < 10)
                return false;
            Check(Count(_sandbox.Host) == 3 && _sandbox.Instances.Count == 3, "Cancel leaves the Creations and clients alone");
            return true;
        });
        Add("clearing removes it for everyone", frame =>
        {
            if (frame == 0)
                _sandbox.Clear(r => _clear = r);
            return _clear is { Ok: true } && Count(_sandbox.Host) == 0 && Count(_client1) == 0 && Count(_client2) == 0;
        });
        Add("removing a client leaves the others connected", frame =>
        {
            if (frame == 0)
                _sandbox.RemoveLocalClient();
            return _sandbox.Instances.Count == 2 && _sandbox.Host.Main.PlayerSync.Players.Count == 2;
        });
        Add("back returns to the editor with the part still open", _ =>
        {
            _sandbox.EmitBack();
            Check(_mode.Editor.Visible && !_sandbox.Visible && _mode.Editor.Document?.Definition.Id == "test_crate", "the editor is back");
            return true;
        });
    }

    private List<int> HostCreations() => [.. _sandbox.Host.Main.Creations.All.Select(c => c.Id)];

    private static int Count(SandboxScreen.Instance instance) => instance.Main.Creations.All.Count();
}
