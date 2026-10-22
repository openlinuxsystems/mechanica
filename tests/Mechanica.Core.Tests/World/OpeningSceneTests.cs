using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class OpeningSceneTests
{
    private static readonly Lazy<WorldGenData> VanillaData = new(() => WorldGenData.Load(ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"))));

    private static OpeningSettings Settings => OpeningSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "opening.json")));

    private static (WorldGenerator Gen, OpeningScene Scene) Vanilla(long seed)
    {
        var gen = new WorldGenerator(VanillaData.Value, seed, 12032f, 9024f);
        var crash = Assert.Single(gen.Pois, p => p.Poi.QualifiedId == "vanilla:crash_site");
        return (gen, new OpeningScene(Settings, crash, gen.HeightAt));
    }

    [Fact]
    public void TheShippedOpeningIsWholeAndAsLongAsPlanned()
    {
        var settings = Settings;

        Assert.Empty(settings.Problems());
        Assert.InRange(settings.Duration, 25f, 35f);
        Assert.InRange(settings.Black[1], settings.Settle, settings.Duration);
    }

    [Fact]
    public void TheIntactShipIsTheWrecksPiecesPutBackTogether()
    {
        var (_, scene) = Vanilla(1234);
        var settings = scene.Settings;
        Assert.Equal(4, settings.Pieces.Count);
        foreach (var t in new[] { 0f, 6f, settings.Explode - 0.1f })
        {
            var pieces = scene.Pieces(t);
            // Every piece puts each of its points back where the intact ship has it (tools/structures/sv04_layout.py).
            foreach (var (piece, i) in settings.Pieces.Select((p, i) => (p, i)))
            {
                var baked = OpeningScene.Blender(piece.Wreck).Compose(new Pose(-OpeningScene.FromBlender(piece.Pivot), Quaternion.Identity));
                foreach (var dx in new[] { -3f, 0f, 3f })
                    foreach (var dy in new[] { -4f, 0f, 4f })
                    {
                        float[] point = [piece.Pivot[0] + dx, piece.Pivot[1] + dy, piece.Pivot[2] + 2f];
                        var flown = pieces.Pieces[i].Apply(baked.Apply(OpeningScene.FromBlender(point)));
                        var intact = pieces.Ship.Apply(OpeningScene.FromBlender(point));
                        Assert.True(Vector3.Distance(flown, intact) < 0.01f, $"at {t} s {piece.Model} is {Vector3.Distance(flown, intact):0.###} m off at ({point[0]}, {point[1]})");
                    }
            }
            // The housing sits where it was built, and every container rides in its rack slot.
            var centre = pieces.Housing.Apply(OpeningScene.Blender(settings.HousingLying).Position);
            var onShip = pieces.Ship.Apply(OpeningScene.FromBlender(settings.HousingOnShip));
            Assert.True(Vector3.Distance(centre, onShip) < 0.01f, $"at {t} s the housing is {Vector3.Distance(centre, onShip):0.###} m off the ship");
            foreach (var (slot, i) in settings.Containers.Slots.Select((s, i) => (s, i)))
            {
                var middle = pieces.Containers[i].Apply(new Vector3(0, settings.Containers.Middle, 0));
                Assert.True(Vector3.Distance(middle, pieces.Ship.Apply(OpeningScene.FromBlender(slot))) < 0.01f, $"at {t} s container {i} is out of its slot");
            }
        }
    }

    [Fact]
    public void EveryPieceComesToRestExactlyWhereTheWrecksModelsLie()
    {
        var (_, scene) = Vanilla(1234);
        var settings = scene.Settings;

        foreach (var t in new[] { settings.Settle, settings.Settle + 0.5f, settings.Duration })
        {
            var pieces = scene.Pieces(t);
            Assert.Equal(scene.PieceWrecks, pieces.Pieces);
            Assert.Equal(scene.HousingWreck, pieces.Housing);
            Assert.Equal(scene.ContainerWrecks, pieces.Containers);
        }
        Assert.Equal(scene.Wreck, scene.PieceWrecks[0]);
        // Smoothly: no jump into place.
        var before = scene.Pieces(settings.Settle - 0.01f);
        for (var i = 0; i < before.Pieces.Count; i++)
            Assert.True(Vector3.Distance(before.Pieces[i].Position, scene.PieceWrecks[i].Position) < 0.05f);
        Assert.True(Vector3.Distance(scene.Housing(settings.HousingDown - 0.01f).Position, scene.HousingWreck.Position) < 0.2f);
        // The housing leaves the ship without a jump.
        Assert.True(Vector3.Distance(scene.Housing(settings.Explode + 0.01f).Position, scene.Housing(settings.Explode - 0.01f).Position) < 0.6f);
        // The containers break away after the explosion, one by one as the ship passes over, and are all down before it lands.
        Assert.Equal(12, scene.Releases.Count);
        Assert.All(scene.Releases, r => Assert.InRange(r, settings.Explode, settings.Touchdown - settings.Containers.Fall + 1e-3f));
        Assert.True(scene.Releases.Distinct().Count() > 3, "the containers do not all fall at once");
        for (var i = 0; i < scene.Releases.Count; i++)
        {
            var land = scene.Releases[i] + settings.Containers.Fall;
            Assert.True(Vector3.Distance(scene.Container(i, land - 0.01f).Position, scene.ContainerWrecks[i].Position) < 0.3f);
            Assert.True(Vector3.Distance(scene.Container(i, scene.Releases[i] + 0.01f).Position, scene.Container(i, scene.Releases[i] - 0.01f).Position) < 0.6f);
        }
    }

    [Theory]
    [InlineData(1234)]
    [InlineData(20261017)]
    [InlineData(7)]
    public void TheShipAndTheCameraStayAboveTheGroundUntilItLands(long seed)
    {
        var (gen, scene) = Vanilla(seed);
        var settings = scene.Settings;

        for (var t = 0f; t < settings.Black[1]; t += 0.1f)
        {
            if (t < settings.Touchdown - 0.5f)
            {
                // Well clear of the trees until the housing is down, then lower over the clearing.
                var ship = scene.Ship(t);
                var ground = gen.HeightAt(ship.Position.X, ship.Position.Z);
                var clearance = t <= settings.HousingDown ? OpeningScene.Clearance - 0.5f : 0.3f;
                Assert.True(ship.Position.Y - ground > clearance, $"at {t:0.0} s the ship is {ship.Position.Y - ground:0.#} m above the ground");
            }
            var camera = scene.Camera(t);
            Assert.NotNull(camera);
            var under = gen.HeightAt(camera.Value.Position.X, camera.Value.Position.Z);
            Assert.True(camera.Value.Position.Y - under > 1f, $"at {t:0.0} s the camera is {camera.Value.Position.Y - under:0.#} m above the ground");
            Assert.True(Vector3.Distance(camera.Value.Position, camera.Value.Target) > 3f, $"at {t:0.0} s the camera sits on what it looks at");
        }
        // The ship lands inside the clearing, on the pad.
        var landing = scene.Ship(settings.Touchdown).Position;
        Assert.True(Math.Abs(landing.Y - gen.HeightAt(landing.X, landing.Z)) < 1.5f, "it touches down on the ground");
    }

    [Fact]
    public void TheCrashSpillsTheCargoAndThePiecesAreHiddenUntilItLands()
    {
        var (gen, scene) = Vanilla(1234);
        var models = scene.Poi.Poi.Prefab.Models!;

        Assert.Equal(4 + 1 + 12, scene.HiddenModels.Count);
        Assert.All(scene.PieceIndices, i => Assert.Contains(i, scene.HiddenModels));
        Assert.Contains(scene.HousingIndex, scene.HiddenModels);
        Assert.All(scene.ContainerIndices, i => Assert.StartsWith("meshes/structures/sv04_container_", models[i].File, StringComparison.Ordinal));
        var centre = scene.Poi.Centre;
        var near = Enumerable.Range(-2, 5).SelectMany(x => Enumerable.Range(-2, 5).Select(z => new ChunkCoord((int)MathF.Floor(centre.X / 64f) + x, (int)MathF.Floor(centre.Y / 64f) + z)))
            .SelectMany(c => gen.GenerateChunk(c, 64).Objects).ToList();
        var hidden = near.Where(scene.Hides).ToList();
        Assert.Contains(hidden, o => o.Definition.Id == "loot_crate");
        Assert.Contains(hidden, o => o.Definition.Id == "scrap_pile_wood");
        Assert.Contains(hidden, o => o.Definition.Id == "cargo_spill_electronics");
        Assert.DoesNotContain(near, o => o.Definition.Id == "bloom_pod_green" && scene.Hides(o));
        Assert.DoesNotContain(near, o => o.Definition.Id == "burnt_tree" && scene.Hides(o));
    }

    [Fact]
    public void ThePictureFadesUpGoesBlackAndComesBackOnThePlayer()
    {
        var (_, scene) = Vanilla(1234);
        var settings = scene.Settings;

        Assert.Equal(1f, scene.Black(0f));
        Assert.Equal(0f, scene.Black(settings.FadeIn + 0.1f));
        Assert.Equal(0f, scene.Black(settings.Black[0] - 0.1f));
        Assert.Equal(1f, scene.Black(settings.Black[1] - 0.01f), 2);
        Assert.False(scene.OnPlayer(settings.Black[1] - 0.01f));
        Assert.True(scene.OnPlayer(settings.Black[1]));
        Assert.Equal(0f, scene.Black(settings.Black[1] + settings.PlayerFadeIn));
        Assert.True(scene.Shake(settings.Touchdown + 0.1f) > 0f);
        Assert.Equal(0f, scene.Shake(2f));
    }

    [Fact]
    public void BadSettingsAreCaught()
    {
        var bad = Settings with
        {
            Ship = [new OpeningKey { T = 2f }, new OpeningKey { T = 1f }],
            Shots = [new OpeningShot { Start = 1f, End = 2f }],
        };

        var problems = bad.Problems();

        Assert.Contains(problems, p => p.Contains("ship[1]", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("shots[0] starts", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("either from or follow", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("before the picture is black", StringComparison.Ordinal));
    }
}
