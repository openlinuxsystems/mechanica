using System.Text.Json;
using Mechanica.Core.Actions;

namespace Mechanica.Core.Ui;

/// <summary>A place a player marked on their Logbook map (UI spec §5, §13).</summary>
/// <param name="Id">Stable id, <c>marker:N</c>, which is also its waypoint id.</param>
/// <param name="Name">What the player called it.</param>
/// <param name="X">East, in metres.</param>
/// <param name="Y">Height of the ground there, in metres.</param>
/// <param name="Z">South, in metres.</param>
public sealed record MapMarker(string Id, string Name, float X, float Y, float Z)
{
    /// <summary>The marker as a compass waypoint.</summary>
    public Waypoint ToWaypoint() => new(Id, Name, new System.Numerics.Vector3(X, Y, Z));
}

/// <summary>Arguments of <c>AddMapMarker</c>: where, and an optional name (a numbered "Marker N" without one).</summary>
public sealed record AddMapMarkerArgs(float X, float Y, float Z, string? Name = null);

/// <summary>Arguments of <c>RemoveMapMarker</c>.</summary>
public sealed record RemoveMapMarkerArgs(string Id);

/// <summary>The value of the map marker actions: the caller's markers now.</summary>
public sealed record MapMarkerList(IReadOnlyList<MapMarker> Markers);

/// <summary>
/// Each player's own map markers (UI spec §13: "Waypoint set from Logbook appears as a blue diamond in world and on
/// HUD compass"). Markers change only through <c>AddMapMarker</c> and <c>RemoveMapMarker</c>, which the host
/// validates: inside the world, at most <see cref="LogbookSettings.MaxMarkers"/> per player, names cut to
/// <see cref="LogbookSettings.MarkerNameLength"/>. They are saved with the world under the player's profile.
/// </summary>
public sealed class MapMarkers(LogbookSettings settings, float worldSize, float worldDepth)
{
    /// <summary>Markers for a square world <paramref name="worldSize"/> metres on a side.</summary>
    public MapMarkers(LogbookSettings settings, float worldSize)
        : this(settings, worldSize, worldSize)
    {
    }

    /// <summary>Marks a place on the caller's map.</summary>
    public const string AddMapMarker = "AddMapMarker";

    /// <summary>Removes one of the caller's markers.</summary>
    public const string RemoveMapMarker = "RemoveMapMarker";

    /// <summary>Reads the caller's markers.</summary>
    public const string GetMapMarkers = "GetMapMarkers";

    /// <summary>The prefix of every marker id.</summary>
    public const string IdPrefix = "marker:";

    private readonly Dictionary<int, List<MapMarker>> _markers = [];

    /// <summary>A player's markers changed: peer and the markers now.</summary>
    public event Action<int, IReadOnlyList<MapMarker>>? Changed;

    /// <summary>Registers the three actions.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<AddMapMarkerArgs>(AddMapMarker, ValidateAdd, (caller, args) =>
        {
            var list = ListOf(caller.Peer);
            var number = list.Select(m => int.TryParse(m.Id.AsSpan(IdPrefix.Length), out var n) ? n : 0).DefaultIfEmpty().Max() + 1;
            var name = Clean(args.Name);
            list.Add(new MapMarker(IdPrefix + number, name.Length == 0 ? $"Marker {number}" : name, args.X, args.Y, args.Z));
            return Report(caller.Peer, list);
        });
        dispatcher.Register<RemoveMapMarkerArgs>(RemoveMapMarker, (caller, args) =>
            caller.Source != ActionSource.Player ? "Only players have map markers."
            : MarkersOf(caller.Peer).Any(m => m.Id == args.Id) ? null : $"There is no marker '{args.Id}'.",
            (caller, args) =>
            {
                var list = ListOf(caller.Peer);
                list.RemoveAll(m => m.Id == args.Id);
                return Report(caller.Peer, list);
            });
        dispatcher.Register<NoArgs>(GetMapMarkers, (caller, _) => caller.Source == ActionSource.Player ? null : "Only players have map markers.",
            (caller, _) => Value(new MapMarkerList(MarkersOf(caller.Peer))), readOnly: true);
    }

    /// <summary>A player's markers, oldest first.</summary>
    public IReadOnlyList<MapMarker> MarkersOf(int peer) => _markers.TryGetValue(peer, out var list) ? list : [];

    /// <summary>A player's markers to save, or null when they have none.</summary>
    public IReadOnlyList<MapMarker>? SavedOf(int peer) => _markers.TryGetValue(peer, out var list) && list.Count > 0 ? [.. list] : null;

    /// <summary>Host: gives a returning player the markers saved for them, dropping any a save got wrong.</summary>
    public void Restore(int peer, IReadOnlyList<MapMarker>? saved)
    {
        var list = new List<MapMarker>();
        foreach (var marker in saved ?? [])
        {
            if (list.Count < settings.MaxMarkers && marker.Id.StartsWith(IdPrefix, StringComparison.Ordinal) && Inside(marker.X, marker.Y, marker.Z)
                && list.All(m => m.Id != marker.Id))
                list.Add(marker with { Name = Clean(marker.Name) });
        }
        if (list.Count == 0)
            _markers.Remove(peer);
        else
            _markers[peer] = list;
        Changed?.Invoke(peer, list);
    }

    /// <summary>Host: forgets a player who left.</summary>
    public void Forget(int peer) => _markers.Remove(peer);

    private List<MapMarker> ListOf(int peer)
    {
        if (!_markers.TryGetValue(peer, out var list))
            _markers[peer] = list = [];
        return list;
    }

    private string? ValidateAdd(ActionCaller caller, AddMapMarkerArgs args)
    {
        if (caller.Source != ActionSource.Player)
            return "Only players have map markers.";
        if (!Inside(args.X, args.Y, args.Z))
            return "Markers go inside the world.";
        if (MarkersOf(caller.Peer).Count >= settings.MaxMarkers)
            return $"You can have at most {settings.MaxMarkers} markers. Remove one first.";
        return null;
    }

    private bool Inside(float x, float y, float z) =>
        float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) && MathF.Abs(x) <= worldSize / 2 && MathF.Abs(z) <= worldDepth / 2;

    private string Clean(string? name)
    {
        var text = new string([.. (name ?? "").Where(c => !char.IsControl(c))]).Trim();
        return text.Length > settings.MarkerNameLength ? text[..settings.MarkerNameLength].TrimEnd() : text;
    }

    private JsonElement Report(int peer, List<MapMarker> list)
    {
        Changed?.Invoke(peer, list);
        return Value(new MapMarkerList([.. list]));
    }

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
