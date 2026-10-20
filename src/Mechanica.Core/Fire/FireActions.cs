using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Quests;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Fire;

/// <summary>One fire, as <c>GetFires</c> returns it, clients are sent it and scripts see it.</summary>
/// <param name="Id"><c>site:&lt;site&gt;:&lt;marker&gt;</c> for a fire at a POI marker, <c>part:&lt;creation&gt;:&lt;part&gt;</c> for a burning part.</param>
/// <param name="Position">Where it burns, in world metres (a part's centre).</param>
/// <param name="Strength">0 (out) to 1.</param>
/// <param name="Creation">The burning part's Creation.</param>
/// <param name="Part">The burning part.</param>
/// <param name="Poi">Qualified id of the POI a site fire burns at.</param>
/// <param name="Site">The world generator's index of that POI.</param>
/// <param name="Marker">Index of the fire's marker in the POI.</param>
public sealed record FireView(string Id, Vec3 Position, float Strength, int? Creation = null, int? Part = null,
    string? Poi = null, int? Site = null, int? Marker = null);

/// <summary>A fire at a POI marker in a save.</summary>
/// <param name="Site">The world generator's POI index.</param>
/// <param name="Marker">Marker index in the POI.</param>
/// <param name="Strength">0 (put out) to 1.</param>
public sealed record SavedSiteFire(int Site, int Marker, float Strength);

/// <summary>What the fire system needs from the world beyond <see cref="ISurvivalWorld"/>. The host's game layer implements it.</summary>
public interface IFireWorld
{
    /// <summary>Parts whose shapes reach within <paramref name="radius"/> m of <paramref name="point"/>, as (Creation, part).</summary>
    IEnumerable<(int Creation, int Part)> PartsNear(Vector3 point, float radius);
}

/// <summary>
/// Fire (D13), run on the host. Two kinds burn: fires at POI markers of type <see cref="FireSettings.SiteMarker"/>
/// (lit when a world is made, like the crash site's three, and burning until water puts them out) and burning
/// parts (their strength lives on the part in its Creation, so it follows splits and welds and is saved with it).
/// Every <see cref="FireSettings.Tick"/> seconds each fire grows toward full strength, burns the players
/// standing in it, and a burning part takes damage until it breaks off; a part strong enough sets its
/// flammable neighbours alight, and a site fire lights flammable parts close by. A part under water goes out.
/// Water puts fires out through <see cref="Douse"/>, which the watering item's and water cannon's Lua scripts
/// call (D2: what puts a fire out, and how much, is their data). There is no randomness, so replays agree.
/// </summary>
public sealed class FireActions(FireSettings settings, SurvivalActions survival, IFireWorld world)
{
    /// <summary>Action name for reading every fire.</summary>
    public const string GetFires = "GetFires";

    private readonly SortedDictionary<(int Site, int Marker), SiteFire> _sites = [];
    // Site fires a save named before the sites were lit.
    private readonly Dictionary<(int Site, int Marker), float> _loaded = [];
    private float _sinceTick;

    private sealed class SiteFire(string poi, int site, int marker, Vector3 position, float strength)
    {
        public readonly string Poi = poi;
        public readonly int Site = site;
        public readonly int Marker = marker;
        public readonly Vector3 Position = position;
        public float Strength = strength;
    }

    /// <summary>The numbers in use.</summary>
    public FireSettings Settings => settings;

    /// <summary>A site fire went out (water put it out).</summary>
    public event Action<FireView>? SiteOut;

    /// <summary>Registers <c>GetFires</c>.</summary>
    public void Register(ActionDispatcher dispatcher) =>
        dispatcher.Register<NoArgs>(GetFires, (_, _) => null, (_, _) => JsonSerializer.SerializeToElement(Fires(), ActionJson.Options), readOnly: true);

    /// <summary>
    /// Lights a fire at every marker of type <see cref="FireSettings.SiteMarker"/> of <paramref name="sites"/>
    /// not lit before, at full strength (or as a loaded save left it).
    /// </summary>
    public void LightSites(IEnumerable<QuestSite> sites)
    {
        foreach (var site in sites)
        {
            foreach (var marker in site.Markers.Where(m => m.Type == settings.SiteMarker))
            {
                var key = (site.Index, marker.Index);
                if (_sites.ContainsKey(key))
                    continue;
                var strength = _loaded.Remove(key, out var saved) ? saved : 1f;
                _sites[key] = new SiteFire(site.Poi, site.Index, marker.Index, marker.Position, strength);
            }
        }
    }

