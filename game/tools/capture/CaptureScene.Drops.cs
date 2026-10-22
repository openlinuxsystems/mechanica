using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;

namespace Mechanica.Game.Tools;

// Items on the ground (tools/capture-drops.sh OUT_DIR): a Rakehand killed drops its loot as one glowing pile per item,
// close up and in first person with the Use prompt naming the pile aimed at; then a row of items of every kind (blocks,
// parts, food, resources, tools) and the beams seen from afar.
public partial class CaptureScene
{
    private static readonly string[] DropRow =
    [
        "vanilla:scrap_wood_block", "vanilla:brick_block", "vanilla:glass_block", "vanilla:metal_wedge", "vanilla:bearing",
        "vanilla:wheel", "vanilla:gas_engine", "vanilla:small_chest", "vanilla:tomato", "vanilla:carrot_seed",
        "vanilla:circuit_board", "vanilla:ember", "vanilla:water_bucket", "vanilla:weld_tool", "vanilla:paint_tool",
        "vanilla:connect_tool", "vanilla:potato_blaster", "vanilla:fertilizer",
    ];

    private void DropShots()
    {
        var driver = new ActionCaller(Creation.HostPeer, ActionSource.Bot);
        var feet = Vector3.Zero;
        var loot = Vector3.Zero;
        var rakehand = 0;
        void View(Vector3 from, Vector3 to)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = from;
            _camera.LookAt(to);
        }
        Vector3 Pile(string item) => _main.Survival.Drops.Values.FirstOrDefault(d => d.Items[0].Item == item)?.Position.ToGodot() ?? loot;
        Shot("drops_00_wait", 120, () =>
        {
            Engine.TimeScale = 1.0;
            feet = Player.GlobalPosition;
            // The first-play tip would cover the prompt.
            if (_main.GetNodeOrNull<CanvasLayer>("TutorialHints") is { } tips)
                tips.Visible = false;
            loot = Ground(feet + new Vector3(0, 0, -4));
            // Killed where it stands up, before it can swing at the player.
            var result = _main.Actions.Dispatcher.Dispatch(driver, BotActions.SpawnBot, new SpawnBotArgs("vanilla:rakehand", [loot.X, loot.Y + 0.2f, loot.Z]));
            GD.Print($"[capture] rakehand: {result.Ok} {result.Message}");
            rakehand = result.Ok ? result.Value!.Value.Deserialize<SpawnBotResult>(ActionJson.Options)!.Bot : 0;
            _main.Bots.Bots!.DamageBot(rakehand, 10000, "capture", melee: false);
        });
        Shot("drops_01_killed", 150, () =>
        {
            // What the screenshot showed: a Component Kit, Gasoline and Scrap Metal Blocks, whatever the roll gave.
            if (!_main.Survival.Drops.Values.Any())
                _main.Survival.Actions!.AddDrop((loot + Vector3.Up * 0.5f).ToNumerics(),
                    [new ItemStack("vanilla:component_kit", 1), new ItemStack("vanilla:gasoline", 1), new ItemStack("vanilla:scrap_metal_block", 8)]);
            View(loot + new Vector3(-2.6f, 1.7f, 3.0f), loot + Vector3.Up * 0.3f);
        });
        Shot("drops_02_loot", 30, () =>
        {
            GD.Print($"[capture] loot: {string.Join(", ", _main.Survival.Drops.Values.Select(d => $"{d.Items[0].Item} x{d.Items[0].Count}"))}");
            View(loot + new Vector3(-1.6f, 1.1f, 1.9f), loot + Vector3.Up * 0.3f);
        });
        Shot("drops_03_loot_low", 30, () => View(loot + new Vector3(1.4f, 0.55f, 1.4f), loot + Vector3.Up * 0.35f));
        // The prompt follows the pile nearest the player: stand by one and look at it from eye height.
        void Beside(int index) => _main.PlayerSync.HostTeleport(_main.Session.LocalPeer,
            _main.Survival.Drops.Values.ElementAt(index).Position.ToGodot() + new Vector3(0, 0.2f, 1.3f), 0f);
        void FromEye(int index)
        {
            var pile = _main.Survival.Drops.Values.ElementAt(index).Position.ToGodot();
            View(Player.GlobalPosition + new Vector3(0, 1.55f, -0.3f), pile + Vector3.Up * Core.Survival.DropAim.Middle);
        }
        Shot("drops_04_walk_up", 40, () => Beside(0));
        Shot("drops_05_prompt", 20, () => FromEye(0));
        Shot("drops_06_walk_on", 40, () => Beside(_main.Survival.Drops.Count - 1));
        Shot("drops_06_prompt_other", 20, () => FromEye(_main.Survival.Drops.Count - 1));
        Shot("drops_07_row", 90, () =>
        {
            var start = Ground(feet + new Vector3(6, 0, -6));
            for (var i = 0; i < DropRow.Length; i++)
            {
                var at = start + new Vector3(i % 6 * 1.3f, 0, i / 6 * 1.3f);
                _main.Survival.Actions!.AddDrop((Ground(at) + Vector3.Up * 0.4f).ToNumerics(), [new ItemStack(DropRow[i], 1)]);
            }
            var middle = Ground(start + new Vector3(3.25f, 0, 1.3f));
            View(middle + new Vector3(-0.5f, 3.2f, 6.2f), middle + Vector3.Up * 0.3f);
        });
        Shot("drops_08_row_close", 30, () =>
        {
            var middle = Ground(feet + new Vector3(8.0f, 0, -4.7f));
            View(middle + new Vector3(-0.3f, 1.2f, 2.8f), middle + Vector3.Up * 0.3f);
        });
        Shot("drops_09_blocks_close", 30, () =>
        {
            var at = Pile("vanilla:brick_block");
            View(at + new Vector3(-0.5f, 0.7f, 1.2f), at + Vector3.Up * 0.35f);
        });
        Shot("drops_10_tools_close", 30, () =>
        {
            var at = Pile("vanilla:paint_tool");
            View(at + new Vector3(-0.4f, 0.75f, 1.3f), at + Vector3.Up * 0.35f);
        });
        Shot("drops_11_far", 60, () =>
        {
            var middle = Ground(feet + new Vector3(4, 0, -5));
            View(middle + new Vector3(-6f, 3.5f, 14f), middle + Vector3.Up * 0.8f);
        });
    }
}
