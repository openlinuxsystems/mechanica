using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Creations;

/// <summary>
/// Reads and writes a <see cref="Creation"/> as JSON (D8): saves, blueprints and late-join sync all use it.
/// The graph is stored (parts and edges); Bodies are derived on load. Each Body with a known world pose
/// gets a <c>bodies</c> entry naming its lowest part. Written in canonical form (parts by id, edges in
/// <see cref="Creation.Connections"/> order, bodies in <see cref="Creation.Bodies"/> order), so a
/// round-trip is byte for byte.
/// <code>
/// {
///   "format_version": 1,
///   "id": 7,
///   "authority_peer": 1,
///   "on_lift": true,
///   "parts": [ { "id": 1, "part": "vanilla:wood_block", "cell": [0, 0, 0], "rotation": "+x+y", "color": "#d02020", "damage": 10 } ],
///   "connections": [ { "type": "rotational", "a": 2, "a_socket": "rotor", "b": 3 } ],
///   "links": [ { "from": 4, "to": 2 } ],
///   "bodies": [ { "part": 1, "position": [0, 2.5, 0], "rotation": [0, 0, 0, 1] } ]
/// }
/// </code>
/// A body's <c>position</c> (metres) and <c>rotation</c> (unit quaternion <c>[x, y, z, w]</c>) place its
/// grid frame in the world (<see cref="Pose"/>). A moving body also stores <c>linear_velocity</c> (m/s)
/// and <c>angular_velocity</c> (rad/s), both in world space and both optional (missing means at rest),
/// so a late joiner or a reloaded save picks it up mid-motion (<see cref="BodyVelocity"/>).
/// <c>on_lift</c> (written only when true) marks a Creation a Lift holds; a part's <c>color</c> is its
/// Paint Tool colour, <c>settings</c> the settings changed from its defaults (<c>{ "mode": "xor" }</c>) and
/// <c>damage</c> the health it has lost, each written only when set. <c>links</c> are the Connect Tool's logic
/// links (chunk 10), written only when there are some. A container
/// or station part also stores its <c>inventory</c> (filled slots: <c>{ "slot": 0, "item": "vanilla:scrap_metal",
/// "count": 12 }</c>) and its <c>crafting</c> orders (<c>{ "recipe": "vanilla:chest", "count": 2, "progress": 1.5 }</c>),
/// each written only when not empty. A soil part stores what grows on it (<c>"soil": { "seed": "vanilla:carrot_seed",
/// "grown": 30, "water": 120 }</c>, seconds), written only when something is planted or it holds water.
/// </summary>
public static class CreationJson
{
    /// <summary>The format version this build reads and writes.</summary>
    public const int FormatVersion = 1;

    /// <summary>Serializes a Creation in canonical form.</summary>
    public static string Write(Creation creation)
    {
        var data = new CreationData
        {
            FormatVersion = FormatVersion,
            Id = creation.Id,
            AuthorityPeer = creation.AuthorityPeer,
            OnLift = creation.OnLift ? true : null,
            Parts = [.. creation.Parts.Select(p => new PartInstanceData
            {
                Id = p.Id,
                Part = p.Part,
                Cell = p.Cell,
                Rotation = p.Rotation,
                Color = p.Color,
                Settings = p.Settings is null ? null : new SortedDictionary<string, JsonElement>(p.Settings.ToDictionary(), StringComparer.Ordinal),
                Damage = creation.PartDamage(p.Id) is var damage and > 0 ? damage : null,
                Inventory = InventorySlots(creation.HoldsItems(p.Id) ? creation.InventoryOf(p.Id) : null),
                Crafting = creation.HoldsItems(p.Id) && creation.CraftingOf(p.Id) is { IsEmpty: false } queue
                    ? [.. queue.Jobs.Select(j => new CraftJobData { Recipe = j.Recipe, Count = j.Count, Progress = j.Progress > 0 ? j.Progress : null })]
                    : null,
                Soil = creation.SoilOf(p.Id) is { IsEmpty: false } soil
                    ? new SoilData { Seed = soil.Seed, Grown = soil.Seed is null ? null : soil.Grown, Water = soil.Water > 0 ? soil.Water : null }
                    : null,
                Extra = Sorted(p.Extra),
            })],
            Connections = [.. creation.Connections.Select(c => new ConnectionData
            {
                Type = c.Type,
                A = c.A,
                ASocket = c.ASocket,
                B = c.B,
                BSocket = c.BSocket,
            })],
            Links = creation.Links.Count == 0 ? null : [.. creation.Links.Select(l => new LinkData { From = l.From, To = l.To })],
            Bodies = BodyPoses(creation),
            Extra = Sorted(creation.Extra),
        };
        return JsonSerializer.Serialize(data, DataJson.WriteOptions) + "\n";
    }