    /// <summary>Every fire: site fires (out ones too, at strength 0) then burning parts, in a fixed order.</summary>
    public IReadOnlyList<FireView> Fires()
    {
        var fires = _sites.Values.Select(View).ToList();
        foreach (var id in survival.World.AllCreations.Where(c => c.Burning.Any()).Select(c => c.Id).Order().ToList())
        {
            // Found again so its Body poses are current.
            if (survival.World.FindCreation(id) is not { } creation)
                continue;
            foreach (var (part, _) in creation.Burning)
                fires.Add(View(creation, part));
        }
        return fires;
    }

    /// <summary>Fires burning within <paramref name="radius"/> m of <paramref name="point"/>, nearest first.</summary>
    public IReadOnlyList<FireView> Near(Vector3 point, float radius) =>
        [.. Fires().Where(f => f.Strength > 0 && Vector3.Distance(f.Position.ToNumerics(), point) <= radius)
            .OrderBy(f => Vector3.Distance(f.Position.ToNumerics(), point))];

    /// <summary>Site fires of <paramref name="poi"/> at markers of type <paramref name="marker"/> that are out.</summary>
    public int SitesOut(string poi, string marker) =>
        marker == settings.SiteMarker ? _sites.Values.Count(s => s.Poi == poi && s.Strength <= 0) : 0;

    /// <summary>
    /// Sets a flammable part alight at <paramref name="strength"/> (default <see cref="FireSettings.Ignite"/>), or
    /// makes a burning one burn at least that strongly. Null when it caught, or why not.
    /// </summary>
    public string? Ignite(Creation creation, int partId, float? strength = null)
    {
        if (creation.FindPart(partId) is null)
            return $"There is no part {partId} in creation {creation.Id}.";
        if (WorldOwned.Refusal(creation, "set alight") is { } owned)
            return owned;
        var definition = creation.DefinitionOf(partId);
        if (definition.Flammable != true)
            return $"The {definition.Name ?? "part"} does not burn.";
        var s = strength ?? settings.Ignite;
        if (!(s > 0 && s <= 1))
            return "strength must be more than 0 and at most 1.";
        if (creation.FireOf(partId) <= 0 && Count() >= settings.MaxFires)
            return "Too many fires are burning already.";
        creation.SetFire(partId, Math.Max(s, creation.FireOf(partId)));
        return null;
    }

    /// <summary>
    /// Water on <paramref name="point"/>: every fire within <paramref name="radius"/> m (site fires by their
    /// position, parts by their shape) loses <paramref name="amount"/> strength, and goes out at 0. Returns the
    /// fires it reached, with their strength after.
    /// </summary>
    public IReadOnlyList<FireView> Douse(Vector3 point, float radius, float amount)
    {
        if (!(amount > 0) || !float.IsFinite(amount) || !(radius >= 0) || !float.IsFinite(radius))
            return [];
        var reached = new List<FireView>();
        foreach (var site in _sites.Values.Where(s => s.Strength > 0 && Vector3.Distance(s.Position, point) <= radius))
        {
            site.Strength = Math.Max(0, site.Strength - amount);
            reached.Add(View(site));
            if (site.Strength <= 0)
                SiteOut?.Invoke(View(site));
        }
        foreach (var (id, part) in world.PartsNear(point, radius).Distinct().ToList())
        {
            if (survival.World.FindCreation(id) is not { } creation || creation.FindPart(part) is null || creation.FireOf(part) <= 0)
                continue;
            creation.SetFire(part, creation.FireOf(part) - amount);
            reached.Add(View(creation, part));
        }
        return reached;
    }

    /// <summary>
    /// Why the player behind <paramref name="caller"/> cannot reach water onto <paramref name="point"/> (their
    /// reach plus <paramref name="radius"/>); null when they can, or when the caller is not a player.
    /// </summary>
    public string? CheckReach(ActionCaller caller, Vector3 point, float radius)
    {
        if (caller.Source != ActionSource.Player)
            return null;
        if (survival.World.EyeOf(caller.Peer) is not { } eye)
            return "You have no player to reach it with.";
        var distance = Vector3.Distance(eye, point);
        var reach = survival.Settings.Reach + Math.Max(0, radius);
        return distance <= reach ? null : $"That is {distance:F1} m away; you can reach {reach:F1} m.";
    }

