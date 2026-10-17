using Godot;
using Mechanica.Core;

namespace Mechanica.Game;

/// <summary>
/// The <c>--self-test</c> launch check (release packaging runs it on the exported build): once the world is
/// entered, loads every part model and sound clip the mods name, prints what it found and quits with 0 when
/// nothing is missing, else 1. A world that fails to load quits with 1 straight away.
/// </summary>
public static class SelfTest
{
    /// <summary>Checks <paramref name="main"/>'s load each frame and quits once it has an answer.</summary>
    public static void Update(Main main)
    {
        if (main.Loading is not { } loading)
            return;
        if (loading.Error is { } error)
        {
            GD.PrintErr($"[self-test] FAIL: the world did not load: {error}");
            main.GetTree().Quit(1);
        }
        else if (loading.Entered)
            main.GetTree().Quit(Run(main) ? 0 : 1);
    }

    private static bool Run(Main main)
    {
        var mods = main.Mods!;
        var problems = new List<string>();
        if (mods.Mods.Count == 0)
            problems.Add($"no mods loaded from {Mods.ModPaths.ModsRoot}");
        problems.AddRange(mods.Diagnostics.Where(d => d.Severity == Core.Data.DiagnosticSeverity.Error).Select(d => $"mod data: {d}"));
        foreach (var part in mods.Parts.Values)
        {
            if (part.Definition.Mesh is { } mesh && main.Assets.ModelFor(part.QualifiedId, part.Definition).IsPlaceholder)
                problems.Add($"model '{mesh}' of {part.QualifiedId} did not load");
        }
        var clips = main.SoundEvents!.Events.Values.SelectMany(e => e.Clips).Distinct().ToList();
        problems.AddRange(clips.Where(c => main.Sounds.Player.Stream(c, loop: false) is null).Select(c => $"sound '{c}' did not load"));
        GD.Print($"[self-test] {MechanicaInfo.GameName} {MechanicaInfo.Version}: mods {string.Join(", ", mods.Mods.Select(m => $"{m.Id} {m.Version}"))}; "
            + $"{mods.Parts.Count} parts, {clips.Count} sound clips, {main.Streamer.Chunks.Count} terrain chunks, seed {main.Streamer.Seed}, "
            + $"player at {main.LocalPlayer?.GlobalPosition}");
        foreach (var problem in problems)
            GD.PrintErr($"[self-test] {problem}");
        GD.Print(problems.Count == 0 ? "[self-test] PASS" : $"[self-test] FAIL: {problems.Count} problems");
        return problems.Count == 0;
    }
}
