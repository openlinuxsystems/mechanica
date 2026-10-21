using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Game.Creations;
using Mechanica.Game.Editor;
using Mechanica.Game.Mods;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless test for chunk 6, vanilla's structural blocks and wedges. Re-runs the Part Editor batch that
/// authors them into a scratch copy of the mod and checks every file it writes (part, editor source,
/// baked mesh and recipe) is byte-for-byte what is checked in, so the shipped blocks are the editor's output (D3).
/// Then, in the Test Sandbox with a host and a local client (D10), the client spawns every block and
/// wedge through the Action API; the host validates them, all of them land on the ground on both
/// instances, and glass is see-through while the path light glows. Exits 0 on success.
/// </summary>
public partial class VanillaBlocksTest : StagedTest
{
    private const string BatchFile = "editor/batches/structural_blocks.json";

    private Task<BatchRunResult>? _batch;
    private string _scratchMod = "";
    private SandboxScreen _sandbox = null!;
    private SandboxScreen.Instance _client = null!;
    private List<string> _blocks = [];
    private int _accepted;
    private ModLoadResult _content = null!;

    protected override string Tag => "vanilla";

    private Vector3 Home => _sandbox.Host.Main.GetNode<Node3D>("SpawnPoint").GlobalPosition;

    protected override int StageTimeoutFrames => 1200;

    private static string VanillaDir => Path.Combine(ModPaths.ModsRoot, "vanilla");

