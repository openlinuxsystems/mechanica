using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Remote;

namespace Mechanica.Core.Packages;

/// <summary>
/// The <c>package.*</c> endpoint methods (admin): list, inspect, export, install, uninstall and verify mod
/// packages through the same <see cref="ModPackages"/> code as the Mod Manager. Packages are read from and
/// written to one packages folder only, named by file name, so an agent can never read or write other
/// paths. Installs and uninstalls are published on the <c>mods</c> event topic; they take effect with the
/// next world, like the Mod Manager's.
/// </summary>
/// <param name="modsRoot">The mods folder packages install into.</param>
/// <param name="packagesFolder">The folder exports go to and installs come from.</param>
/// <param name="events">Where to publish installs and uninstalls, or null.</param>
public sealed class PackageRpcModule(string modsRoot, string packagesFolder, RemoteEvents? events = null) : IRpcModule
{
    /// <summary>Params naming a package file in the packages folder.</summary>
    /// <param name="File">File name in the packages folder, like <c>better_wheels-1.2.0.zip</c>.</param>
    public sealed record FileParams(string File);

    /// <summary>Params naming an installed mod.</summary>
    /// <param name="Id">Mod id.</param>
    public sealed record ModParams(string Id);

    /// <summary>Params for <c>package.install</c>.</summary>
    /// <param name="File">File name in the packages folder.</param>
    /// <param name="Replace">Must be true to replace an installed copy (upgrade, reinstall, downgrade or a broken folder).</param>
    public sealed record InstallParams(string File, bool Replace = false);

    /// <summary>One diagnostic as the endpoint returns it.</summary>
    public sealed record Problem(string Severity, string Code, string File, string Message);

    /// <summary>The methods.</summary>
    public IEnumerable<RpcMethodInfo> Methods =>
    [
        RpcMethod.Create<NoArgs>("package.list", AgentScope.Admin,
            "Lists the mod packages (.zip) in the packages folder, by file name and size.", (_, _) => List()),
        RpcMethod.Create<FileParams>("package.inspect", AgentScope.Admin,
            "Checks a package in the packages folder without installing it: its manifest, every problem (unsafe paths, hashes, size limits, game version), and what installing it would do (new, upgrade, reinstall, downgrade, replace_broken; dependencies and conflicts).",
            (_, p) => Inspect(p.File)),
        RpcMethod.Create<ModParams>("package.export", AgentScope.Admin,
            "Validates an installed mod and exports it to <id>-<version>.zip in the packages folder. A mod with errors is not exported; ok is false and problems say why.",
            (_, p) => Export(p.Id)),
        RpcMethod.Create<InstallParams>("package.install", AgentScope.Admin,
            "Installs a package from the packages folder into the mods folder after checking it. Replacing an installed copy needs replace: true. Takes effect with the next world.",
            (_, p) => Install(p.File, p.Replace)),
        RpcMethod.Create<ModParams>("package.uninstall", AgentScope.Admin,
            "Deletes an installed mod (not vanilla). dependents lists the installed mods that need it and stop loading. Takes effect with the next world.",
            (_, p) => Uninstall(p.Id)),
        RpcMethod.Create<ModParams>("package.verify", AgentScope.Admin,
            "Compares an installed mod with the package.json it was installed with: files changed, missing or added since (MD1310).",
            (_, p) => Verify(p.Id)),
    ];

    private JsonElement? List()
    {
        var files = Directory.Exists(packagesFolder)
            ? Directory.GetFiles(packagesFolder, "*.zip").Order(StringComparer.Ordinal)
                .Select(f => new { file = Path.GetFileName(f), size = new FileInfo(f).Length }).ToList()
            : [];
        return RpcMethod.Value(new { folder = packagesFolder, packages = files });
    }

