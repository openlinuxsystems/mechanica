using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Logic;
using Mechanica.Core.World;
using Mechanica.Game.Editor;
using Mechanica.Game.Mods;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless test for chunk 23's content passes. Re-runs the Part Editor batches that author the lights,
/// utility parts, decor props and the remaining containers and stations into a scratch copy of the mod and
/// checks every file they write is byte-for-byte what is checked in (D3). Then, on a host with a client
/// (D10), through the client's Action API: a switch turns a spotlight and a rotor on, an interior light is
/// switched off with F, a warning light blinks, and the client shows every light and the rotor's spinning
/// model as the host's scripts drive them.
/// </summary>
public partial class VanillaContentTest : StagedTest
{
    private const int RigId = 1;
    // Stations first: the other batches' recipes name the Fabricator.
    private static readonly string[] Batches = ["containers_stations.json", "lights_utility.json", "decor.json"];

    private readonly List<ActionResult> _results = [];
    private readonly List<(string File, HashSet<string> Outputs)> _batches = [];
    private Task<BatchRunResult>? _batch;
    private int _next;
    private string _scratchMod = "";
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _switch, _spot, _lamp, _warning, _rotor;
    private readonly HashSet<bool> _blinks = [];
    private float _rotorAngle, _rotorTurned;

    protected override string Tag => "vanilla-content";

    protected override int StageTimeoutFrames => 1500;

    private static string VanillaDir => Path.Combine(ModPaths.ModsRoot, "vanilla");

