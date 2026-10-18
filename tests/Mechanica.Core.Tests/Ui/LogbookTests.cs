using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Quests;
using Mechanica.Core.Ui;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.Ui;

public class LogbookTests
{
    private static readonly ActionCaller Player = new(2, ActionSource.Player);
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));
    private static readonly Lazy<WorldGenData> World = new(() => WorldGenData.Load(Vanilla.Value));
    private static readonly Lazy<QuestBook> Book = new(() => QuestBook.Load(Vanilla.Value, RecipeBook.Load(Vanilla.Value), World.Value));

    [Fact]
    public void TheShippedSettingsRead()
    {
        var settings = LogbookSettings.Parse(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "logbook.json")));
        Assert.True(settings.MaxMarkers >= 1);
        Assert.True(settings.ZoomStep > 1);
    }

    [Theory]
    [InlineData("""{ "zoom_step": 1 }""", "zoom_step")]
    [InlineData("""{ "map_resolution": 4 }""", "map_resolution")]
    [InlineData("""{ "max_markers": 0 }""", "max_markers")]
    [InlineData("""{ "pick_radius": 0 }""", "pick_radius")]
    public void BadSettingsSayWhatIsWrong(string json, string field) =>
        Assert.Contains(field, Assert.Throws<FormatException>(() => LogbookSettings.Parse(json)).Message);

    [Fact]
    public void TheMapStartsWithTheWholeWorldAndPansWithTheDrag()
    {
        var view = new MapView(4096, 0.25f);
        var size = new Vector2(800, 400);
        view.Fit(size);
        Assert.Equal(4096f / 400f, view.MetresPerPixel, 3);
        Assert.Equal(size / 2, view.ToScreen(Vector2.Zero, size));
        // Dragging right moves the ground right, so the centre moves west.
        view.Pan(new Vector2(10, 0));
        Assert.True(view.Centre.X < 0);
        Assert.Equal(new Vector2(410, 200), view.ToScreen(Vector2.Zero, size), new Vec2Comparer());
        // It never leaves the world.
        view.Pan(new Vector2(100000, -100000));
        Assert.Equal(new Vector2(-2048, 2048), view.Centre);
    }

    [Fact]
    public void AWideWorldFitsByItsLongerSide()
    {
        var view = new MapView(12000, 9000, 0.25f);
        var size = new Vector2(800, 800);
        view.Fit(size);
        Assert.Equal(12000f / 800f, view.MetresPerPixel, 3);
        view.Pan(new Vector2(-100000, -100000));
        Assert.Equal(new Vector2(6000, 4500), view.Centre);
    }

    [Fact]
    public void ZoomKeepsTheGroundUnderTheCursorAndStaysInItsLimits()
    {
        var view = new MapView(4096, 0.25f);
        var size = new Vector2(500, 500);
        view.Fit(size);
        var cursor = new Vector2(400, 100);
        var under = view.ToWorld(cursor, size);
        view.ZoomAt(cursor, size, 3, 1.25f);
        Assert.Equal(4096f / 500f / MathF.Pow(1.25f, 3), view.MetresPerPixel, 3);
        Assert.Equal(cursor, view.ToScreen(under, size), new Vec2Comparer());
        view.ZoomAt(cursor, size, 100, 1.25f);
        Assert.Equal(0.25f, view.MetresPerPixel);
        view.ZoomAt(cursor, size, -100, 1.25f);
        Assert.Equal(view.MaxMetresPerPixel(size), view.MetresPerPixel);
    }

    [Fact]
    public void ResizingKeepsThePanAndZoom()
    {
        var view = new MapView(4096, 0.25f);
        view.Fit(new Vector2(500, 500));
        view.ZoomAt(new Vector2(250, 250), new Vector2(500, 500), 5, 1.25f);
        view.CentreOn(new Vector2(300, -200));
        var zoom = view.MetresPerPixel;
        view.Resized(new Vector2(900, 700));
        Assert.Equal((zoom, new Vector2(300, -200)), (view.MetresPerPixel, view.Centre));
    }

    [Fact]
    public void ActiveQuestsComeFirstAndFinishedOnesAreMarkedDone()
    {
        var book = Book.Value;
        Assert.True(book.All.Count >= 2);
        var (first, second) = (book.All[0], book.All[1]);
        var state = new QuestState();
        state.Quests[first.QualifiedId] = new QuestProgress { Status = QuestStatus.Done, Step = first.Steps.Count };
        state.Quests[second.QualifiedId] = new QuestProgress { Status = QuestStatus.Active, Count = 1 };

        var entries = Logbook.Quests(book, state, id => book.Find(id)!.Steps[0]);

        Assert.Equal([second.QualifiedId, first.QualifiedId], entries.Select(e => e.Quest.QualifiedId));
        Assert.False(entries[0].Done);
        Assert.Equal(second.Steps[0], entries[0].Step);
        Assert.True(entries[1].Done);
        Assert.Equal($"quest:{second.QualifiedId}", entries[0].WaypointId);
    }

    [Fact]
    public void TheMapIsBlueWithSeaAroundTheIsland()
    {
        var world = new WorldGenerator(World.Value, 1234, 1024);
        const int size = 64;
        var pixels = WorldMap.Render(world, new Vector2(-640, -640), 1280f / size, size, size, 10f);
        Assert.Equal(size * size * 3, pixels.Length);
        // The corner is off the island: deep sea.
        Assert.Equal((pixels[0], pixels[1], pixels[2]), WorldMapCorner());
        // Every pixel is a blue: blue at least as strong as red, never orange.
        for (var i = 0; i < pixels.Length; i += 3)
        {
            var (r, g, b) = (pixels[i] / 255f, pixels[i + 1] / 255f, pixels[i + 2] / 255f);
            Assert.True(b >= r && b >= g, $"pixel {i / 3} is ({r}, {g}, {b})");
            Assert.False(UiPalette.IsOrange(r, g, b));
        }
        // The middle is land, lighter than the sea.
        var mid = (size / 2 * size + size / 2) * 3;
        Assert.True(pixels[mid + 2] > pixels[2]);
    }

    private static (byte, byte, byte) WorldMapCorner()
    {
        var (r, g, b) = UiPalette.Parse(UiPalette.DeepNavy);
        return ((byte)MathF.Round(r * 255), (byte)MathF.Round(g * 255), (byte)MathF.Round(b * 255));
    }

    [Fact]
    public void MarkersAreThePlayersOwnAndLimited()
    {
        var dispatcher = new ActionDispatcher();
        var markers = new MapMarkers(new LogbookSettings { MaxMarkers = 2, MarkerNameLength = 8 }, 1000);
        markers.Register(dispatcher);
        var changed = new List<int>();
        markers.Changed += (peer, _) => changed.Add(peer);

        Assert.True(dispatcher.Dispatch(Player, MapMarkers.AddMapMarker, new AddMapMarkerArgs(10, 2, -20)).Ok);
        var named = dispatcher.Dispatch(Player, MapMarkers.AddMapMarker, new AddMapMarkerArgs(-5, 1, 5, "  Base camp north  "));
        Assert.True(named.Ok);
        var list = named.Value!.Value.Deserialize<MapMarkerList>(ActionJson.Options)!.Markers;
        Assert.Equal([new MapMarker("marker:1", "Marker 1", 10, 2, -20), new MapMarker("marker:2", "Base cam", -5, 1, 5)], list);
        Assert.Equal([2, 2], changed);

        var full = dispatcher.Dispatch(Player, MapMarkers.AddMapMarker, new AddMapMarkerArgs(0, 0, 0));
        Assert.Contains("at most 2", full.Message);
        Assert.Contains("inside the world", dispatcher.Dispatch(ActionCaller.HostPlayer, MapMarkers.AddMapMarker, new AddMapMarkerArgs(600, 0, 0)).Message);
        Assert.Contains("inside the world", dispatcher.Dispatch(ActionCaller.HostPlayer, MapMarkers.AddMapMarker, new AddMapMarkerArgs(float.NaN, 0, 0)).Message);
        Assert.False(dispatcher.Dispatch(new ActionCaller(5, ActionSource.Bot), MapMarkers.AddMapMarker, new AddMapMarkerArgs(0, 0, 0)).Ok);
        // Each player has their own.
        Assert.Empty(markers.MarkersOf(1));

        Assert.True(dispatcher.Dispatch(Player, MapMarkers.RemoveMapMarker, new RemoveMapMarkerArgs("marker:1")).Ok);
        Assert.Contains("no marker", dispatcher.Dispatch(Player, MapMarkers.RemoveMapMarker, new RemoveMapMarkerArgs("marker:1")).Message);
        // Numbers are not reused while a later marker exists.
        Assert.True(dispatcher.Dispatch(Player, MapMarkers.AddMapMarker, new AddMapMarkerArgs(1, 1, 1)).Ok);
        Assert.Equal(["marker:2", "marker:3"], markers.MarkersOf(2).Select(m => m.Id));
        Assert.Equal(markers.MarkersOf(2), markers.SavedOf(2));
        Assert.Null(markers.SavedOf(1));
        Assert.True(dispatcher.IsReadOnly(MapMarkers.GetMapMarkers));
    }

    [Fact]
    public void RestoringDropsBadMarkersAndKeepsTheLimit()
    {
        var markers = new MapMarkers(new LogbookSettings { MaxMarkers = 2 }, 1000);
        markers.Restore(2,
        [
            new MapMarker("marker:1", "A", 0, 0, 0),
            new MapMarker("elsewhere", "B", 0, 0, 0),
            new MapMarker("marker:3", "C", 9000, 0, 0),
            new MapMarker("marker:1", "D", 0, 0, 0),
            new MapMarker("marker:4", "E", 1, 0, 1),
            new MapMarker("marker:5", "F", 2, 0, 2),
        ]);
        Assert.Equal(["marker:1", "marker:4"], markers.MarkersOf(2).Select(m => m.Id));
        Assert.Equal(new Waypoint("marker:4", "E", new Vector3(1, 0, 1)), markers.MarkersOf(2)[1].ToWaypoint());
        markers.Forget(2);
        Assert.Empty(markers.MarkersOf(2));
    }

    [Fact]
    public void PagesTurnAndStopAtTheEnds()
    {
        string[] pages = ["a.md", "b.md", "c.md"];
        Assert.Equal("b.md", Handbook.Turn(pages, "a.md", 1));
        Assert.Equal("c.md", Handbook.Turn(pages, "c.md", 1));
        Assert.Equal("a.md", Handbook.Turn(pages, "a.md", -1));
        Assert.Equal("a.md", Handbook.Turn(pages, "hidden.md", 1));
        Assert.Null(Handbook.Turn([], "a.md", 1));
        Assert.Equal("2 / 3", Handbook.Position(pages, "b.md"));
        Assert.Equal("", Handbook.Position(pages, "hidden.md"));
    }

    [Fact]
    public void DiagramsOnTheirOwnLineSplitThePage()
    {
        const string page = "# Title\n\nText before.\n\n![The HUD](images/hud.svg)\n\nText after.\n```\n![not a picture](images/x.svg)\n```\n![Escape](../secret.png)\n";
        var blocks = Handbook.Split(page);
        Assert.Equal(3, blocks.Count);
        Assert.Equal("# Title\n\nText before.\n", blocks[0].Markdown);
        Assert.Equal(new HandbookBlock(null, "images/hud.svg", "The HUD"), blocks[1]);
        Assert.Contains("![not a picture]", blocks[2].Markdown);
        // An unsafe path stays as text.
        Assert.Contains("../secret.png", blocks[2].Markdown);
    }

    [Theory]
    [InlineData("images/a.svg", "images/a.svg")]
    [InlineData("images\\b.PNG", "images/b.PNG")]
    [InlineData("../a.svg", null)]
    [InlineData("/etc/a.png", null)]
    [InlineData("C:/a.png", null)]
    [InlineData("https://example.com/a.png", null)]
    [InlineData("images/a.md", null)]
    public void DiagramPathsStayInTheDocs(string path, string? safe) => Assert.Equal(safe, Handbook.SafeImagePath(path));

    private sealed class Vec2Comparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 1e-2f;

        public int GetHashCode(Vector2 v) => 0;
    }
}
