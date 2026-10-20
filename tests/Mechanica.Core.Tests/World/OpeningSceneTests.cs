using System.Numerics;
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
    public void TheIntactShipIsTheWrecksSectionsPutBackTogether()
    {
        var (_, scene) = Vanilla(1234);
        var settings = scene.Settings;
        var aftWreck = OpeningScene.Blender(settings.AftWreck);
        var foreWreck = OpeningScene.Blender(settings.ForeWreck);
        foreach (var t in new[] { 0f, 6f, 11.9f })
        {
            var pieces = scene.Pieces(t);
            // The aft section's torn end (its y 6 in ship.py) meets the fore section's (its y -8), and the fore section flies level.
            foreach (var x in new[] { -3f, 0f, 3f })
                foreach (var z in new[] { 1f, 5f })
                {
                    var aftEnd = pieces.Aft.Apply(aftWreck.Apply(OpeningScene.FromBlender([x, 6f, z])));
                    var foreEnd = pieces.Fore.Apply(foreWreck.Apply(OpeningScene.FromBlender([x, -8f, z])));
                    Assert.True(Vector3.Distance(aftEnd, foreEnd) < 0.01f, $"at {t} s the ends at ({x}, {z}) are {Vector3.Distance(aftEnd, foreEnd):0.###} m apart");
                    Assert.True(Vector3.Distance(foreEnd, pieces.Ship.Apply(OpeningScene.FromBlender([x, -8f, z]))) < 0.01f);
                }
            // The nacelle hangs on the aft section where ship.py builds it.
            var centre = pieces.Nacelle.Apply(OpeningScene.Blender(settings.NacelleLying).Position);
            var onAft = pieces.Aft.Apply(aftWreck.Apply(OpeningScene.FromBlender(settings.NacelleOnAft)));
            Assert.True(Vector3.Distance(centre, onAft) < 0.01f, $"at {t} s the nacelle is {Vector3.Distance(centre, onAft):0.###} m off its pylon");
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
            Assert.Equal(scene.Wreck, pieces.Fore);
            Assert.Equal(scene.AftWreck, pieces.Aft);
            Assert.Equal(scene.NacelleWreck, pieces.Nacelle);
        }
        // Smoothly: no jump into place.
        var before = scene.Pieces(settings.Settle - 0.01f);
        Assert.True(Vector3.Distance(before.Fore.Position, scene.Wreck.Position) < 0.05f);
        Assert.True(Vector3.Distance(before.Aft.Position, scene.AftWreck.Position) < 0.05f);
        Assert.True(Vector3.Distance(scene.Nacelle(settings.NacelleDown - 0.01f).Position, scene.NacelleWreck.Position) < 0.2f);
        // The nacelle leaves the ship without a jump.
        Assert.True(Vector3.Distance(scene.Nacelle(settings.NacelleOff + 0.01f).Position, scene.Nacelle(settings.NacelleOff - 0.01f).Position) < 0.6f);
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
                // Well clear of the trees until the nacelle is down, then lower over the clearing.
                var ship = scene.Ship(t);
                var ground = gen.HeightAt(ship.Position.X, ship.Position.Z);
                var clearance = t <= settings.NacelleDown ? OpeningScene.Clearance - 0.5f : 0.3f;
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
    public void TheCrashSpillsTheCargoAndTheSectionsAreHiddenUntilItLands()
    {
        var (gen, scene) = Vanilla(1234);
        var models = scene.Poi.Poi.Prefab.Models!;

        Assert.Equal(3 + models.Count(m => m.File == "meshes/structures/cargo_crate_colony.glb"), scene.HiddenModels.Count);
        Assert.Contains(scene.ForeIndex, scene.HiddenModels);
        Assert.Contains(scene.NacelleIndex, scene.HiddenModels);
        var centre = scene.Poi.Centre;
        var near = Enumerable.Range(-2, 5).SelectMany(x => Enumerable.Range(-2, 5).Select(z => new ChunkCoord((int)MathF.Floor(centre.X / 64f) + x, (int)MathF.Floor(centre.Y / 64f) + z)))
            .SelectMany(c => gen.GenerateChunk(c, 64).Objects).ToList();
        var hidden = near.Where(scene.Hides).ToList();
        Assert.Contains(hidden, o => o.Definition.Id == "loot_crate");
        Assert.Contains(hidden, o => o.Definition.Id == "scrap_pile_wood");
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