    /// <summary>
    /// Parses and validates a saved Creation against <paramref name="catalog"/>. Every problem is added to
    /// <paramref name="diagnostics"/> (with <paramref name="file"/> as its location); returns null on any error.
    /// </summary>
    public static Creation? Read(string json, IPartCatalog catalog, string file, List<DataDiagnostic> diagnostics)
    {
        var sink = new DiagnosticSink(file, diagnostics);
        var data = ModLoader.ParseJson<CreationData>(json, sink);
        if (data is null)
            return null;
        const string invalid = DiagnosticCodes.CreationInvalid;
        KnownKeys.WarnUnknown(data.Extra, "$", sink, typeof(CreationData));

        if (data.FormatVersion is null)
            sink.Error(invalid, "$.format_version", "format_version is required.");
        else if (data.FormatVersion != FormatVersion)
            sink.Error(DiagnosticCodes.UnsupportedFormat, "$.format_version",
                $"format_version {data.FormatVersion} is not supported; this build reads {FormatVersion}.");
        if (data.Id is not { } id)
        {
            sink.Error(invalid, "$.id", "id is required.");
            id = 0;
        }
        var peer = data.AuthorityPeer ?? Creation.HostPeer;
        if (peer < 1)
            sink.Error(invalid, "$.authority_peer", "authority_peer must be 1 or more.");
        if (data.Parts is not { Count: > 0 })
            sink.Error(invalid, "$.parts", "a creation needs at least one part.");
        if (sink.ErrorCount > 0)
            return null;

        var creation = new Creation(id, catalog, peer) { Extra = data.Extra, OnLift = data.OnLift == true };
        var owner = new Dictionary<GridCell, int>();
        for (var i = 0; i < data.Parts!.Count; i++)
        {
            var path = $"$.parts[{i}]";
            if (ReadPart(data.Parts[i], path, catalog, sink) is not var (part, definition))
                continue;
            if (creation.FindPart(part.Id) is not null)
            {
                sink.Error(invalid, $"{path}.id", $"part id {part.Id} is used twice.");
                continue;
            }
            var cells = PartGeometry.Cells(definition, part.Cell, part.Rotation).ToList();
            var clashes = cells.Where(owner.ContainsKey).Take(1).ToList();
            if (clashes is [var clash] && owner[clash] is var other)
            {
                sink.Error(DiagnosticCodes.CreationOverlap, path, $"part {part.Id} overlaps part {other} at {clash}.");
                continue;
            }
            foreach (var c in cells)
                owner[c] = part.Id;
            creation.Load(part, definition);
            if (data.Parts[i].Damage is { } damage)
                creation.SetPartDamage(part.Id, damage);
            ReadStorage(data.Parts[i], path, creation, part.Id, sink);
            ReadSoil(data.Parts[i], path, creation, part.Id, sink);
        }
        if (sink.ErrorCount > 0)
            return null;

        var seen = new HashSet<Connection>();
        for (var i = 0; i < (data.Connections?.Count ?? 0); i++)
        {
            var path = $"$.connections[{i}]";
            var c = data.Connections![i];
            foreach (var key in c.Extra?.Keys ?? Enumerable.Empty<string>())
                sink.Warning(DiagnosticCodes.UnknownKey, $"{path}.{key}", $"unknown key '{key}' on a connection is ignored and not saved.");
            if (c.Type is not { } type || c.A is not { } a || c.B is not { } b)
            {
                sink.Error(invalid, path, "a connection needs type, a and b.");
                continue;
            }
            var connection = new Connection(type, a, b, c.ASocket, c.BSocket);
            if (!seen.Add(connection))
                sink.Error(DiagnosticCodes.CreationConnection, path, $"connection {a} → {b} is listed twice.");
            else if (!creation.TryConnect(connection, out var error))
                sink.Error(DiagnosticCodes.CreationConnection, path, error);
        }
        if (sink.ErrorCount > 0)
            return null;

        var pieces = creation.Components(includeJoints: true);
        if (pieces.Count > 1)
        {
            sink.Error(DiagnosticCodes.CreationDisconnected, "$.connections",
                $"the parts form {pieces.Count} separate pieces (lowest ids {string.Join(", ", pieces.Select(p => p[0]))}); a creation must be connected.");
            return null;
        }
        for (var i = 0; i < (data.Links?.Count ?? 0); i++)
        {
            var path = $"$.links[{i}]";
            var l = data.Links![i];
            foreach (var key in l.Extra?.Keys ?? Enumerable.Empty<string>())
                sink.Warning(DiagnosticCodes.UnknownKey, $"{path}.{key}", $"unknown key '{key}' on a link is ignored and not saved.");
            if (l.From is not { } from || l.To is not { } to)
                sink.Error(invalid, path, "a link needs from and to.");
            else if (!creation.TryLink(from, to, out var error))
                sink.Error(DiagnosticCodes.CreationConnection, path, error);
        }
        ReadBodies(data.Bodies, creation, sink);
        return sink.ErrorCount > 0 ? null : creation;
    }

