using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Characters;

public class CharacterBookTests
{
    /// <summary>A minimal glTF with a skinned mesh over <paramref name="bones"/> and the named empty animations.</summary>
    internal static string Gltf(IEnumerable<string> bones, params (string Name, string[] Targets)[] animations)
    {
        var names = bones.ToList();
        var nodes = names.Select(n => (object)new { name = n }).ToList();
        nodes.Add(new { name = "body", mesh = 0, skin = 0 });
        var anims = animations.Select(a => new
        {
            name = a.Name,
            samplers = new[] { new { input = 0, output = 0 } },
            channels = a.Targets.Select(t => new { sampler = 0, target = new { node = names.IndexOf(t), path = "rotation" } }).ToArray(),
        });
        return JsonSerializer.Serialize(new
        {
            asset = new { version = "2.0" },
            nodes,
            meshes = new[] { new { primitives = Array.Empty<object>() } },
            skins = new[] { new { joints = Enumerable.Range(0, names.Count).ToArray() } },
            accessors = new[] { new { count = 2, type = "SCALAR", componentType = 5126, max = new[] { 0.5f }, min = new[] { 0f } } },
            animations = anims,
        });
    }

    private static TestMods Mods(string character, string? mesh = null)
    {
        var mods = new TestMods().Mod("vanilla").File("vanilla", "characters/hero.json", character);
        if (mesh is not null)
            mods.File("vanilla", "meshes/hero.gltf", mesh);
        return mods;
    }

    [Fact]
    public void ACharacterSkinnedToTheRigLoads()
    {
        using var mods = Mods("""{ "id": "hero", "name": "Hero", "mesh": "meshes/hero.gltf", "sounds": { "jump": "boing" } }""", Gltf(CharacterRig.Bones));
        var book = CharacterBook.Load(mods.Load());
        Assert.Empty(book.Diagnostics);
        var hero = Assert.Single(book.All);
        Assert.Equal("vanilla:hero", hero.QualifiedId);
        Assert.Same(hero, book.Default);
        Assert.Equal("boing", hero.Sound(CharacterSounds.Jump));
        Assert.Equal("footstep_default", hero.Sound(CharacterSounds.Footstep));
        Assert.Same(hero, book.Find("hero"));
    }

    [Fact]
    public void AMeshMissingRigBonesIsRefused()
    {
        using var mods = Mods("""{ "id": "hero", "name": "Hero", "mesh": "meshes/hero.gltf" }""", Gltf(CharacterRig.Bones.Where(b => b != "hand_r")));
        var book = CharacterBook.Load(mods.Load());
        Assert.Empty(book.All);
        Assert.Contains(book.Diagnostics, d => d.Code == DiagnosticCodes.CharacterInvalid && d.Message.Contains("'hand_r'"));
    }

    [Theory]
    [InlineData("""{ "name": "Hero", "mesh": "meshes/hero.gltf" }""", "id is required")]
    [InlineData("""{ "id": "hero", "mesh": "meshes/hero.gltf" }""", "name is required")]
    [InlineData("""{ "id": "hero", "name": "Hero" }""", "mesh is required")]
    [InlineData("""{ "id": "hero", "name": "Hero", "mesh": "../hero.gltf" }""", "..")]
    [InlineData("""{ "id": "hero", "name": "Hero", "mesh": "meshes/gone.gltf" }""", "does not exist")]
    [InlineData("""{ "id": "hero", "name": "Hero", "mesh": "meshes/hero.gltf", "sounds": { "sneeze": "x" } }""", "not a character sound")]
    public void BadCharactersSayWhy(string json, string message)
    {
        using var mods = Mods(json, Gltf(CharacterRig.Bones));
        var book = CharacterBook.Load(mods.Load());
        Assert.Empty(book.All);
        Assert.Contains(book.Diagnostics, d => d.Message.Contains(message));
    }

