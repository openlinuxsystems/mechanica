using System.Text.Json;
using Mechanica.Core.Actions;

namespace Mechanica.Scripting.Tests;

/// <summary>What scripts show on their parts (part.display, creation.display) and menu commands (on_command).</summary>
public sealed class PartMenuScriptTests : IDisposable
{
    private readonly ScriptRig _rig = new();

    public void Dispose()
    {
        _rig.Runtime.Clear();
        _rig.Dispose();
    }

    [Fact]
    public void APartShowsNumbersTextAndBooleansAndClearsThem()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_update(dt)
              state.n = (state.n or 0) + 1
              if state.n == 1 then
                part.display("fuel", 0.25)
                part.display("mode", "and")
                part.display("on", true)
              elseif state.n == 2 then
                print(part.displayed("fuel"), part.displayed("mode"), part.displayed("missing"))
                part.display("on", nil)
                local ok, why = part.display("Bad Id", 1)
                print(ok, why)
              end
            end
            """);
        var creation = _rig.AddCreation(1, ScriptRig.Thruster);
        var part = ScriptRig.PartOf(creation, ScriptRig.Thruster);
        _rig.Runtime.Update(1f / 60);
        Assert.Equal(0.25f, _rig.Runtime.Readouts.Number(1, part, "fuel"));
        Assert.Equal("and", _rig.Runtime.Readouts.Text(1, part, "mode"));
        Assert.Equal(1f, _rig.Runtime.Readouts.Number(1, part, "on"));
        _rig.Runtime.Update(1f / 60);
        Assert.False(_rig.Runtime.Readouts.Has(1, part, "on"));
        Assert.Contains("0.25\tand\tnil", _rig.Prints);
        Assert.Contains(_rig.Prints, p => p.StartsWith("false\t") && p.Contains("snake_case"));
        Assert.Empty(_rig.Errors);

        // Readouts go with the part.
        _rig.World.Creations.Remove(1);
        _rig.Runtime.Update(1f / 60);
        Assert.False(_rig.Runtime.Readouts.Has(1, part, "fuel"));
    }

    [Fact]
    public void ACreationDisplayOnlyReachesPartsThisOneIsLinkedTo()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_update(dt)
              print(creation.display(1, "drive", 2))
              print(creation.display(part.id(), "drive", 3))
            end
            """);
        var creation = _rig.AddCreation(1, ScriptRig.Thruster);
        var part = ScriptRig.PartOf(creation, ScriptRig.Thruster);
        _rig.Runtime.Update(1f / 60);
        Assert.Contains(_rig.Prints, p => p.StartsWith("false\t") && p.Contains("not linked from this part"));
        Assert.False(_rig.Runtime.Readouts.Has(1, 1, "drive"));
        Assert.Equal(3f, _rig.Runtime.Readouts.Number(1, part, "drive"));
    }

    [Fact]
    public void OnCommandGetsTheNameArgsAndPeer()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_command(name, args, peer)
              print(name, args.playing, #args.frames, args.frames[1], peer)
            end
            """);
        var creation = _rig.AddCreation(1, ScriptRig.Thruster);
        var part = ScriptRig.PartOf(creation, ScriptRig.Thruster);
        Assert.Null(_rig.Runtime.CheckCommand(creation, part));
        Assert.Contains("runs no script", _rig.Runtime.CheckCommand(creation, 1));
        _rig.Runtime.Command(new ActionCaller(4, ActionSource.Player), creation, part, "preview",
            JsonSerializer.SerializeToElement(new { playing = true, frames = new[] { 30, 60 } }));
        Assert.Contains("preview\ttrue\t2\t30\t4", _rig.Prints);

        _rig.Write("scripts/thruster.lua", "function on_update(dt) end");
        Assert.True(_rig.Runtime.Reload("test:scripts/thruster.lua").Ok);
        Assert.Contains("takes no commands", _rig.Runtime.CheckCommand(creation, part));
    }
}