    public override void _Ready()
    {
        foreach (var file in Batches)
            _batches.Add((file, OutputsOf(file)));

        Add("the Part Editor batches reproduce the checked-in content exactly", frame =>
        {
            if (frame == 0)
            {
                var root = ProjectSettings.GlobalizePath("user://tests/vanilla_content_mods");
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
                _scratchMod = Path.Combine(root, "vanilla");
                foreach (var file in Directory.GetFiles(VanillaDir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(VanillaDir, file).Replace('\\', '/');
                    if (IsBatchOutput(relative))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_scratchMod, relative))!);
                    File.Copy(file, Path.Combine(_scratchMod, relative));
                }
            }
            // One batch at a time, each after the one before has exported.
            if (_batch is { IsCompleted: true } done)
            {
                var file = _batches[_next - 1].File;
                Check(done.Result.Succeeded, $"{file} exports its parts and recipes ({string.Join("; ", done.Result.Diagnostics)})");
                _batch = null;
            }
            if (_batch is null && _next < _batches.Count)
                _batch = PartBatchRunner.RunAsync(this, Path.Combine(_scratchMod, PartBatch.Folder, _batches[_next++].File));
            if (_batch is not null)
                return false;
            var made = Directory.GetFiles(_scratchMod, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_scratchMod, f).Replace('\\', '/'))
                .Where(IsBatchOutput)
                .Order(StringComparer.Ordinal).ToList();
            var expected = _batches.Sum(b => b.Outputs.Count);
            Check(made.Count == expected, $"they write a part, an editor source, a mesh and recipes for each ({made.Count} of {expected} files)");
            var differing = made.Where(f => !File.Exists(Path.Combine(VanillaDir, f))
                || !File.ReadAllBytes(Path.Combine(VanillaDir, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(_scratchMod, f)))).ToList();
            Check(differing.Count == 0, $"every file matches mods/vanilla (differing: {string.Join(", ", differing)}); re-run tools/author-vanilla.sh");
            return true;
        });

        Add("a host and a client start on vanilla in Creative", frame =>
        {
            if (frame == 0)
            {
                _mods = ModLoader.Load([VanillaDir]);
                Check(!_mods.HasErrors, $"vanilla loads ({string.Join("; ", _mods.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))})");
                _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
                AddChild(_harness);
                _host = _harness.StartHost(mods: _mods);
                _client = _harness.StartClient("Client", mods: _mods);
            }
            return frame > 0 && _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null;
        });
        Add("the host spawns a rig of lights and a rotor, and the client walks up", frame =>
        {
            if (frame == 0)
            {
                SpawnRig();
                _host.Main.PlayerSync.HostTeleport(_client.Peer, new Vector3(2, 0.5f, 4));
            }
            return frame >= 30 && _client.Main.Creations.NodeOf(RigId) is not null;
        });
        Wait("the scripts start", 30);

        Add("the unlinked lights shine on both peers and the switched ones are dark", frame =>
        {
            foreach (var instance in new[] { _host, _client })
            {
                Check(Lit(instance, _lamp) is > 1.4f, $"{instance}: the interior light shines ({Lit(instance, _lamp)})");
                Check(Lit(instance, _spot) is 0f, $"{instance}: the spotlight waits for its switch ({Lit(instance, _spot)})");
            }
            return true;
        });
        Add("the client flips the switch: the spotlight comes on and the rotor spins up", frame =>
        {
            if (frame == 0)
                Request(LogicActions.ActivatePart, new LogicPartArgs(RigId, _switch));
            if (frame < 120)
                return false;
            Check(_results.Count == 1 && _results[0].Ok, $"flipping the switch succeeds ({string.Join("; ", _results)})");
            foreach (var instance in new[] { _host, _client })
                Check(Lit(instance, _spot) is > 5.9f and < 6.1f, $"{instance}: the spotlight shines at its full energy ({Lit(instance, _spot)})");
            Check(_host.Main.Display.Animations.Speed(RigId, _rotor) < -30f, $"the host spins the rotor's model clockwise ({_host.Main.Display.Animations.Speed(RigId, _rotor)} rad/s)");
            _rotorAngle = RotorAngle(_client);
            _results.Clear();
            return true;
        });
        Add("the client shows the rotor's model turning between updates", frame =>
        {
            // Wrapped to -π..π so a whole turn between samples doesn't hide the spin.
            _rotorTurned = MathF.Max(_rotorTurned, MathF.Abs(Mathf.Wrap(RotorAngle(_client) - _rotorAngle, -Mathf.Pi, Mathf.Pi)));
            if (frame < 30)
                return false;
            Check(_rotorTurned > 0.05f, $"the client's rotor model turned (by up to {_rotorTurned:F3} rad)");
            return true;
        });
        Add("the warning light blinks on the client", frame =>
        {
            _blinks.Add(Lit(_client, _warning) > 0f);
            if (frame < 90)
                return false;
            Check(_blinks.Count == 2, "the client sees the warning light both lit and dark");
            return true;
        });
        Add("the client switches the interior light off with F", frame =>
        {
            if (frame == 0)
                Request(LogicActions.ActivatePart, new LogicPartArgs(RigId, _lamp));
            if (frame < 30)
                return false;
            Check(_results.Count == 1 && _results[0].Ok, $"using the light succeeds ({string.Join("; ", _results)})");
            foreach (var instance in new[] { _host, _client })
                Check(Lit(instance, _lamp) is 0f, $"{instance}: the interior light is off ({Lit(instance, _lamp)})");
            return true;
        });
    }

    // A 6 x 6 metal plate with a switch linked to a spotlight and a rotor, an interior light hung under a
    // corner and a warning light.
    private void SpawnRig()
    {
        var rig = new Creation(RigId, _mods);
        for (var x = 0; x < 6; x++)
            for (var z = 0; z < 6; z++)
                Place(rig, "vanilla:metal_block", new GridCell(x, 0, z));
        _switch = Place(rig, "vanilla:switch", new GridCell(5, 1, 5));
        _spot = Place(rig, "vanilla:spotlight", new GridCell(0, 1, 0));
        _rotor = Place(rig, "vanilla:rotor", new GridCell(3, 1, 0));
        _warning = Place(rig, "vanilla:warning_light", new GridCell(0, 1, 5));
        _lamp = Place(rig, "vanilla:interior_light", new GridCell(4, -1, 4));
        foreach (var to in new[] { _spot, _rotor })
        {
            if (!rig.TryLink(_switch, to, out var error))
                throw new InvalidOperationException(error);
        }
        rig.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(0, 0.15f, 0), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(rig, _host.Main.Assets);
    }

    private static int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var error))
            throw new InvalidOperationException(error);
        return placed.Id;
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    // The energy a peer shows a part's light at (0 when it is dark).
    private static float Lit(Instance instance, int part)
    {
        var node = instance.Main.Creations.NodeOf(RigId) ?? throw new InvalidOperationException($"{instance} shows no rig");
        var light = node.Lights.First(l => l.Part == part).Light;
        return light.Visible ? light.LightEnergy : 0f;
    }

    // How far a peer has turned the rotor's model about its +Y.
    private float RotorAngle(Instance instance)
    {
        var model = instance.Main.Creations.NodeOf(RigId)!.AnimatedModels.First(m => m.Part == _rotor).Model;
        var x = model.Transform.Basis.X;
        return Mathf.Atan2(-x.Z, x.X);
    }

    // Everything a batch writes: each part, its editor source and baked mesh, and its recipes.
    private static HashSet<string> OutputsOf(string file)
    {
        var diagnostics = new List<DataDiagnostic>();
        var batch = PartBatch.Read(File.ReadAllText(Path.Combine(VanillaDir, PartBatch.Folder, file)), file, diagnostics)
            ?? throw new InvalidOperationException($"{file}: {string.Join("; ", diagnostics)}");
        var outputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in batch.Parts ?? [])
        {
            outputs.Add(entry.File!);
            outputs.Add(PartDocument.SourcePathFor(entry.File!));
            // A part whose model is one existing mesh points at that mesh and bakes nothing.
            if ((PartSource.Empty with { Model = entry.Model is { Count: > 0 } model ? [.. model] : null }).NeedsBake)
                outputs.Add(PartExporter.BakedMeshPath(entry.File!));
            foreach (var recipe in entry.Recipes ?? [])
                outputs.Add(RecipeExporter.TargetFile(recipe.Id!));
        }
        return outputs;
    }

    private bool IsBatchOutput(string relative) => _batches.Any(b => b.Outputs.Contains(relative));
}
