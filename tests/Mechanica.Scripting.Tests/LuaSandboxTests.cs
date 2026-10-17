using System.Text.Json.Nodes;
using Mechanica.Scripting.Editor;

namespace Mechanica.Scripting.Tests;

public class LuaSandboxTests
{
    private static readonly ScriptSettings Settings = new() { InstructionBudget = 10_000 };

    [Fact]
    public void AnExpressionReturnsItsValue()
    {
        var result = LuaSandbox.Eval("1 + 2", Settings);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(3, Assert.Single(result.Values)!.GetValue<double>());
    }

    [Fact]
    public void AChunkRunsWithPrintsAndTheVec3Prelude()
    {
        var result = LuaSandbox.Eval("""
            local v = vec3(3, 4, 0)
            print("length", v:length())
            return { len = v:length(), parts = { 1, 2 } }, function() end
            """, Settings);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(["length\t5"], result.Output);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "len": 5, "parts": [1, 2] }"""), result.Values[0]));
        Assert.Equal("<function>", result.Values[1]!.GetValue<string>());
    }

    [Fact]
    public void ErrorsAndEndlessLoopsStopTheChunk()
    {
        var error = LuaSandbox.Eval("print('before') error('boom')", Settings);
        var loop = LuaSandbox.Eval("while true do end", Settings);
        var syntax = LuaSandbox.Eval("if then", Settings);

        Assert.False(error.Ok);
        Assert.Contains("boom", error.Error);
        Assert.Equal(["before"], error.Output);
        Assert.Contains("10000 Lua instructions", loop.Error);
        Assert.StartsWith("line 1:", syntax.Error);
    }

    [Fact]
    public void TheSandboxHasNoFilesShellOrWorld()
    {
        var result = LuaSandbox.Eval("return io == nil, os == nil or os.execute == nil, dofile == nil, loadfile == nil, require == nil, world == nil", Settings);

        Assert.True(result.Ok, result.Error);
        Assert.All(result.Values, v => Assert.True(v!.GetValue<bool>()));
    }

    [Fact]
    public void EachEvalStartsFresh()
    {
        LuaSandbox.Eval("leftover = 1", Settings);

        Assert.Null(Assert.Single(LuaSandbox.Eval("leftover", Settings).Values));
    }

    [Fact]
    public void SyntaxProblemsUseTheModToolShape()
    {
        Assert.Null(LuaSyntax.Problem("function on_update(dt) end"));
        Assert.Equal(2, LuaSyntax.Problem("local x = 1\nif then")!.Line);
    }
}