    private JsonElement? Inspect(string file)
    {
        var plan = ModPackages.PlanInstall(modsRoot, PackagePath(file));
        return RpcMethod.Value(new
        {
            file,
            valid = plan.Inspection.IsValid,
            can_install = plan.CanInstall,
            change = Change(plan.Change),
            installed_version = plan.InstalledVersion?.ToString(),
            manifest = plan.Inspection.Manifest is { } m ? DataJson.Element(DataJson.Write(m)) : (JsonElement?)null,
            problems = Problems(plan.Diagnostics),
        });
    }

    private JsonElement? Export(string id)
    {
        RequireId(id);
        var result = ModPackages.Export(modsRoot, id, packagesFolder);
        return RpcMethod.Value(new
        {
            ok = result.Succeeded,
            file = result.Path is null ? null : Path.GetFileName(result.Path),
            version = result.Manifest?.Version,
            sha256 = result.Manifest?.Sha256,
            files = result.Manifest?.Files?.Count,
            skipped = result.Skipped,
            problems = Problems(result.Diagnostics),
        });
    }

    private JsonElement? Install(string file, bool replace)
    {
        var result = ModPackages.Install(modsRoot, PackagePath(file), replace);
        var manifest = result.Plan.Inspection.Manifest;
        if (result.Succeeded)
            events?.Publish("mods", new { action = "installed", id = manifest!.Id, version = manifest.Version, change = Change(result.Plan.Change) });
        return RpcMethod.Value(new
        {
            ok = result.Succeeded,
            id = manifest?.Id,
            version = manifest?.Version,
            change = Change(result.Plan.Change),
            installed_version = result.Plan.InstalledVersion?.ToString(),
            needs_replace = !result.Succeeded && result.Plan.CanInstall && result.Plan.Replaces && !replace,
            problems = Problems(result.Diagnostics),
        });
    }

    private JsonElement? Uninstall(string id)
    {
        RequireId(id);
        var dependents = ModPackages.Dependents(modsRoot, id);
        var problems = ModPackages.Uninstall(modsRoot, id);
        if (problems.Count == 0)
            events?.Publish("mods", new { action = "uninstalled", id });
        return RpcMethod.Value(new { ok = problems.Count == 0, id, dependents, problems = Problems(problems) });
    }

    private JsonElement? Verify(string id)
    {
        RequireId(id);
        var mod = ModLoader.Scan(modsRoot).Mods.FirstOrDefault(m => m.Id == id)
            ?? throw new RpcException(RpcErrors.Rejected, $"mod '{id}' is not installed.");
        var problems = ModPackages.VerifyInstalled(mod.Directory);
        return RpcMethod.Value(new
        {
            id,
            from_package = File.Exists(Path.Combine(mod.Directory, ModPackages.ManifestFileName)),
            unchanged = problems.Count == 0,
            problems = Problems(problems),
        });
    }

    // Only a plain .zip file name inside the packages folder.
    private string PackagePath(string file)
    {
        if (string.IsNullOrEmpty(file) || file != Path.GetFileName(file) || file.Contains('\\') || file.StartsWith('.')
            || !file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(RpcErrors.BadParams, "file must be the name of a .zip in the packages folder (see package.list), not a path.");
        var path = Path.Combine(packagesFolder, file);
        return File.Exists(path) ? path : throw new RpcException(RpcErrors.Rejected, $"'{file}' is not in the packages folder (see package.list).");
    }

    private static void RequireId(string id)
    {
        if (!Identifiers.IsValid(id))
            throw new RpcException(RpcErrors.BadParams, $"'{id}' is not a mod id (lowercase snake_case).");
    }

    private static string Change(PackageChange change) => change switch
    {
        PackageChange.New => "new",
        PackageChange.Upgrade => "upgrade",
        PackageChange.Reinstall => "reinstall",
        PackageChange.Downgrade => "downgrade",
        _ => "replace_broken",
    };

    private static List<Problem> Problems(IEnumerable<DataDiagnostic> diagnostics) =>
        [.. diagnostics.Select(d => new Problem(d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.Code, d.File, d.Message))];
}