    private static List<BodyPoseData>? BodyPoses(Creation creation)
    {
        var poses = new List<BodyPoseData>();
        foreach (var body in creation.Bodies.Where(b => creation.HasBodyPose(b.Index)))
        {
            var pose = creation.BodyPose(body.Index);
            var velocity = creation.GetBodyVelocity(body.Index);
            poses.Add(new BodyPoseData
            {
                Part = body.PartIds[0],
                Position = pose.Position.ToVec3(),
                Rotation = pose.Rotation,
                LinearVelocity = velocity.IsZero ? null : velocity.Linear.ToVec3(),
                AngularVelocity = velocity.IsZero ? null : velocity.Angular.ToVec3(),
            });
        }
        return poses.Count > 0 ? poses : null;
    }

    private static void ReadBodies(List<BodyPoseData>? bodies, Creation creation, DiagnosticSink sink)
    {
        const string invalid = DiagnosticCodes.CreationInvalid;
        var claimed = new Dictionary<int, int>();
        for (var i = 0; i < (bodies?.Count ?? 0); i++)
        {
            var path = $"$.bodies[{i}]";
            var b = bodies![i];
            foreach (var key in b.Extra?.Keys ?? Enumerable.Empty<string>())
                sink.Warning(DiagnosticCodes.UnknownKey, $"{path}.{key}", $"unknown key '{key}' on a body is ignored and not saved.");
            if (b.Part is not { } part || b.Position is not { } position || b.Rotation is not { } rotation)
            {
                sink.Error(invalid, path, "a body needs part, position and rotation.");
                continue;
            }
            if (creation.FindPart(part) is null)
            {
                sink.Error(invalid, $"{path}.part", $"body names part {part}, which is not in the creation.");
                continue;
            }
            var length = rotation.Length();
            if (!float.IsFinite(length) || MathF.Abs(length - 1) > 1e-3f)
            {
                sink.Error(invalid, $"{path}.rotation", $"rotation must be a unit quaternion [x, y, z, w]; its length is {length}.");
                continue;
            }
            if (!IsFinite(position))
            {
                sink.Error(invalid, $"{path}.position", "position must be finite.");
                continue;
            }
            if (b.LinearVelocity is { } linear && !IsFinite(linear))
            {
                sink.Error(invalid, $"{path}.linear_velocity", "linear_velocity must be finite.");
                continue;
            }
            if (b.AngularVelocity is { } angular && !IsFinite(angular))
            {
                sink.Error(invalid, $"{path}.angular_velocity", "angular_velocity must be finite.");
                continue;
            }
            var body = creation.BodyIndexOf(part);
            if (claimed.TryGetValue(body, out var first))
            {
                sink.Error(invalid, $"{path}.part", $"parts {first} and {part} are in the same body, so only one may carry its pose.");
                continue;
            }
            claimed[body] = part;
            creation.SetBodyPose(body, new Pose(position.ToNumerics(), Quaternion.Normalize(rotation)));
            var velocity = new BodyVelocity(b.LinearVelocity?.ToNumerics() ?? Vector3.Zero, b.AngularVelocity?.ToNumerics() ?? Vector3.Zero);
            if (!velocity.IsZero)
                creation.SetBodyVelocity(body, velocity);
        }
    }

