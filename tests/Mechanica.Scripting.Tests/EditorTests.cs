using System.Numerics;
using Mechanica.Scripting.Editor;

namespace Mechanica.Scripting.Tests;

public sealed class EditorTests : IDisposable
{
    private static readonly string TemplatesDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "data", "lua_templates");

    private readonly ScriptRig _rig = new();

    public void Dispose() => _rig.Dispose();

    public static TheoryData<string> TemplateIds() => [.. LuaTemplates.Load(TemplatesDir).Select(t => t.Id)];

    [Fact]
    public void TemplatesLoadWithTitlesAndEmptyFirst()
    {
        var templates = LuaTemplates.Load(TemplatesDir);

        Assert.True(templates.Count >= 5);
        Assert.Equal("empty", templates[0].Id);
        Assert.All(templates, t => Assert.False(string.IsNullOrWhiteSpace(t.Title)));
        Assert.Contains("Rocket", templates.First(t => t.Id == "thruster").For("Rocket"));
        Assert.Equal("scripts/rocket.lua", LuaTemplates.PathFor("rocket"));
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void EveryTemplateCompilesAndRuns(string id)
    {
        var template = LuaTemplates.Load(TemplatesDir).Single(t => t.Id == id);
        Assert.Null(LuaSyntax.Check(template.For("Test")));
        _rig.Write("scripts/thruster.lua", template.For("Test"));
        var creation = _rig.AddCreation(1, ScriptRig.Thruster);
        creation.SetBodyVelocity(0, new Core.Creations.BodyVelocity(new Vector3(10, 0, 0), Vector3.Zero));

        for (var i = 0; i < 5; i++)
            _rig.Runtime.Update(1 / 60f);
        _rig.World.Creations.Remove(1);
        _rig.Runtime.Update(1 / 60f);

        Assert.Empty(_rig.Errors);
    }

    [Fact]
    public void TheThrusterTemplatePushesTowardItsBase()
    {
        _rig.Write("scripts/thruster.lua", LuaTemplates.Load(TemplatesDir).Single(t => t.Id == "thruster").For("Test"));
        _rig.AddCreation(1, ScriptRig.Thruster);

        _rig.Runtime.Update(1 / 60f);

        Assert.Equal(new Vector3(0, -50, 0), Assert.Single(_rig.World.Pushes).Force);
    }

    [Theory]
    [InlineData("function on_update(dt)\n  local x = = 1\nend", 2, "unexpected symbol near '='")]
    [InlineData("function on_update(dt)\n  if x then\nend", 3, "'end' expected")]
    public void SyntaxErrorsPointAtTheLine(string source, int line, string message)
    {
        var error = LuaSyntax.Check(source);

        Assert.NotNull(error);
        Assert.Equal(line, error.Line);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void GoodScriptsPassTheCheck() => Assert.Null(LuaSyntax.Check("function on_update(dt) return _ENV end"));

    [Fact]
    public void MessagesWithoutAPlaceAreOnLineOne() => Assert.Equal(new LuaSyntaxError(1, 0, "oops"), LuaSyntax.Parse("oops"));
}
