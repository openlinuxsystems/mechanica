namespace Mechanica.Core.Data;

/// <summary>
/// The logic behind the in-game Mod Manager: lists installed mods in load order, enables and
/// disables them (pulling in dependencies and dependents), reorders them, and turns the result into
/// a <see cref="ModProfile"/> that <see cref="ModLoader"/> loads. Changes stay in memory until the
/// caller saves <see cref="ToProfile"/>.
/// </summary>
public sealed class ModPackageManager
{
    private readonly Dictionary<string, InstalledMod> _installed;
    private readonly Dictionary<string, bool> _enabled = new(StringComparer.Ordinal);
    private List<string> _order;

    private ModPackageManager(string modsRoot, ModScanResult scan, ModProfile? profile)
    {
        ModsRoot = modsRoot;
        Scan = scan;
        _installed = scan.Mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var entry in profile?.Mods ?? [])
        {
            if (entry.Id is not null)
                _enabled.TryAdd(entry.Id, entry.IsEnabled || entry.Id == Identifiers.VanillaModId);
        }
        foreach (var id in _installed.Keys)
            _enabled.TryAdd(id, true);
        _order = ModLoader.PreferredOrder(_installed.Keys, profile);
        Normalize();
    }

    /// <summary>Scans <paramref name="modsRoot"/> and applies <paramref name="profile"/> (null: every mod enabled).</summary>
    public static ModPackageManager Open(string modsRoot, ModProfile? profile) =>
        new(modsRoot, ModLoader.Scan(modsRoot), profile);

    /// <summary>The mods folder this manager lists.</summary>
    public string ModsRoot { get; }

    /// <summary>The installed mods and manifest problems found when the manager opened.</summary>
    public ModScanResult Scan { get; }

    /// <summary>
    /// Installed mods in load order. Enabled mods load in this order; dependencies always come
    /// before their dependents.
    /// </summary>
    public IReadOnlyList<InstalledMod> Mods => [.. _order.Where(_installed.ContainsKey).Select(id => _installed[id])];

    /// <summary>Mods the profile lists that are not installed. They stay in the saved profile.</summary>
    public IReadOnlyList<string> MissingMods => [.. _order.Where(id => !_installed.ContainsKey(id))];

    /// <summary>Whether <paramref name="id"/> is enabled.</summary>
    public bool IsEnabled(string id) => _enabled.GetValueOrDefault(id);

    /// <summary>False for vanilla, which the game needs and is always enabled.</summary>
    public static bool CanDisable(string id) => id != Identifiers.VanillaModId;

    /// <summary>
    /// Enables or disables an installed mod. Enabling also enables every installed mod it depends on;
    /// disabling also disables every enabled mod that depends on it. Disabling vanilla does nothing.
    /// </summary>
    /// <returns>Every mod whose state changed, <paramref name="id"/> first.</returns>
    public IReadOnlyList<string> SetEnabled(string id, bool enabled)
    {
        if (!_installed.ContainsKey(id))
            throw new ArgumentException($"mod '{id}' is not installed.", nameof(id));
        if (!enabled && !CanDisable(id))
            return [];
        var affected = enabled ? Closure(id, m => m.DependencyIds) : Closure(id, Dependents);
        var changed = affected.Where(m => _enabled[m] != enabled && (enabled || CanDisable(m))).ToList();
        foreach (var m in changed)
            _enabled[m] = enabled;
        return changed;
    }

    /// <summary>
    /// Moves <paramref name="id"/> to <paramref name="index"/> in <see cref="Mods"/>. A mod cannot move
    /// above a mod it depends on, nor below one that depends on it, so the move stops at that limit.
    /// </summary>
    /// <returns>The index the mod ended up at.</returns>
    public int Move(string id, int index)
    {
        var mods = Mods.Select(m => m.Id).ToList();
        var from = mods.IndexOf(id);
        if (from < 0)
            throw new ArgumentException($"mod '{id}' is not installed.", nameof(id));
        index = Math.Clamp(index, 0, mods.Count - 1);
        if (index < from)
        {
            // Stop just below the lowest of its dependencies that sits in the way.
            for (var i = from - 1; i >= index; i--)
            {
                if (DependsOn(id, mods[i]))
                {
                    index = i + 1;
                    break;
                }
            }
        }
        else
        {
            for (var i = from + 1; i <= index; i++)
            {
                if (DependsOn(mods[i], id))
                {
                    index = i - 1;
                    break;
                }
            }
        }
        if (index == from)
            return from;

        // Rebuild the full order (missing mods keep their slots) with the moved mod beside its new neighbour.
        var neighbour = mods[index];
        _order.Remove(id);
        var at = _order.IndexOf(neighbour);
        _order.Insert(index < from ? at : at + 1, id);
        Normalize();
        return Mods.Select(m => m.Id).ToList().IndexOf(id);
    }

    /// <summary>The setup as a profile, ready to save to <see cref="ModProfile.FileName"/>.</summary>
    public ModProfile ToProfile() => new()
    {
        FormatVersion = ModProfile.CurrentFormatVersion,
        Mods = [.. _order.Select(id => new ModProfileEntry { Id = id, Enabled = _enabled.GetValueOrDefault(id, true) })],
    };

    /// <summary>Loads the enabled mods as the game would, to show their errors, warnings and conflicts.</summary>
    public ModLoadResult Load() => ModLoader.Load(ModsRoot, ToProfile());

    private IEnumerable<string> Dependents(InstalledMod mod) =>
        _installed.Values.Where(m => m.DependencyIds.Contains(mod.Id)).Select(m => m.Id);

    // id plus everything reachable through next, limited to installed mods, in discovery order.
    private List<string> Closure(string id, Func<InstalledMod, IEnumerable<string>> next)
    {
        var result = new List<string> { id };
        for (var i = 0; i < result.Count; i++)
        {
            foreach (var other in next(_installed[result[i]]).Order(StringComparer.Ordinal))
            {
                if (_installed.ContainsKey(other) && !result.Contains(other))
                    result.Add(other);
            }
        }
        return result;
    }

    private bool DependsOn(string id, string otherId) => Closure(id, m => m.DependencyIds).Skip(1).Contains(otherId);

    // Keeps the order a valid load order: each mod below the installed mods it depends on.
    private void Normalize()
    {
        var (order, stuck) = ModLoader.SortByDependencies(_order,
            id => _installed.TryGetValue(id, out var mod) ? mod.DependencyIds.Where(_installed.ContainsKey) : []);
        _order = [.. order, .. stuck];
    }
}