    private static List<SlotData>? InventorySlots(Inventory? inventory)
    {
        if (inventory is null || inventory.IsEmpty)
            return null;
        return [.. inventory.Slots.Select((s, i) => (s, i)).Where(x => x.s is not null)
            .Select(x => new SlotData { Slot = x.i, Item = x.s!.Item, Count = x.s.Count })];
    }

    private static void ReadStorage(PartInstanceData data, string path, Creation creation, int partId, DiagnosticSink sink)
    {
        const string invalid = DiagnosticCodes.CreationInvalid;
        if (data.Inventory is { Count: > 0 } slots)
        {
            if (creation.InventoryOf(partId) is not { } inventory)
            {
                sink.Error(invalid, $"{path}.inventory", $"part '{data.Part}' has no inventory.");
            }
            else
            {
                for (var i = 0; i < slots.Count; i++)
                {
                    var slotPath = $"{path}.inventory[{i}]";
                    var slot = slots[i];
                    if (slot.Slot is not { } index || slot.Item is null || slot.Count is not { } count)
                        sink.Error(invalid, slotPath, "a slot needs slot, item and count.");
                    else if (index < 0 || index >= inventory.SlotCount)
                        sink.Error(invalid, $"{slotPath}.slot", $"slot must be 0 to {inventory.SlotCount - 1} (got {index}).");
                    else if (inventory[index] is not null)
                        sink.Error(invalid, $"{slotPath}.slot", $"slot {index} is listed twice.");
                    else if (inventory.CheckStack(new ItemStack(Identifiers.Qualify(slot.Item), count)) is { } error)
                        sink.Error(invalid, slotPath, error);
                    else
                        inventory.SetSlot(index, new ItemStack(slot.Item, count));
                }
            }
        }
        if (data.Crafting is { Count: > 0 } jobs)
        {
            if (creation.CraftingOf(partId) is not { } queue)
            {
                sink.Error(invalid, $"{path}.crafting", $"part '{data.Part}' is not a crafting station.");
                return;
            }
            var read = new List<CraftJob>();
            for (var i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                var jobPath = $"{path}.crafting[{i}]";
                if (!PartValidator.IsItemRef(job.Recipe))
                    sink.Error(invalid, $"{jobPath}.recipe", "recipe must be a recipe id or 'mod_id:recipe_id'.");
                else if (job.Count is not ({ } count and >= 1))
                    sink.Error(invalid, $"{jobPath}.count", "count must be 1 or more.");
                else if (job.Progress is { } progress && !(float.IsFinite(progress) && progress >= 0))
                    sink.Error(invalid, $"{jobPath}.progress", "progress must be 0 or more seconds.");
                else
                    read.Add(new CraftJob(Identifiers.Qualify(job.Recipe!), count, job.Progress ?? 0));
            }
            queue.Load(read);
        }
    }

    private static void ReadSoil(PartInstanceData data, string path, Creation creation, int partId, DiagnosticSink sink)
    {
        const string invalid = DiagnosticCodes.CreationInvalid;
        if (data.Soil is not { } soil)
            return;
        if (creation.SoilOf(partId) is not { } state)
            sink.Error(invalid, $"{path}.soil", $"part '{data.Part}' is not soil.");
        else if (soil.Seed is not null && !PartValidator.IsItemRef(soil.Seed))
            sink.Error(invalid, $"{path}.soil.seed", "seed must be an item id or 'mod_id:item_id'.");
        else if (soil.Grown is { } grown && !(float.IsFinite(grown) && grown >= 0))
            sink.Error(invalid, $"{path}.soil.grown", "grown must be 0 or more seconds.");
        else if (soil.Water is { } water && !(float.IsFinite(water) && water >= 0))
            sink.Error(invalid, $"{path}.soil.water", "water must be 0 or more seconds.");
        else
            state.Load(soil.Seed is null ? null : Identifiers.Qualify(soil.Seed), soil.Grown ?? 0, soil.Water ?? 0);
    }