    /// <summary>Burns for <paramref name="seconds"/>. The host calls it every physics tick; the work runs every <see cref="FireSettings.Tick"/>.</summary>
    public void Tick(float seconds)
    {
        _sinceTick += seconds;
        if (_sinceTick < settings.Tick)
            return;
        var dt = _sinceTick;
        _sinceTick = 0;
        // Parts that catch this tick start burning next tick.
        var burning = survival.World.AllCreations.SelectMany(c => c.Burning.Select(b => (Creation: c.Id, b.PartId))).ToList();
        foreach (var site in _sites.Values.Where(s => s.Strength > 0))
        {
            site.Strength = Math.Min(1f, site.Strength + settings.Grow * dt);
            foreach (var (id, part) in world.PartsNear(site.Position, settings.SiteRadius).Distinct().ToList())
            {
                if (survival.World.FindCreation(id) is { } creation && creation.FireOf(part) <= 0)
                    Ignite(creation, part);
            }
            Scorch(site.Position, site.Strength, dt);
        }
        foreach (var (id, part) in burning)
        {
            // A part that broke off, or split away into another Creation, is skipped; it burns on next tick.
            if (survival.World.FindCreation(id) is not { } creation || creation.FindPart(part) is null || creation.FireOf(part) <= 0)
                continue;
            var centre = PartCentre(creation, part);
            if (survival.World.IsWater(centre))
            {
                creation.SetFire(part, 0);
                continue;
            }
            var strength = Math.Min(1f, creation.FireOf(part) + settings.Grow * dt);
            creation.SetFire(part, strength);
            if (strength >= settings.SpreadAt)
            {
                foreach (var next in creation.Connections.Where(c => c.Involves(part)).Select(c => c.A == part ? c.B : c.A).Distinct().ToList())
                {
                    if (creation.FindPart(next) is not null && creation.FireOf(next) <= 0)
                        Ignite(creation, next);
                }
            }
            Scorch(centre, strength, dt);
            if (settings.PartDamage > 0)
                survival.World.DamagePart(creation, part, settings.PartDamage * strength * dt);
        }
    }

    /// <summary>The site fires, for a save: every lit site, out ones at 0.</summary>
    public IReadOnlyList<SavedSiteFire> SaveSites() =>
        [.. _sites.Values.Select(s => new SavedSiteFire(s.Site, s.Marker, s.Strength))];

    /// <summary>Puts back the site fires of a save; sites not lit yet take these values when they are.</summary>
    public void LoadSites(IEnumerable<SavedSiteFire> saved)
    {
        foreach (var s in saved)
        {
            var strength = float.IsFinite(s.Strength) ? Math.Clamp(s.Strength, 0, 1) : 1f;
            if (_sites.TryGetValue((s.Site, s.Marker), out var site))
                site.Strength = strength;
            else
                _loaded[(s.Site, s.Marker)] = strength;
        }
    }

    // Burns the living players whose feet are in the fire.
    private void Scorch(Vector3 at, float strength, float dt)
    {
        if (settings.PlayerDamage <= 0)
            return;
        foreach (var peer in survival.Players.ToList())
        {
            if (survival.World.FeetOf(peer) is { } feet && Vector3.Distance(feet, at) <= settings.PlayerRadius)
                survival.Damage(peer, settings.PlayerDamage * strength * dt, "fire");
        }
    }

    private int Count() => _sites.Values.Count(s => s.Strength > 0) + survival.World.AllCreations.Sum(c => c.Burning.Count());

    private static FireView View(SiteFire s) =>
        new($"site:{s.Site}:{s.Marker}", s.Position.ToVec3(), s.Strength, Poi: s.Poi, Site: s.Site, Marker: s.Marker);

    private static FireView View(Creation creation, int part) =>
        new($"part:{creation.Id}:{part}", PartCentre(creation, part).ToVec3(), creation.FireOf(part), creation.Id, part);

    private static Vector3 PartCentre(Creation creation, int partId)
    {
        var part = creation.FindPart(partId)!;
        var pose = creation.BodyPose(creation.BodyIndexOf(partId));
        return pose.Apply(PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToNumerics());
    }
}
