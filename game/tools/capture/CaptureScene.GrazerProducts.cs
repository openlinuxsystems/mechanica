using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;

namespace Mechanica.Game.Tools;

// Consumables batch C3 in a running world (tools/capture-consumables.sh OUT_DIR grazer_products): a tame Grazer eats the
// carrots in its Feeding Trough until its milk is ready (the cue over it), the player collects it and holds the bottle;
// a Grazer killed drops Grazer Steak, which the player then holds.
public partial class CaptureScene
{
    private void GrazerProductShots()
    {
        const string trough = "vanilla:feeding_trough";
        var driver = new ActionCaller(Creation.HostPeer, ActionSource.Bot);
        var feet = Vector3.Zero;
        var grazer = 0;
        var other = 0;
        Vector3 Ground(Vector3 at) => at with { Y = _main.Streamer.Generator.HeightAt(at.X, at.Z) };
        Vector3 At(int bot) => _main.Bots.Bots!.Find(bot)?.Position.ToGodot() ?? feet;
        void View(Vector3 from, Vector3 to)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = from;
            _camera.LookAt(to);
        }
        int Spawn(Vector3 at)
        {
            var result = _main.Actions.Dispatcher.Dispatch(driver, BotActions.SpawnBot, new SpawnBotArgs("vanilla:grazer", [at.X, at.Y + 0.2f, at.Z]));
            GD.Print($"[capture] grazer: {result.Ok} {result.Message}");
            return result.Ok ? result.Value!.Value.Deserialize<SpawnBotResult>(ActionJson.Options)!.Bot : 0;
        }
        void Hurry()
        {
            if (grazer != 0)
                _main.Bots.Bots!.Hurry(grazer);
        }
        Shot("products_00_wait", 120, () =>
        {
            Engine.TimeScale = 1.0;
            feet = Player.GlobalPosition;
            var at = Ground(feet + new Vector3(3, 0, -3));
            var placed = _main.Actions.Dispatcher.Dispatch(driver, Core.Building.BuildActions.PlacePart,
                new Core.Building.PlacePartArgs(trough, GridCell.Zero, Position: Vec3Numerics.ToVec3((at + new Vector3(0, 0.27f, 0)).ToNumerics())));
            GD.Print($"[capture] trough: {placed.Ok} {placed.Message}");
            var made = placed.Value!.Value.Deserialize<Core.Building.PlacePartResult>(ActionJson.Options)!;
            _main.Inventory.Items!.Find(InventoryRef.OfPart(made.Creation, made.Part))!.Add("vanilla:carrot", 12);
            grazer = Spawn(Ground(at + new Vector3(-1.8f, 0, 1.6f)));
            _main.Bots.Bots!.Keep(grazer);
        });
        // a meal at each shot's start: it plays its eat clip, and after five the cue rises over it
        for (var meal = 1; meal <= 5; meal++)
        {
            var n = meal;
            Shot($"products_0{n}_meal", 75, () =>
            {
                Hurry();
                var bot = At(grazer) + Vector3.Up * 0.8f;
                View(bot + new Vector3(-2.6f, 1.4f, 2.8f), bot);
            });
        }
        Shot("products_06_milk_ready", 60, () =>
        {
            Hurry();
            var bot = At(grazer) + Vector3.Up * 0.9f;
            View(bot + new Vector3(-2.4f, 1.0f, 2.6f), bot + Vector3.Up * 0.4f);
            GD.Print($"[capture] ready: {_main.Bots.Bots!.Find(grazer)?.Ready}, meals {_main.Bots.Bots!.Find(grazer)?.Meals}");
        });
        Shot("products_07_milk_ready_close", 30, () =>
        {
            var bot = At(grazer) + Vector3.Up * 1.4f;
            View(bot + new Vector3(-1.1f, 0.2f, 1.3f), bot + Vector3.Up * 0.2f);
        });
        Shot("products_08_collect", 25, () =>
        {
            var bot = At(grazer);
            var stand = Ground(bot + new Vector3(1.6f, 0, 0.6f)) + Vector3.Up * 0.2f;
            Player.GlobalPosition = stand;
            Player.Gesture(CharacterGesture.Interact);
            _main.Actions.Request(BotActions.CollectBot, new BotArgs(grazer), r => GD.Print($"[capture] collect: {r.Ok} {r.Message}"));
            var mid = (bot + stand) / 2 + Vector3.Up * 1.0f;
            View(mid + new Vector3(-1.0f, 1.0f, 3.6f), mid);
        });
        Shot("products_09_milk_held", 40, () =>
        {
            Hold(CharacterProp.Drink);
            Player.HeldItem = _main.HeldItems.IndexOf("vanilla:grazer_milk");
            Look(new Vector3(-1.2f, 1.5f, -1.9f), 1.25f);
        });
        Shot("products_10_milk_drink", 16, () => Player.Gesture(CharacterGesture.Drink));
        Shot("products_11_milk_first_person", 40, () => { Input(); Player.MakeCameraCurrent(); });
        Shot("products_12_steak_kill", 30, () =>
        {
            Hold(CharacterProp.None);
            feet = Player.GlobalPosition;
            other = Spawn(Ground(feet + new Vector3(-2.5f, 0, -2.5f)));
        });
        Shot("products_13_steak_drop", 150, () =>
        {
            _main.Bots.Bots!.DamageBot(other, 1000, "capture", melee: false);
            var bot = At(other) + Vector3.Up * 0.4f;
            View(bot + new Vector3(-2.2f, 1.6f, 2.4f), bot);
        });
        Shot("products_14_steak_close", 120, () =>
        {
            var drop = _main.Survival.Drops.Values.FirstOrDefault(d => d.Items.Any(s => s.Item == "vanilla:grazer_steak"));
            GD.Print($"[capture] steak drop: {drop?.Id} {string.Join(", ", drop?.Items.Select(s => $"{s.Item} x{s.Count}") ?? [])}");
            var at = drop?.Position.ToGodot() ?? At(other);
            View(at + new Vector3(-0.9f, 0.8f, 1.0f), at);
        });
        Shot("products_15_steak_held", 40, () =>
        {
            Hold(CharacterProp.Item);
            Player.HeldItem = _main.HeldItems.IndexOf("vanilla:grazer_steak");
            Look(new Vector3(-1.2f, 1.4f, -1.9f), 1.15f);
        });
        Shot("products_16_steak_first_person", 30, () => { Input(); Player.MakeCameraCurrent(); });
    }
}