    private static bool IsFinite(Vec3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static (PartInstance, PartDefinition)? ReadPart(PartInstanceData data, string path, IPartCatalog catalog, DiagnosticSink sink)
    {
        const string invalid = DiagnosticCodes.CreationInvalid;
        var errors = sink.ErrorCount;
        if (data.Id is not ({ } id and >= 1))
            sink.Error(invalid, $"{path}.id", "part id must be 1 or more.");
        if (data.Cell is null)
            sink.Error(invalid, $"{path}.cell", "cell is required.");
        PartDefinition? definition = null;
        if (string.IsNullOrEmpty(data.Part))
            sink.Error(invalid, $"{path}.part", "part is required.");
        else if ((definition = catalog.FindDefinition(data.Part)) is null)
            sink.Error(DiagnosticCodes.CreationUnknownPart, $"{path}.part", $"unknown part '{data.Part}'; is its mod loaded?");
        else if (!definition.IsPlaceable)
            sink.Error(invalid, $"{path}.part", $"'{data.Part}' cannot be placed.");
        if (data.Color is not null && !HexColor.TryParse(data.Color, out _))
            sink.Error(invalid, $"{path}.color", $"color must be \"#rrggbb\" (got \"{data.Color}\").");
        if (data.Damage is { } damage && !(float.IsFinite(damage) && damage >= 0))
            sink.Error(invalid, $"{path}.damage", $"damage must be a number of 0 or more (got {damage}).");
        if (definition is not null && data.Settings is { } settings && Logic.PartSettings.Check(definition, settings) is { } bad)
            sink.Error(invalid, $"{path}.settings", bad);
        if (sink.ErrorCount > errors)
            return null;
        KnownKeys.WarnUnknown(data.Extra, path, sink, typeof(PartInstanceData));
        var part = new PartInstance(data.Id!.Value, Identifiers.Qualify(data.Part!), data.Cell!.Value, data.Rotation ?? GridRotation.Identity)
        {
            Color = data.Color?.ToLowerInvariant(),
            Settings = data.Settings is { Count: > 0 } s ? new SortedDictionary<string, JsonElement>(s, StringComparer.Ordinal) : null,
            Extra = data.Extra,
        };
        return (part, definition!);
    }

    private static Dictionary<string, JsonElement>? Sorted(IReadOnlyDictionary<string, JsonElement>? extra) =>
        extra?.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);

    internal sealed record CreationData
    {
        public int? FormatVersion { get; init; }

        public int? Id { get; init; }

        public int? AuthorityPeer { get; init; }

        public bool? OnLift { get; init; }

        public List<PartInstanceData>? Parts { get; init; }

        public List<ConnectionData>? Connections { get; init; }

        public List<LinkData>? Links { get; init; }

        public List<BodyPoseData>? Bodies { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    internal sealed record PartInstanceData
    {
        public int? Id { get; init; }

        public string? Part { get; init; }

        public GridCell? Cell { get; init; }

        public GridRotation? Rotation { get; init; }

        public string? Color { get; init; }

        public SortedDictionary<string, JsonElement>? Settings { get; init; }

        public float? Damage { get; init; }

        public List<SlotData>? Inventory { get; init; }

        public List<CraftJobData>? Crafting { get; init; }

        public SoilData? Soil { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    internal sealed record SlotData
    {
        public int? Slot { get; init; }

        public string? Item { get; init; }

        public int? Count { get; init; }
    }

    internal sealed record CraftJobData
    {
        public string? Recipe { get; init; }

        public int? Count { get; init; }

        public float? Progress { get; init; }
    }

    internal sealed record SoilData
    {
        public string? Seed { get; init; }

        public float? Grown { get; init; }

        public float? Water { get; init; }
    }

    internal sealed record ConnectionData
    {
        public ConnectionType? Type { get; init; }

        public int? A { get; init; }

        public string? ASocket { get; init; }

        public int? B { get; init; }

        public string? BSocket { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    internal sealed record LinkData
    {
        public int? From { get; init; }

        public int? To { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    internal sealed record BodyPoseData
    {
        public int? Part { get; init; }

        public Vec3? Position { get; init; }

        [JsonConverter(typeof(QuaternionJsonConverter))]
        public Quaternion? Rotation { get; init; }

        public Vec3? LinearVelocity { get; init; }

        public Vec3? AngularVelocity { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }
}
