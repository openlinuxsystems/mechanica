using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.World;

/// <summary>
/// The opening cinematic (<c>game/data/opening.json</c>): the intact SV-04 flies in over the forest, its port engine
/// housing catches fire, an explosion tears its cargo spine open and blows the housing off, the containers break off the
/// racks and fall, bursting where they land, and the ship ploughs into the crash site's clearing and breaks in four,
/// every piece coming to rest exactly as the wreck lies; then the camera cuts to the player getting up at the spawn. The
/// host sees it the first time a new world starts. Times are seconds from its start; places are metres relative to the
/// POI's centre on its pad (x east, y up, z south), as POI prefabs give them. Engine settings: they ship with the build
/// and are not moddable. <see cref="OpeningScene"/> samples it.
/// </summary>
public sealed record OpeningSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The POI it plays at (qualified).</summary>
    public string Poi { get; init; } = "vanilla:crash_site";

    /// <summary>
    /// The wreck's pieces (POI models): each flies as part of the intact ship until the break, then comes to rest where
    /// its model lies.
    /// </summary>
    public List<OpeningPiece> Pieces { get; init; } = [];

    /// <summary>The POI model of the engine housing the explosion tears off, lying where it fell.</summary>
    public string HousingModel { get; init; } = "meshes/structures/sv04_housing.glb";

    /// <summary>The housing's centre on the intact ship (Blender frame: x, y forward, z up; the ship's origin).</summary>
    public float[] HousingOnShip { get; init; } = [0f, 0f, 0f];

    /// <summary>Where the housing model holds the housing relative to its origin (Blender frame): lying upside down.</summary>
    public OpeningPlacement HousingLying { get; init; } = new();

    /// <summary>The containers that break off in the explosion and burst where they land.</summary>
    public OpeningContainers Containers { get; init; } = new();

    /// <summary>Where the explosion tears the cargo spine open, on the intact ship (Blender frame).</summary>
    public float[] ExplodeAt { get; init; } = [0f, 0f, 0f];

    /// <summary>
    /// Other POI models (their <c>file</c>s) kept out of sight until the ship settles. The pieces, the housing and the
    /// containers are always hidden while their copies fly.
    /// </summary>
    public List<string> HiddenModels { get; init; } = [];

    /// <summary>World objects (ids, such as <c>loot_crate</c>) within <see cref="HideRadius"/> kept out of sight until the ship settles.</summary>
    public List<string> HiddenObjects { get; init; } = [];

    /// <summary>
    /// Metres from the POI's centre within which <see cref="HiddenObjects"/> and the site's fires are kept out of sight until
    /// the ship settles.
    /// </summary>
    public float HideRadius { get; init; } = 50f;

    /// <summary>Length of the whole cinematic, the player getting up included.</summary>
    public float Duration { get; init; } = 31f;

    /// <summary>
    /// Keys of the flying ship's origin, in time order. Between keys it moves on a smooth curve. The ship comes to rest
    /// at <see cref="Settle"/> exactly where the wreck's models lie, a key the scene adds itself.
    /// </summary>
    public List<OpeningKey> Ship { get; init; } = [];

    /// <summary>When the hull first touches the ground (dust and a heavy shake).</summary>
    public float Touchdown { get; init; } = 19.6f;

    /// <summary>When the pieces start to tear apart.</summary>
    public float Break { get; init; } = 20.2f;

    /// <summary>When every piece lies still where the wreck is.</summary>
    public float Settle { get; init; } = 24.5f;

    /// <summary>When the port engine housing starts to smoke and burn.</summary>
    public float HousingBurn { get; init; } = 8f;

    /// <summary>When the spine explodes: the housing blows off and the containers start to break away.</summary>
    public float Explode { get; init; } = 12f;

    /// <summary>When the housing hits the ground and lies where its model lies.</summary>
    public float HousingDown { get; init; } = 15f;

    /// <summary>Whole turns the housing tumbles while falling.</summary>
    public int HousingTurns { get; init; } = 2;

    /// <summary>Camera shots, in time order; together they cover the time until <see cref="Black"/> ends.</summary>
    public List<OpeningShot> Shots { get; init; } = [];

    /// <summary>Camera shakes: at a time, how strong (metres) and for how long.</summary>
    public List<OpeningShake> Shakes { get; init; } = [];

    /// <summary>One-shot sounds (sound events) at times, at the ship, the housing, the explosion or the camera.</summary>
    public List<OpeningSound> Sounds { get; init; } = [];

    /// <summary>The engines' drone that follows the ship until it touches down (a looping sound event), or null.</summary>
    public string? EngineLoop { get; init; } = "thruster_loop";

    /// <summary>Seconds the picture takes to come up from black at the start.</summary>
    public float FadeIn { get; init; } = 1f;

    /// <summary>From when to when the picture goes to black before the cut to the player.</summary>
    public float[] Black { get; init; } = [26f, 27.5f];

    /// <summary>Seconds the picture takes to come back up on the player after <see cref="Black"/>.</summary>
    public float PlayerFadeIn { get; init; } = 1f;

    /// <summary>Seconds a skip takes to go to black before it cuts to the player.</summary>
    public float SkipFade { get; init; } = 0.4f;

    /// <summary>Height of each letterbox bar, as a share of the screen's height.</summary>
    public float Letterbox { get; init; } = 0.1f;

    /// <summary>Reads settings JSON, falling back to the defaults for missing keys.</summary>
    public static OpeningSettings FromJson(string json)
    {
        OpeningSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<OpeningSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Invalid opening settings: {e.Message}", e);
        }
        return settings ?? throw new InvalidDataException("Opening settings are empty.");
    }

    /// <summary>Problems with the settings: keys out of order, shots that leave gaps, times past the end.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Ship.Count < 2)
            problems.Add("ship needs at least two keys");
        for (var i = 1; i < Ship.Count; i++)
        {
            if (Ship[i].T <= Ship[i - 1].T)
                problems.Add($"ship[{i}] is not after ship[{i - 1}]");
        }
        if (Ship.Count > 0 && Ship[^1].T >= Settle)
            problems.Add("the last ship key must come before settle");
        if (Pieces.Count == 0)
            problems.Add("pieces needs at least one piece");
        if (!(HousingBurn <= Explode && Explode < HousingDown))
            problems.Add("housing_burn, explode and housing_down must come in that order");
        if (Containers.Models.Count > 0 && !(Explode + Containers.Fall <= Touchdown))
            problems.Add("the containers must have time to fall between explode and touchdown");
        if (!(Touchdown <= Break && Break < Settle && Settle <= Black[0]))
            problems.Add("touchdown, break, settle and black must come in that order");
        if (Black.Length != 2 || Black[1] <= Black[0] || Black[1] + PlayerFadeIn > Duration)
            problems.Add("black must be [start, end], ending with time for the player to fade in");
        var at = 0f;
        foreach (var (shot, i) in Shots.Select((s, i) => (s, i)))
        {
            if (Math.Abs(shot.Start - at) > 1e-3f)
                problems.Add($"shots[{i}] starts at {shot.Start} s, not where the last one ended ({at} s)");
            if (shot.End <= shot.Start)
                problems.Add($"shots[{i}] ends before it starts");
            if ((shot.From is null) == (shot.Follow is null))
                problems.Add($"shots[{i}] needs either from or follow");
            at = shot.End;
        }
        if (at < Black[1])
            problems.Add($"the shots end at {at} s, before the picture is black");
        return problems;
    }
}

