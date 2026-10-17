using System.IO.Compression;
using System.Text;
using Mechanica.Core.Data;
using Mechanica.Core.Packages;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Packages;

public class ModPackagesTests : IDisposable
{
    private readonly TestMods _source = new();
    private readonly TestMods _target = new();
    private readonly string _out = Path.Combine(Path.GetTempPath(), "mechanica-tests", Guid.NewGuid().ToString("N"), "out");

    public ModPackagesTests()
    {
        _source.Mod("vanilla", "0.1.0")
            .Mod("wheels", "1.2.0", dependencies: """[{ "id": "vanilla", "version": "^0.1.0" }]""")
            .Part("wheels", "wheel.json", TestMods.Block("wheel"))
            .File("wheels", "scripts/wheel.lua", "-- spin\n")
            .File("wheels", "meshes/wheel.glb.import", "godot leftover")
            .File("wheels", ".git/HEAD", "ref: main")
            .File("wheels", "notes.txt~", "backup");
        _target.Mod("vanilla", "0.1.0");
    }

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(_out)!, recursive: true); } catch (IOException) { }
    }

    private string Export(string id = "wheels")
    {
        var result = ModPackages.Export(_source.Root, id, _out);
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        return result.Path!;
    }

    [Fact]
    public void ExportWritesAVerifiedPackageWithoutLeftovers()
    {
        var result = ModPackages.Export(_source.Root, "wheels", _out);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Equal("wheels-1.2.0.zip", Path.GetFileName(result.Path));
        Assert.Equal(["mod.json", "parts/wheel.json", "scripts/wheel.lua"], result.Manifest!.Files!.Select(f => f.Path));
        Assert.Equal(["meshes/wheel.glb.import", "notes.txt~"], result.Skipped);
        Assert.Equal("", result.Manifest.Signature);
        Assert.Equal(MechanicaInfo.Version, result.Manifest.MinGameVersion);
        Assert.Equal("^0.1.0", Assert.Single(result.Manifest.Dependencies!).Version);
        using (var zip = ZipFile.OpenRead(result.Path!))
            Assert.Equal(["wheels/package.json", "wheels/mod.json", "wheels/parts/wheel.json", "wheels/scripts/wheel.lua"], zip.Entries.Select(e => e.FullName));
        var inspection = ModPackages.Inspect(result.Path!);
        Assert.True(inspection.IsValid, string.Join("\n", inspection.Diagnostics));
        Assert.Equal(result.Manifest.Sha256, inspection.Manifest!.Sha256);
    }

    [Fact]
    public void ExportIsByteForByteRepeatable()
    {
        var first = File.ReadAllBytes(Export());
        var second = File.ReadAllBytes(Export());

        Assert.Equal(first, second);
    }

    [Fact]
    public void ExportRefusesAModWithErrors()
    {
        _source.Part("wheels", "broken.json", TestMods.Block("broken", """{ "mass": -1 }"""));

        var result = ModPackages.Export(_source.Root, "wheels", _out);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.PartInvalid);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.PackageExportRefused);
        Assert.False(Directory.Exists(_out) && Directory.EnumerateFiles(_out).Any());
    }

    [Fact]
    public void ExportIgnoresErrorsInUnrelatedMods()
    {
        _source.Mod("other").Part("other", "broken.json", TestMods.Block("broken", """{ "mass": -1 }"""));

        Assert.True(ModPackages.Export(_source.Root, "wheels", _out).Succeeded);
    }

    [Fact]
    public void ExportRefusesAnUnknownMod()
    {
        var result = ModPackages.Export(_source.Root, "nope", _out);

        Assert.False(result.Succeeded);
        Assert.Contains("not installed", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public void InstallExtractsTheModWithItsManifest()
    {
        var zip = Export();

        var result = ModPackages.Install(_target.Root, zip, replace: false);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Equal(PackageChange.New, result.Plan.Change);
        Assert.Equal(File.ReadAllText(Path.Combine(_source.Root, "wheels/parts/wheel.json")),
            File.ReadAllText(Path.Combine(_target.Root, "wheels/parts/wheel.json")));
        Assert.True(File.Exists(Path.Combine(_target.Root, "wheels", ModPackages.ManifestFileName)));
        Assert.False(File.Exists(Path.Combine(_target.Root, "wheels/meshes/wheel.glb.import")));
        Assert.Empty(ModPackages.VerifyInstalled(result.Directory!));
        Assert.Empty(Directory.GetDirectories(_target.Root, ".*"));
        var loaded = _target.Load();
        Assert.Empty(loaded.Diagnostics);
        Assert.NotNull(loaded.FindPart("wheels:wheel"));
    }

    [Fact]
    public void VerifyInstalledReportsChangedMissingAndAddedFiles()
    {
        var dir = ModPackages.Install(_target.Root, Export(), replace: false).Directory!;
        File.AppendAllText(Path.Combine(dir, "scripts/wheel.lua"), "-- edited\n");
        File.Delete(Path.Combine(dir, "parts/wheel.json"));
        File.WriteAllText(Path.Combine(dir, "parts/new.json"), "{}");

        var messages = ModPackages.VerifyInstalled(dir).Select(d => $"{d.File}: {d.Message}").ToList();

        Assert.Equal(["wheels/parts/wheel.json: file is missing since install.", "wheels/scripts/wheel.lua: file was changed since install.",
            "wheels/parts/new.json: file was added since install."], messages);
    }

    [Fact]
    public void ReplacingNeedsConfirmationAndReportsTheChange()
    {
        var zip = Export();
        ModPackages.Install(_target.Root, zip, replace: false);
        File.WriteAllText(Path.Combine(_target.Root, "wheels/stale.lua"), "-- old");

        var refused = ModPackages.Install(_target.Root, zip, replace: false);
        var replaced = ModPackages.Install(_target.Root, zip, replace: true);

        Assert.False(refused.Succeeded);
        Assert.Equal(PackageChange.Reinstall, refused.Plan.Change);
        Assert.Contains("confirm replacing", refused.Diagnostics.Last().Message);
        Assert.True(replaced.Succeeded);
        Assert.False(File.Exists(Path.Combine(_target.Root, "wheels/stale.lua")));
    }

    [Theory]
    [InlineData("1.0.0", PackageChange.Upgrade)]
    [InlineData("1.2.0", PackageChange.Reinstall)]
    [InlineData("2.0.0", PackageChange.Downgrade)]
    public void PlanComparesWithTheInstalledVersion(string installed, PackageChange expected)
    {
        _target.Mod("wheels", installed);

        var plan = ModPackages.PlanInstall(_target.Root, Export());

        Assert.Equal(expected, plan.Change);
        Assert.Equal(SemVersion.Parse(installed), plan.InstalledVersion);
        Assert.True(plan.CanInstall && plan.Replaces);
    }

    [Fact]
    public void ABrokenFolderWithTheSameIdIsReplacedAfterConfirmation()
    {
        _target.File("wheels", "readme.txt", "not a mod");

        var plan = ModPackages.PlanInstall(_target.Root, Export());

        Assert.Equal(PackageChange.ReplaceBroken, plan.Change);
        Assert.True(ModPackages.Install(_target.Root, Export(), replace: true).Succeeded);
    }

    [Fact]
    public void MissingOrMismatchedDependenciesBlockTheInstall()
    {
        using var empty = new TestMods();
        var zip = Export();

        var missing = ModPackages.Install(empty.Root, zip, replace: true);
        _target.Mod("vanilla", "0.2.0");
        var wrong = ModPackages.PlanInstall(_target.Root, zip);

        Assert.False(missing.Succeeded);
        Assert.Contains("'vanilla' ^0.1.0, which is not installed", missing.Diagnostics.Single(d => d.Code == DiagnosticCodes.PackageDependency).Message);
        Assert.False(Directory.Exists(Path.Combine(empty.Root, "wheels")));
        Assert.False(wrong.CanInstall);
        Assert.Contains("but 0.2.0 is installed", wrong.Diagnostics.Single().Message);
    }

    [Fact]
    public void ConflictsAndBrokenDependentsAreWarnings()
    {
        _source.Mod("wheels", "2.0.0", conflicts: """[{ "id": "old_wheels" }]""");
        _target.Mod("old_wheels").Mod("cars", dependencies: """[{ "id": "wheels", "version": "^1.0.0" }]""").Mod("rival", conflicts: """[{ "id": "wheels", "version": ">=2.0.0" }]""");

        var plan = ModPackages.PlanInstall(_target.Root, Export());

        Assert.True(plan.CanInstall);
        Assert.All(plan.Diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Contains(plan.Diagnostics, d => d.Message.Contains("conflicts with the installed 'old_wheels'"));
        Assert.Contains(plan.Diagnostics, d => d.Message.Contains("'rival' conflicts with wheels 2.0.0"));
        Assert.Contains(plan.Diagnostics, d => d.Message.Contains("'cars' needs wheels ^1.0.0"));
    }

    [Fact]
    public void ATamperedFileIsRejected()
    {
        var zip = Rewrite(Export(), (name, data) => name == "wheels/scripts/wheel.lua" ? Encoding.UTF8.GetBytes("os.exit()\n") : data);

        var inspection = ModPackages.Inspect(zip);

        Assert.False(inspection.IsValid);
        Assert.Contains(inspection.Diagnostics, d => d.Code == DiagnosticCodes.PackageHashMismatch && d.File.EndsWith("scripts/wheel.lua"));
        Assert.False(ModPackages.Install(_target.Root, zip, replace: true).Succeeded);
        Assert.False(Directory.Exists(Path.Combine(_target.Root, "wheels")));
    }

    [Fact]
    public void AnEditedFileListNoLongerMatchesThePackageHash()
    {
        var zip = Rewrite(Export(), (name, data) => name == "wheels/package.json"
            ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(data).Replace("\"path\": \"scripts/wheel.lua\"", "\"path\": \"scripts/other.lua\""))
            : data);

        var codes = ModPackages.Inspect(zip).Diagnostics.Select(d => d.Code).ToList();

        Assert.Contains(DiagnosticCodes.PackageFileList, codes);
        Assert.Contains(DiagnosticCodes.PackageHashMismatch, codes);
    }

    [Fact]
    public void UnlistedFilesAreRejected()
    {
        var zip = Rewrite(Export(), (_, data) => data, extra: [("wheels/scripts/hidden.lua", "print('hi')")]);

        var error = Assert.Single(ModPackages.Inspect(zip).Diagnostics);

        Assert.Equal(DiagnosticCodes.PackageFileList, error.Code);
        Assert.Contains("not in package.json", error.Message);
    }

    [Theory]
    [InlineData("wheels/../evil.lua")]
    [InlineData("/etc/evil")]
    [InlineData("wheels\\evil.lua")]
    [InlineData("C:/evil.lua")]
    [InlineData("other/evil.lua")]
    [InlineData("wheels/./evil.lua")]
    [InlineData("wheels/MOD.JSON")]
    public void UnsafePathsAreRejected(string name)
    {
        var zip = Rewrite(Export(), (_, data) => data, extra: [(name, "x")]);

        var inspection = ModPackages.Inspect(zip);

        Assert.False(inspection.IsValid);
        Assert.Contains(inspection.Diagnostics, d => d.Code == DiagnosticCodes.PackageUnsafePath);
        Assert.False(ModPackages.Install(_target.Root, zip, replace: true).Succeeded);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_target.Root)!, "evil.lua")));
    }

    [Fact]
    public void SymbolicLinkEntriesAreRejected()
    {
        var zip = Rewrite(Export(), (_, data) => data, extra: [("wheels/link", "/etc/passwd")], symlink: "wheels/link");

        var error = Assert.Single(ModPackages.Inspect(zip).Diagnostics);

        Assert.Equal(DiagnosticCodes.PackageUnsafePath, error.Code);
        Assert.Contains("symbolic links", error.Message);
    }

    [Fact]
    public void ZipBombsAndOversizedArchivesAreRejected()
    {
        var zip = Rewrite(Export(), (_, data) => data, extra: [("wheels/bomb.bin", new string('\0', 4 << 20))]);

        var bomb = ModPackages.Inspect(zip);
        var total = ModPackages.Inspect(Export(), new PackageLimits(MaxTotalBytes: 100));
        var count = ModPackages.Inspect(Export(), new PackageLimits(MaxFiles: 2));
        var archive = ModPackages.Inspect(Export(), new PackageLimits(MaxArchiveBytes: 10));

        Assert.Contains(bomb.Diagnostics, d => d.Code == DiagnosticCodes.PackageTooLarge && d.Message.Contains("zip bomb"));
        Assert.Contains(total.Diagnostics, d => d.Code == DiagnosticCodes.PackageTooLarge);
        Assert.Contains(count.Diagnostics, d => d.Code == DiagnosticCodes.PackageTooLarge);
        Assert.Contains(archive.Diagnostics, d => d.Code == DiagnosticCodes.PackageTooLarge);
    }

    [Fact]
    public void ALyingSizeHeaderCannotOverrunTheListedSize()
    {
        // package.json says the script is 2 bytes; the archive holds more, so the stream is cut off.
        var zip = Rewrite(Export(), (name, data) => name == "wheels/package.json"
            ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(data).Replace("\"size\": 8,", "\"size\": 2,"))
            : data);

        var inspection = ModPackages.Inspect(zip);

        Assert.Contains(inspection.Diagnostics, d => d.Code == DiagnosticCodes.PackageHashMismatch || d.Code == DiagnosticCodes.PackageFileList);
        Assert.False(inspection.IsValid);
    }

    [Fact]
    public void APackageForANewerGameIsRejected()
    {
        var zip = Rewrite(Export(), (name, data) => name == "wheels/package.json"
            ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(data).Replace($"\"min_game_version\": \"{MechanicaInfo.Version}\"", "\"min_game_version\": \"99.0.0\""))
            : data);

        var error = Assert.Single(ModPackages.Inspect(zip).Diagnostics);

        Assert.Equal(DiagnosticCodes.PackageGameVersion, error.Code);
    }

    [Fact]
    public void APlainZipIsNotAPackage()
    {
        var zip = Path.Combine(_out, "plain.zip");
        Directory.CreateDirectory(_out);
        ZipFile.CreateFromDirectory(Path.Combine(_source.Root, "wheels"), zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        var garbage = Path.Combine(_out, "garbage.zip");
        File.WriteAllText(garbage, "not a zip");

        Assert.Contains("not a mod package", Assert.Single(ModPackages.Inspect(zip).Diagnostics).Message);
        Assert.Contains("not a readable zip", Assert.Single(ModPackages.Inspect(garbage).Diagnostics).Message);
    }

    [Fact]
    public void VanillaCannotBeInstalledOrUninstalled()
    {
        var zip = Export("vanilla");

        Assert.False(ModPackages.PlanInstall(_target.Root, zip).CanInstall);
        Assert.NotEmpty(ModPackages.Uninstall(_target.Root, "vanilla"));
        Assert.True(Directory.Exists(Path.Combine(_target.Root, "vanilla")));
    }

    [Fact]
    public void UninstallRemovesTheFolderAndListsDependents()
    {
        ModPackages.Install(_target.Root, Export(), replace: false);
        _target.Mod("cars", dependencies: """[{ "id": "wheels" }]""");

        Assert.Equal(["cars"], ModPackages.Dependents(_target.Root, "wheels"));
        Assert.Empty(ModPackages.Uninstall(_target.Root, "wheels"));
        Assert.False(Directory.Exists(Path.Combine(_target.Root, "wheels")));
        Assert.Empty(Directory.GetDirectories(_target.Root, ".*"));
        Assert.NotEmpty(ModPackages.Uninstall(_target.Root, "wheels"));
    }

    [Fact]
    public void ScanSkipsHiddenFolders()
    {
        Directory.CreateDirectory(Path.Combine(_target.Root, ".install-leftover", "wheels"));

        var scan = ModLoader.Scan(_target.Root);

        Assert.Empty(scan.Diagnostics);
        Assert.Equal(["vanilla"], scan.Mods.Select(m => m.Id));
    }

    // Copies a package, transforming entries and adding extra ones (optionally marking one as a symlink).
    private string Rewrite(string source, Func<string, byte[], byte[]> transform, (string Name, string Content)[]? extra = null, string? symlink = null)
    {
        var target = Path.Combine(_out, $"rewritten-{Guid.NewGuid():N}.zip");
        using (var input = ZipFile.OpenRead(source))
        using (var output = ZipFile.Open(target, ZipArchiveMode.Create))
        {
            foreach (var entry in input.Entries)
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                Write(output, entry.FullName, transform(entry.FullName, buffer.ToArray()), symlink);
            }
            foreach (var (name, content) in extra ?? [])
                Write(output, name, Encoding.UTF8.GetBytes(content), symlink);
        }
        return target;
    }

    private static void Write(ZipArchive zip, string name, byte[] data, string? symlink)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        if (name == symlink)
            entry.ExternalAttributes = unchecked((int)0xA1FF0000);
        using var stream = entry.Open();
        stream.Write(data);
    }
}
