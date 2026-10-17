using Godot;
using Mechanica.Core.Data;
using Mechanica.Game.Editor;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Bot Behavior Editor's "Test in Sandbox" button (chunk 21 leftover): a bot made from a template in a
/// scratch mod exports and spawns in the Test Sandbox in front of the player, a local client sees it (D10),
/// Back returns to the editor, and a second bot made afterwards spawns too, so the sandbox reloads bot types
/// between tests. Exits 0 on success.
/// </summary>
public partial class BotSandboxTest : StagedTest
{
    private const string Mod = "bot_sandbox_test";

    private BotEditorMode _mode = null!;
    private SandboxScreen _sandbox = null!;
    private SandboxScreen.Instance _client = null!;

    protected override string Tag => "botsandbox";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        var modsRoot = ProjectSettings.GlobalizePath("user://tests/bot_sandbox_mods");
        if (Directory.Exists(modsRoot))
            Directory.Delete(modsRoot, recursive: true);
        Directory.CreateDirectory(Path.Combine(modsRoot, Mod));
        File.WriteAllText(Path.Combine(modsRoot, Mod, ModLoader.ManifestFileName),
            $$"""{ "id": "{{Mod}}", "name": "Bot sandbox test", "version": "1.0.0" }""");
        _mode = new BotEditorMode { Name = "Mode", ModsRoot = modsRoot };
        AddChild(_mode);

        Add("Test in Sandbox exports the bot and opens the sandbox", _ =>
        {
            Check(_mode.Editor.SelectMod(Mod) && _mode.Editor.New("melee", "biter"), "a bot starts from the melee template");
            Check(_mode.Editor.TestInSandbox(), $"the bot exports ({_mode.Editor.StatusText})");
            _sandbox = _mode.Sandbox!;
            Check(_sandbox is not null && !_mode.Editor.Visible, "the editor hides and the sandbox opens");
            return true;
        });
        Add("the bot spawns in front of the player", _ => Bots(_sandbox.Host).Contains($"{Mod}:biter"));
        Add("the spawn is reported and the bot is listed", _ =>
        {
            Check(_sandbox.BotIds.Contains($"{Mod}:biter"), "the bot spawner lists the bot");
            Check(_sandbox.LastResult.StartsWith("Spawned bot", StringComparison.Ordinal), $"the spawn result is shown ({_sandbox.LastResult})");
            var player = _sandbox.Host.Main.LocalPlayer!.GlobalPosition;
            var bot = _sandbox.Host.Main.Bots.Bodies.Values.Single().GlobalPosition;
            var flat = new Vector2(bot.X - player.X, bot.Z - player.Z).Length();
            Check(flat is > 3 and < 9, $"it stands a few metres from the player ({flat:F1} m)");
            return true;
        });
        Add("a local client sees the bot", frame =>
        {
            if (frame == 0)
                _client = _sandbox.AddLocalClient()!;
            return _client.Main.LocalPlayer is not null && Bots(_client).Contains($"{Mod}:biter");
        });
        Add("back returns to the editor with the bot still open", _ =>
        {
            _sandbox.EmitBack();
            Check(_mode.Editor.Visible && !_sandbox.Visible && _mode.Editor.Document?.Id == "biter", "the editor is back");
            return true;
        });
        Add("a bot made afterwards spawns too", frame =>
        {
            if (frame == 0)
            {
                Check(_mode.Editor.New("ranged", "spitter") && _mode.Editor.TestInSandbox(), $"a second bot exports ({_mode.Editor.StatusText})");
                Check(_sandbox.Visible && !_mode.Editor.Visible, "the sandbox shows again");
            }
            return Bots(_sandbox.Host).Contains($"{Mod}:spitter") && Bots(_client).Contains($"{Mod}:spitter");
        });
    }

    private static List<string> Bots(SandboxScreen.Instance instance) =>
        [.. instance.Main.Bots.Bodies.Values.Select(b => b.Type.QualifiedId)];
}
