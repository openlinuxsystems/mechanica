namespace Mechanica.Scripting.Tests;

public sealed class LuaApiTests : IDisposable
{
    private static readonly string Doc = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "lua-api.md"));

    private readonly ScriptRig _rig = new();

    public void Dispose() => _rig.Dispose();

    public static TheoryData<string> Signatures() => [.. LuaApi.Entries.Select(e => e.Signature)];

    [Theory]
    [MemberData(nameof(Signatures))]
    public void TheReferenceDocumentsEveryEntry(string signature) =>
        Assert.Contains($"`{signature.Replace("function ", "", StringComparison.Ordinal)}", Doc);

    [Fact]
    public void EveryGlobalEntryExistsForScripts()
    {
        var names = LuaApi.Entries.Where(e => e.Group is "part" or "creation" or "world" or "Globals" && e.Name is not ("state" or "properties"))
            .Select(e => e.Name).Concat(LuaApi.Libraries).ToList();
        var checks = string.Join(", ", names.Select(n => $"type({n}) ~= 'nil'"));
        _rig.Write("scripts/thruster.lua", $"function get_outputs() return {checks} end");
        var creation = _rig.AddCreation(1, ScriptRig.Thruster);

        var result = _rig.Runtime.Invoke(1, ScriptRig.PartOf(creation, ScriptRig.Thruster), ScriptRuntime.Hooks.GetOutputs);

        Assert.True(result.Ok, result.Error);
        var missing = names.Where((_, i) => result.Values[i]?.GetValue<bool>() != true).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryHookEntryIsAHostHook()
    {
        var hooks = typeof(ScriptRuntime.Hooks).GetFields().Select(f => (string)f.GetValue(null)!).Order().ToList();
        Assert.Equal(hooks, LuaApi.Entries.Where(e => e.Group == "Hooks").Select(e => e.Name).Order().ToList());
    }
}
