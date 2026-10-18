using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Saves;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Building;

public sealed class BlueprintTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly SurvivalFixture _f = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mechanica-blueprints-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    // ---- Files ----

    [Fact]
    public void ABlueprintKeepsTheShapeAndLeavesTheWorldBehind()
    {
        var creation = Cart(7);
        creation.SetPartColor(1, "#3070d0");
        creation.SetPartDamage(1, 1.5f);
        creation.InventoryOf(2)!.Add(Scrap, 5);
        creation.SetAnchored(1, true);
        creation.OnLift = true;

        var json = Blueprint.Write(creation, "  My cart\n ", Noon);

        Assert.DoesNotContain("anchored", json);
        Assert.DoesNotContain("damage", json);
        Assert.DoesNotContain("inventory", json);
        Assert.DoesNotContain("on_lift", json);
        var info = Blueprint.Peek(json, "cart.json", [])!;
        Assert.Equal(new BlueprintInfo("My cart", Noon, 2), info);
        var diagnostics = new List<DataDiagnostic>();
        var read = Blueprint.Read(json, _f.Catalog, 42, "cart.json", diagnostics);
        Assert.NotNull(read);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(42, read.Id);
        Assert.Equal(Creation.HostPeer, read.AuthorityPeer);
        Assert.False(read.OnLift);
        Assert.Equal("#3070d0", read.FindPart(1)!.Color);
        Assert.Equal(0f, read.PartDamage(1));
        Assert.False(read.HoldsItems(2));
        Assert.Equal([Block, Chest], read.Parts.Select(p => p.Part));
    }

    [Fact]
    public void ReadingIgnoresWorldStateAFileStillHas()
    {
        var creation = Cart(3);
        creation.InventoryOf(2)!.Add(Scrap, 5);
        var file = $$"""{ "format_version": 1, "name": "Cart", "creation": {{CreationJson.Write(creation)}} }""";

        var read = Blueprint.Read(file, _f.Catalog, 9, "cart.json", [])!;

        Assert.False(read.HoldsItems(2));
    }

    [Theory]
    [InlineData("""{ "name": "x", "creation": {} }""", "format_version is required.")]
    [InlineData("""{ "format_version": 2, "name": "x", "creation": {} }""", "format_version 2 is not supported; this build reads 1.")]
    [InlineData("""{ "format_version": 1, "creation": {} }""", "name is required.")]
    [InlineData("""{ "format_version": 1, "name": "x" }""", "creation is required.")]
    [InlineData("""{ "format_version": 1, "name": "x", "saved_at": "soon", "creation": {} }""", "saved_at must be a date and time like \"2026-10-17T12:00:00+00:00\".")]
    [InlineData("""[1]""", "a blueprint is a JSON object.")]
    public void BadFilesSayWhy(string json, string message)
    {
        var diagnostics = new List<DataDiagnostic>();

        Assert.Null(Blueprint.Peek(json, "bad.json", diagnostics));

        Assert.Equal(message, diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message);
    }

    [Fact]
    public void NamesAreTrimmedShortenedAndNeverEmpty()
    {
        Assert.Equal("Blueprint", Blueprint.CleanName("   "));
        Assert.Equal(Blueprint.MaxNameLength, Blueprint.CleanName(new string('a', 80)).Length);
        Assert.Equal("ab", Blueprint.CleanName("a\tb"));
    }

    [Fact]
    public void TheStoreListsNewestFirstUpToItsLimit()
    {
        var store = new BlueprintStore(_folder, limit: 2);
        store.Write(Cart(1), "Old", Noon.AddHours(-2));
        store.Write(Cart(1), "Middle", Noon.AddHours(-1));
        var newest = store.Write(Cart(1), "New", Noon);
        File.WriteAllText(BlueprintStore.ThumbnailFor(newest), "png");

        var list = store.List();

        Assert.Equal(["New", "Middle"], list.Select(b => b.Name));
        Assert.Equal(BlueprintStore.ThumbnailFor(newest), list[0].Thumbnail);
        Assert.Null(list[1].Thumbnail);
        Assert.Equal(2, list[0].Parts);
        Assert.Equal(Path.Combine(_folder, "new.json"), store.PathFor(" New "));
        Assert.True(store.Exists("new"));
    }

    [Fact]
    public void SavingUnderAnExistingNameReplacesIt()
    {
        var store = new BlueprintStore(_folder);
        store.Write(Cart(1), "Cart", Noon.AddHours(-1));
        store.Write(Cart(1), "Cart", Noon);

        Assert.Equal([Noon], store.List().Select(b => b.SavedAt));
    }

    [Fact]
    public void BrokenFilesAreListedWithTheirError()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "broken.json"), "{");

        var broken = Assert.Single(new BlueprintStore(_folder).List());

        Assert.Equal("broken", broken.Name);
        Assert.NotNull(broken.Error);
    }

    [Fact]
    public void DeletingTakesThePictureAndStaysInTheFolder()
    {
        var store = new BlueprintStore(_folder);
        var path = store.Write(Cart(1), "Cart", Noon);
        File.WriteAllText(BlueprintStore.ThumbnailFor(path), "png");
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(outside, "{}");
        try
        {
            Assert.False(store.Delete(outside));
            Assert.Null(store.Read(outside));
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }

        Assert.NotNull(store.Read(path));
        Assert.True(store.Delete(path));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(BlueprintStore.ThumbnailFor(path)));
        Assert.Empty(store.List());
    }

    // ---- LoadBlueprint ----

    [Fact]
    public void LoadingNeedsAnEmptyLift()
    {
        var blueprint = Json(Blueprint.Write(Cart(1), "Cart", Noon));
        Assert.Equal("You have no lift to load a blueprint onto.", _f.Refused(Me, ToolActions.LoadBlueprint, new { blueprint }));

        var held = _f.Add(Block, new Vector3(0, 0.125f, 1));
        _f.Ok(Me, ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 1f }, creation = held });

        Assert.Equal("Your lift already holds a creation.", _f.Refused(Me, ToolActions.LoadBlueprint, new { blueprint }));
    }

    [Fact]
    public void ABlueprintIsPaidForPartByPartInSurvival()
    {
        _f.Ok(Me, ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 1f } });
        var blueprint = Json(Blueprint.Write(Cart(1), "Cart", Noon));
        Assert.Equal("You need 1 Block and have 0.", _f.Refused(Me, ToolActions.LoadBlueprint, new { blueprint }));

        _f.Pockets(Me).Add(Block, 1);
        _f.Pockets(Me).Add(Chest, 2);
        var lift = _f.Ok<LiftResult>(Me, ToolActions.LoadBlueprint, new { blueprint });

        var loaded = _f.World.FindCreation(lift.Creation!.Value)!;
        Assert.True(loaded.OnLift);
        Assert.Equal(lift.Creation, _f.Tools.LiftOf(Me.Peer)!.Creation);
        Assert.Equal(0, _f.Pockets(Me).Count(Block));
        Assert.Equal(1, _f.Pockets(Me).Count(Chest));
        // Seated on the platform, centred over it.
        var box = CreationBounds.World(loaded);
        Assert.Equal(new BuildSettings().Lift.BaseHeight, box.Min.Y, 1e-4f);
        Assert.Equal(1f, (box.Min.Z + box.Max.Z) / 2, 1e-4f);
    }

    [Fact]
    public void CreativeLoadsForFree()
    {
        var creative = new SurvivalFixture(mode: GameMode.Creative);
        creative.Ok(Me, ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 1f } });

        var lift = creative.Ok<LiftResult>(Me, ToolActions.LoadBlueprint, new { blueprint = Json(Blueprint.Write(Cart(1), "Cart", Noon)) });

        Assert.NotNull(lift.Creation);
    }

    [Fact]
    public void ABadBlueprintSaysWhy()
    {
        _f.Ok(Me, ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 1f } });
        var unknown = Blueprint.Write(Cart(1), "Cart", Noon).Replace(Chest, "test:nothing");

        Assert.StartsWith("The blueprint does not load: ", _f.Refused(Me, ToolActions.LoadBlueprint, new { blueprint = Json(unknown) }));
        Assert.Equal("That is not a blueprint.", _f.Refused(Me, ToolActions.LoadBlueprint, new { blueprint = 3 }));
    }

    [Fact]
    public void BlueprintsMayBeLongerThanOtherArguments()
    {
        Assert.Equal(new BuildSettings().Lift.BlueprintMaxBytes, _f.Dispatcher.MaxArgsOf(ToolActions.LoadBlueprint));
        Assert.Equal(ActionJson.MaxArgsLength, _f.Dispatcher.MaxArgsOf(ToolActions.PlaceLift));
    }

    // ---- Picking up ----

    [Fact]
    public void ACreationWithStoredItemsCannotBeLifted()
    {
        var cart = Cart(_f.World.NextCreationId());
        cart.SetAllBodyPoses(new Pose(new Vector3(0, 0.125f, 1), Quaternion.Identity));
        _f.World.Added(cart);
        cart.InventoryOf(2)!.Add(Scrap, 1);

        Assert.Equal(ToolActions.StoredItemsRefusal, _f.Refused(Me, ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 1f }, creation = cart.Id }));

        cart.InventoryOf(2)!.Clear();
        _f.Ok(Me, ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 1f }, creation = cart.Id });
        // Already on the Lift, filling it later does not drop it from the next move.
        cart.InventoryOf(2)!.Add(Scrap, 1);
        _f.Ok(Me, ToolActions.PlaceLift, new { position = new[] { 0.5f, 0f, 1f }, creation = cart.Id });
    }

    // ---- Blocking ----

    [Fact]
    public void BlockingIsCreativeOnly()
    {
        // The spec: no block in Survival, so a hit always lands in full.
        Assert.Equal("Blocking with the Sledgehammer is only in Creative.", _f.Refused(You, ToolActions.Block, new { on = true }));
        Assert.Empty(_f.Tools.Blocking);
        _f.Ok(You, ToolActions.Block, new { on = false });
        _f.Ok(Me, ToolActions.Sledge, new { player = You.Peer });
        Assert.Equal(100f - 20f, _f.Survival.View(You.Peer)!.Health, 3);
    }

    [Fact]
    public void ACreativeBlockIsOnlyAPose()
    {
        var creative = new SurvivalFixture(mode: GameMode.Creative);
        creative.Ok(Me, ToolActions.Block, new { on = true });
        Assert.Contains(Me.Peer, creative.Tools.Blocking);
        creative.Ok(Me, ToolActions.Block, new { on = false });
        Assert.Empty(creative.Tools.Blocking);
        creative.Ok(Me, ToolActions.Block, new { on = true });
        creative.Tools.Forget(Me.Peer);
        Assert.Empty(creative.Tools.Blocking);
    }

    [Fact]
    public void OnlyAPlayerCanBlock()
    {
        var ghost = new ActionCaller(9, ActionSource.Player);

        Assert.Equal("You have no player to block with.", _f.Refused(ghost, ToolActions.Block, new { on = true }));
        _f.Ok(ghost, ToolActions.Block, new { on = false });
    }

    // A block with a chest on its +x side.
    private Creation Cart(int id)
    {
        var creation = new Creation(id, _f.Catalog);
        Assert.True(creation.TryPlacePart(Block, GridCell.Zero, default, out _, out var error), error);
        Assert.True(creation.TryPlacePart(Chest, new GridCell(1, 0, 0), default, out _, out error), error);
        creation.SetAllBodyPoses(new Pose(new Vector3(5, 0.125f, 5), Quaternion.Identity));
        return creation;
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
