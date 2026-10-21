using Godot;
using Mechanica.Core.Bots;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;
using Mechanica.Game.World;

namespace Mechanica.Game.Tools;

// The husbandry gear in a running world (tools/capture-husbandry.sh): the Lead in hand, a tame Grazer led on it and
// following, the rope at its collar, and the Feeding Trough with a herd living round it.
public partial class CaptureScene
{
    private void HusbandryShots()
    {
        const string trough = "vanilla:feeding_trough";
        var feet = Vector3.Zero;
        var grazers = new List<int>();
        Vector3 Ground(Vector3 at) => at with { Y = _main.Streamer.Generator.HeightAt(at.X, at.Z) };
        void View(Vector3 from, Vector3 to)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = from;
            _camera.LookAt(to);
        }
        void Spawn(Vector3 at) => _main.Actions.Request(BotActions.SpawnBot, new SpawnBotArgs("vanilla:grazer", [at.X, at.Y + 0.2f, at.Z]), r =>
        {
            if (r.Ok && r.Value is { } value && System.Text.Json.JsonSerializer.Deserialize<SpawnBotResult>(value, Core.Actions.ActionJson.Options) is { } made)
            {
                grazers.Add(made.Bot);
                _main.Bots.Bots!.Keep(made.Bot);
            }
            GD.Print($"[capture] grazer: {r.Ok} {r.Message}");
        });
        Shot("husbandry_00_wait", 120, () =>
        {
            Engine.TimeScale = 1.0;
            feet = Player.GlobalPosition;
            Spawn(Ground(feet + new Vector3(0, 0, -2.6f)));
        });
        Shot("husbandry_01_lead_held", 40, () =>
        {
            Hold(CharacterProp.Item);
            Player.HeldItem = _main.HeldItems.IndexOf("vanilla:lead");
            Look(new Vector3(-1.4f, 1.4f, -2.0f), 1.1f);
        });
        Shot("husbandry_02_lead_held_side", 20, () => Look(new Vector3(-2.2f, 1.2f, 0.3f), 1.1f));
        Shot("husbandry_03_lead_fp", 20, () => { Input(); Player.MakeCameraCurrent(); });
        Shot("husbandry_04_on_the_lead", 60, () =>
        {
            _main.Actions.Request(BotActions.LeadBot, new BotArgs(grazers[0]), r => GD.Print($"[capture] lead: {r.Ok} {r.Message}"));
            Look(new Vector3(-3.2f, 2.0f, -2.6f), 0.8f);
        });
        // walk off: it comes after
        Shot("husbandry_05_following", 150, () => { Input(z: 0.5f); Look(new Vector3(-4.2f, 2.2f, 2.0f), 0.6f); });
        Shot("husbandry_06_following_side", 60, () => Look(new Vector3(4.5f, 1.8f, 0.8f), 0.6f));
        Shot("husbandry_07_stopped", 120, () => { Input(); Look(new Vector3(-3.0f, 1.8f, 3.6f), 0.6f); });
        Shot("husbandry_08_collar_rope", 20, () =>
        {
            var bot = _main.Bots.Bodies[grazers[0]];
            var ring = bot.Model?.LeadNode?.GlobalPosition ?? bot.GlobalPosition;
            View(ring + new Vector3(-2.0f, 0.8f, -1.6f), ring);
        });
        Shot("husbandry_09_trough", 90, () =>
        {
            feet = Player.GlobalPosition;
            var at = Ground(feet + new Vector3(3, 0, -3));
            _main.Actions.Request(Core.Building.BuildActions.PlacePart,
                new Core.Building.PlacePartArgs(trough, GridCell.Zero, Position: Vec3Numerics.ToVec3((at + new Vector3(0, 0.27f, 0)).ToNumerics())),
                r => GD.Print($"[capture] trough: {r.Ok} {r.Message}"));
            _main.Actions.Request(BotActions.LeadBot, new BotArgs(grazers[0]), r => GD.Print($"[capture] let go: {r.Ok} {r.Message}"));
            Spawn(Ground(at + new Vector3(-2.5f, 0, 2.5f)));
            Spawn(Ground(at + new Vector3(2.5f, 0, 2f)));
            Hold(CharacterProp.None);
            View(at + new Vector3(-3.4f, 2.4f, 4.2f), at);
        });
        Shot("husbandry_10_trough_empty", 30, () => { });
        foreach (var (fill, carrots) in new[] { (1, 10), (2, 40), (3, 80) })
        {
            Shot($"husbandry_{10 + fill}_trough_fill{fill}", 120, () =>
            {
                var creation = _main.Creations.All.First(c => c.Parts.Any(p => p.Part == trough));
                var part = creation.Parts.First(p => p.Part == trough).Id;
                var inventory = _main.Inventory.Items!.Find(new InventoryRef(Creation: creation.Id, Part: part))!;
                inventory.Add("vanilla:carrot", carrots - inventory.Count("vanilla:carrot"));
                var at = creation.BodyPose(0).Position.ToGodot();
                View(at + new Vector3(-1.2f, 1.7f, 1.9f), at);
            });
        }
        Shot("husbandry_14_herd_home", 600, () =>
        {
            var creation = _main.Creations.All.First(c => c.Parts.Any(p => p.Part == trough));
            var at = creation.BodyPose(0).Position.ToGodot();
            View(at + new Vector3(-7f, 4.5f, 8f), at);
            foreach (var id in grazers)
                GD.Print($"[capture] grazer {id} home {_main.Bots.Bots!.Find(id)?.Home}");
        });
    }
}