    public override void _Ready()
    {
        Add("the Part Editor batch reproduces the checked-in blocks exactly", frame =>
        {
            if (frame == 0)
            {
                var root = ProjectSettings.GlobalizePath("user://tests/vanilla_mods");
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
                // A copy of vanilla without what the batch makes; the rest (resources, stations) its recipes need.
                _scratchMod = Path.Combine(root, "vanilla");
                foreach (var file in Directory.GetFiles(VanillaDir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(VanillaDir, file).Replace('\\', '/');
                    if (IsBatchOutput(relative))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_scratchMod, relative))!);
                    File.Copy(file, Path.Combine(_scratchMod, relative));
                }
                _batch = PartBatchRunner.RunAsync(this, Path.Combine(_scratchMod, BatchFile));
            }
            if (_batch is not { IsCompleted: true })
                return false;
            var result = _batch.Result;
            Check(result.Succeeded && result.Exports.Count == 169, $"the batch exports 86 parts and 83 recipes ({string.Join("; ", result.Diagnostics)})");
            var made = Directory.GetFiles(_scratchMod, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_scratchMod, f).Replace('\\', '/'))
                .Where(IsBatchOutput)
                .Order(StringComparer.Ordinal).ToList();
            Check(made.Count == 341, $"it writes a part, an editor source and a mesh for each, and the recipes ({made.Count} files)");
            var differing = made.Where(f => !File.Exists(Path.Combine(VanillaDir, f))
                || !File.ReadAllBytes(Path.Combine(VanillaDir, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(_scratchMod, f)))).ToList();
            Check(differing.Count == 0, $"every file matches mods/vanilla (differing: {string.Join(", ", differing)}); re-run tools/author-vanilla.sh");
            return true;
        });

        Add("the sandbox opens on the vanilla mod with a host and a local client", frame =>
        {
            if (frame == 0)
            {
                _content = ModLoader.LoadAll(ModPaths.ModsRoot);
                Check(!_content.HasErrors, $"the mods load ({string.Join("; ", _content.Diagnostics)})");
                _blocks = [.. _content.Parts.Values.Where(p => p.Definition.Category == PartCategory.Block).Select(p => p.QualifiedId).Order()];
                Check(_blocks.Count == 86, $"vanilla has 86 blocks and wedges ({_blocks.Count})");
                _sandbox = new SandboxScreen { Name = "Sandbox", LoadContent = () => _content };
                AddChild(_sandbox);
            }
            if (frame == 1)
                _client = _sandbox.AddLocalClient()!;
            return frame > 1 && _sandbox.Host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null;
        });

        Add("the client spawns every block and wedge and the host accepts them", frame =>
        {
            if (frame == 0)
            {
                // Rows just north of the spawn, on the crash site's flat pad and clear of its wreck.
                var home = Home;
                for (var i = 0; i < _blocks.Count; i++)
                {
                    float[] position = [home.X + (i % 8 * 0.75f) - 2.5f, home.Y + 0.6f, home.Z - (i / 8 * 0.75f) - 4f];
                    _client.Main.Actions.Request(CreationActions.SpawnPart, new SpawnPartArgs(_blocks[i], position), r =>
                    {
                        if (Check(r.Ok, $"spawn accepted ({r})"))
                            _accepted++;
                    });
                }
            }
            return _accepted == _blocks.Count && Count(_sandbox.Host) == _blocks.Count && Count(_client) == _blocks.Count;
        });

        Add("every block lands on the ground on host and client", frame =>
        {
            var hostNodes = _sandbox.Host.Main.Creations.All.Where(c => !c.IsWorldOwned).Select(c => _sandbox.Host.Main.Creations.NodeOf(c.Id)!).ToList();
            if (frame < 60 || (hostNodes.Any(n => !n.Bodies[0].Sleeping) && frame < 900))
                return false;
            foreach (var node in hostNodes)
            {
                var part = node.Creation.Parts.Single().Part;
                var host = node.Bodies[0].GlobalPosition;
                Check(host.Y - Home.Y is > 0.08f and < 0.17f, $"{part} rests on the ground on the host (y={host.Y:F3}, up={node.Bodies[0].GlobalBasis.Y}, sleeping={node.Bodies[0].Sleeping})");
                var client = _client.Main.Creations.NodeOf(node.Creation.Id)!.Bodies[0].GlobalPosition;
                Check(client.DistanceTo(host) < 0.05f, $"{part} is in the same place on the client ({client} vs {host})");
            }
            return true;
        });

        Add("glass is see-through and the path light glows", _ =>
        {
            var assets = new PartAssets(_content);
            BaseMaterial3D Material(string id) =>
                (BaseMaterial3D)assets.ModelFor(id, _content.FindDefinition(id)!).Meshes[0].SurfaceMaterials![0]!;
            var glass = Material("vanilla:glass_block");
            Check(glass.Transparency == BaseMaterial3D.TransparencyEnum.Alpha && Mathf.IsEqualApprox(glass.AlbedoColor.A, 0.5f),
                $"glass is drawn at 50% opacity (alpha {glass.AlbedoColor.A})");
            var metal = Material("vanilla:metal_block");
            Check(metal.Transparency == BaseMaterial3D.TransparencyEnum.Disabled, "metal is opaque");
            Check(metal.AlbedoTexture is not null && metal.AlbedoColor.R > 0.99f, "metal is textured and untinted");
            var net = Material("vanilla:net_block");
            Check(net.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor, $"the net's gaps are cut out ({net.Transparency})");
            var pathLight = Material("vanilla:path_light_block");
            Check(pathLight.EmissionEnabled && pathLight.EmissionTexture is not null && pathLight.EmissionOperator == BaseMaterial3D.EmissionOperatorEnum.Multiply,
                "the path light block glows only where its glow map is bright");
            Check(!assets.ModelFor("vanilla:wood_wedge", _content.FindDefinition("vanilla:wood_wedge")!).IsPlaceholder,
                "wedges use their baked mesh");
            // Scrap metal spans 8 cells: blocks side by side take different pieces of the grid-mapped pattern, and a block
            // one whole repeat (2 m) along shares a material with the first.
            var scrap = assets.ModelFor("vanilla:scrap_metal_block", _content.FindDefinition("vanilla:scrap_metal_block")!).Meshes[0];
            Material Shown(Vector3 at) => scrap.Instantiate(new Transform3D(Basis.Identity, at)).GetSurfaceOverrideMaterial(0);
            var first = Shown(new Vector3(0.125f, 0.125f, 0.125f));
            Check(scrap.TextureSpan == 8 && first is ShaderMaterial, $"scrap metal is laid over the grid (span {scrap.TextureSpan}, {first.GetType().Name})");
            Check(Shown(new Vector3(0.375f, 0.125f, 0.125f)) != first, "the next scrap metal block shows the next piece of the pattern");
            Check(Shown(new Vector3(2.125f, 0.125f, 0.125f)) == first, "a scrap metal block one repeat along shows the same piece");
            return true;
        });
    }

    // Blocks and wedges live in blocks/ folders; their recipes are named after them.
    private static bool IsBatchOutput(string relative) =>
        relative.StartsWith("parts/blocks/", StringComparison.Ordinal) || relative.StartsWith("meshes/blocks/", StringComparison.Ordinal)
        || relative.StartsWith("editor/parts/blocks/", StringComparison.Ordinal)
        || (relative.StartsWith("recipes/", StringComparison.Ordinal) && BlockRecipe().IsMatch(relative));

    // metal_block.json, metal_wedge_2.json: a tier's number follows block or wedge.
    [System.Text.RegularExpressions.GeneratedRegex(@"_(block|wedge)(_\d+)?\.json$")]
    private static partial System.Text.RegularExpressions.Regex BlockRecipe();

    // Only what players made: the Workshops' Fabricators (world-owned) are in the vanilla world too.
    private static int Count(SandboxScreen.Instance instance) => instance.Main.Creations.All.Count(c => !c.IsWorldOwned);
}