    [Fact]
    public void PickerOrderPutsLowerOrderFirst()
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", "meshes/a.gltf", Gltf(CharacterRig.Bones))
            .File("vanilla", "characters/a.json", """{ "id": "a", "name": "A", "mesh": "meshes/a.gltf", "order": 5 }""")
            .File("vanilla", "characters/b.json", """{ "id": "b", "name": "B", "mesh": "meshes/a.gltf", "order": 1 }""");
        var book = CharacterBook.Load(mods.Load());
        Assert.Equal(["vanilla:b", "vanilla:a"], book.All.Select(c => c.QualifiedId));
    }

    [Fact]
    public void ChoosingIsOncePerPlayerAndSurvivesRestore()
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", "meshes/a.gltf", Gltf(CharacterRig.Bones))
            .File("vanilla", "characters/a.json", """{ "id": "a", "name": "A", "mesh": "meshes/a.gltf" }""")
            .File("vanilla", "characters/b.json", """{ "id": "b", "name": "B", "mesh": "meshes/a.gltf", "order": 1 }""");
        var choices = new CharacterActions(CharacterBook.Load(mods.Load()));
        var dispatcher = new ActionDispatcher();
        choices.Register(dispatcher);
        var changes = new List<(int, string?)>();
        choices.Changed += (peer, id) => changes.Add((peer, id));

        Assert.Equal("vanilla:a", choices.ShownAs(2)!.QualifiedId);
        Assert.False(dispatcher.Dispatch(new ActionCaller(2, ActionSource.Player), CharacterActions.ChooseCharacter, """{ "character": "nobody" }""").Ok);
        Assert.True(dispatcher.Dispatch(new ActionCaller(2, ActionSource.Player), CharacterActions.ChooseCharacter, """{ "character": "b" }""").Ok);
        Assert.Equal("vanilla:b", choices.ChosenBy(2));
        var again = dispatcher.Dispatch(new ActionCaller(2, ActionSource.Player), CharacterActions.ChooseCharacter, """{ "character": "a" }""");
        Assert.False(again.Ok);
        Assert.Contains("already", again.Message);
        Assert.False(dispatcher.Dispatch(new ActionCaller(3, ActionSource.Bot), CharacterActions.ChooseCharacter, """{ "character": "a" }""").Ok);

        choices.Forget(2);
        Assert.Null(choices.ChosenBy(2));
        choices.Restore(2, "vanilla:b");
        Assert.Equal("vanilla:b", choices.ChosenBy(2));
        choices.Restore(4, "gone_mod:someone");
        Assert.Null(choices.ChosenBy(4));
        Assert.Equal([(2, "vanilla:b"), (2, "vanilla:b"), (4, null)], changes);
    }

    [Fact]
    public void GltfSummaryReadsBinaryFiles()
    {
        var json = System.Text.Encoding.UTF8.GetBytes(Gltf(CharacterRig.Bones, ("idle", ["hips"])));
        var padded = json.Concat(Enumerable.Repeat((byte)' ', (4 - json.Length % 4) % 4)).ToArray();
        var glb = new byte[20 + padded.Length];
        BitConverter.GetBytes(0x46546C67u).CopyTo(glb, 0);
        BitConverter.GetBytes(2u).CopyTo(glb, 4);
        BitConverter.GetBytes((uint)glb.Length).CopyTo(glb, 8);
        BitConverter.GetBytes((uint)padded.Length).CopyTo(glb, 12);
        BitConverter.GetBytes(0x4E4F534Au).CopyTo(glb, 16);
        padded.CopyTo(glb, 20);
        var summary = GltfSummary.Parse(glb);
        Assert.Empty(summary.MissingBones());
        Assert.Equal(1, summary.SkinnedMeshes);
        var idle = Assert.Single(summary.Animations);
        Assert.Equal("idle", idle.Name);
        Assert.Equal(0.5f, idle.Duration);
        Assert.Equal(["hips"], idle.Targets);
        Assert.Throws<FormatException>(() => GltfSummary.Parse("{ nope"u8));
    }
}