/// <summary>A piece of the wreck: its POI model and how <c>tools/structures</c> baked it.</summary>
public sealed record OpeningPiece
{
    /// <summary>The POI model (its <c>file</c>); the cinematic flies a copy of it.</summary>
    public string Model { get; init; } = "";

    /// <summary>The point of the intact ship (Blender frame) the piece turned about.</summary>
    public float[] Pivot { get; init; } = [0f, 0f, 0f];

    /// <summary>Where the pivot came to rest and how the piece turned (Blender frame), relative to the wreck's origin.</summary>
    public OpeningPlacement Wreck { get; init; } = new();
}

/// <summary>
/// The containers that break off in the explosion: the POI models whose file is one of <see cref="Models"/>, in the
/// prefab's order, ride in <see cref="Slots"/> until each breaks away and falls to where its model lies.
/// </summary>
public sealed record OpeningContainers
{
    /// <summary>The container models' files.</summary>
    public List<string> Models { get; init; } = [];

    /// <summary>Each container's middle on the intact ship (Blender frame), one per container model the POI places.</summary>
    public List<float[]> Slots { get; init; } = [];

    /// <summary>How far a container model's middle is above its origin.</summary>
    public float Middle { get; init; } = 1.05f;

    /// <summary>Seconds a container falls.</summary>
    public float Fall { get; init; } = 2.8f;

    /// <summary>Whole turns a container tumbles while falling.</summary>
    public int Turns { get; init; } = 1;

    /// <summary>The sound event each plays where it lands, or null.</summary>
    public string? Land { get; init; }
}

/// <summary>A place and turn in Blender's frame, from <c>tools/structures</c>.</summary>
public sealed record OpeningPlacement
{
    /// <summary>Position in metres (x, y forward, z up).</summary>
    public float[] At { get; init; } = [0f, 0f, 0f];

    /// <summary>Rotation in degrees about x, y and z, applied in that order.</summary>
    public float[] Rotation { get; init; } = [0f, 0f, 0f];
}

/// <summary>Where the flying ship's origin is at a time, and how it is turned.</summary>
public sealed record OpeningKey
{
    /// <summary>Seconds from the start.</summary>
    public float T { get; init; }

    /// <summary>Position relative to the POI (x east, y up, z south).</summary>
    public float[] At { get; init; } = [0f, 0f, 0f];

    /// <summary>Heading in degrees, as POI models turn: 0 points the nose north (-z), 90 west.</summary>
    public float Yaw { get; init; }

    /// <summary>Nose up (positive) or down, in degrees.</summary>
    public float Pitch { get; init; }

    /// <summary>Roll in degrees: positive lifts the starboard side.</summary>
    public float Roll { get; init; }
}

/// <summary>
/// One camera shot. A fixed shot stands at <see cref="From"/> (moving to <see cref="FromEnd"/>); a follow shot rides with
/// the ship at <see cref="Follow"/> in the ship's heading frame (x starboard, y up, z astern). It looks at
/// <see cref="Look"/> (moving to <see cref="LookEnd"/>), or at the ship when that is null.
/// </summary>
public sealed record OpeningShot
{
    /// <summary>When the shot starts.</summary>
    public float Start { get; init; }

    /// <summary>When it ends (the next one starts).</summary>
    public float End { get; init; }

    /// <summary>Where a fixed shot's camera stands, relative to the POI.</summary>
    public float[]? From { get; init; }

    /// <summary>Where it has moved to by the end; null keeps it still.</summary>
    public float[]? FromEnd { get; init; }

    /// <summary>A follow shot's camera, relative to the ship in its heading frame.</summary>
    public float[]? Follow { get; init; }

    /// <summary>What it looks at, relative to the POI; null looks at the ship.</summary>
    public float[]? Look { get; init; }

    /// <summary>Where the look has moved to by the end; null keeps it still.</summary>
    public float[]? LookEnd { get; init; }

    /// <summary>A follow shot's look point ahead of the ship, in its heading frame; null looks at the ship.</summary>
    public float[]? Ahead { get; init; }

    /// <summary>Vertical field of view in degrees.</summary>
    public float Fov { get; init; } = 55f;

    /// <summary>
    /// Metres the camera stays above the ground at least: where a world's hills rise above <see cref="From"/>, the camera
    /// rises over them (out of the grass, or high over the trees).
    /// </summary>
    public float Clear { get; init; } = 1.5f;
}

/// <summary>A camera shake.</summary>
public sealed record OpeningShake
{
    /// <summary>When it starts.</summary>
    public float At { get; init; }

    /// <summary>How far the camera shakes at first, in metres; it dies away.</summary>
    public float Strength { get; init; }

    /// <summary>How long it lasts.</summary>
    public float Seconds { get; init; } = 1f;
}

/// <summary>A one-shot sound.</summary>
public sealed record OpeningSound
{
    /// <summary>When it plays.</summary>
    public float At { get; init; }

    /// <summary>The sound event (a bare id is vanilla's).</summary>
    public string Event { get; init; } = "";

    /// <summary>Where: <c>ship</c>, <c>housing</c>, <c>explosion</c> or <c>camera</c>.</summary>
    public string On { get; init; } = "ship";
}
