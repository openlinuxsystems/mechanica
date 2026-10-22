using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.World;
using Mechanica.Game.Fire;
using Mechanica.Game.Player;
using Mechanica.Game.Sound;
using Mechanica.Game.Ui;

namespace Mechanica.Game.World;

/// <summary>
/// The opening cinematic (<see cref="OpeningSettings"/>, <c>game/data/opening.json</c>; docs/art-direction.md). The host
/// sees it the first time a world it started from the menu loads; players who join later spawn straight in. The intact
/// SV-04 (copies of the wreck's own models) flies in over the forest, its port engine housing catches fire, the cargo
/// spine blows open, the housing and the containers fall away, and the ship comes down in the crash site's clearing and
/// breaks in four, every piece ending exactly where the wreck's models lie.
/// The picture goes black and comes back on the player getting up at the spawn. Meanwhile the real wreck, the cargo it
/// spills and the site's fires are out of sight until the ship settles, the HUD is hidden, quest dialogue waits
/// (<see cref="Playing"/>) and the player stands still. Jump or Pause skips to the player. Nothing of it is sent to other
/// peers: the world itself never changes.
/// </summary>
public partial class OpeningCinematic : Node3D
{
    /// <summary>Where the settings are read from.</summary>
    public const string SettingsPath = "res://data/opening.json";

    private static readonly Gradient DustColors = new()
    {
        Offsets = [0f, 0.25f, 1f],
        Colors = [new Color(0.45f, 0.4f, 0.32f, 0f), new Color(0.42f, 0.37f, 0.3f, 0.55f), new Color(0.5f, 0.47f, 0.42f, 0f)],
    };

    private static readonly Gradient SmokeColors = new()
    {
        Offsets = [0f, 0.2f, 1f],
        Colors = [new Color(0.12f, 0.11f, 0.1f, 0f), new Color(0.16f, 0.15f, 0.14f, 0.5f), new Color(0.3f, 0.3f, 0.3f, 0f)],
    };

    private static readonly Gradient FlameColors = new()
    {
        Offsets = [0f, 0.35f, 1f],
        Colors = [new Color(1f, 0.92f, 0.55f), new Color(1f, 0.5f, 0.12f, 0.9f), new Color(0.55f, 0.1f, 0.04f, 0f)],
    };

    private OpeningScene _scene = null!;
    private Camera3D _camera = null!;
    private Node3D?[] _pieces = [];
    private Node3D? _housing;
    private Node3D?[] _containers = [];
    private CpuParticles3D[] _impacts = [];
    private bool[] _landed = [];
    private Node3D _housingFire = null!;
    private CpuParticles3D _housingFlames = null!;
    private Node3D _stub = null!;
    private CpuParticles3D _burst = null!;
    private OmniLight3D _flash = null!;
    private CpuParticles3D _dust = null!;
    private ColorRect _top = null!;
    private ColorRect _bottom = null!;
    private ColorRect _black = null!;
    private Label _hint = null!;
    private System.Numerics.Vector3 _housingCentre;
    private OpeningCamera _lastCamera;
    private float _t;
    private float? _skipFrom;
    private bool _wasHidden;
    private bool _revealed;
    private bool _blown;
    private int _nextSound;
    private int _hiddenRevision = -1;
    private SoundPlayer.Voice? _engine;
    // Loops from the sound list (the camera's air whoosh): they never end by themselves, so they stop at the hand-off.
    private readonly List<SoundPlayer.Voice> _loops = [];

    /// <summary>The streamed world, for the crash site and its models. Set before adding to the tree.</summary>
    public WorldStreamer World { get; init; } = null!;

    /// <summary>The local player, who gets up at the end. Set before adding to the tree.</summary>
    public PlayerController Player { get; init; } = null!;

    /// <summary>The scene it plays (<see cref="SceneFor"/>). Set before adding to the tree.</summary>
    public OpeningScene Scene
    {
        get => _scene;
        init => _scene = value;
    }

    /// <summary>The site fires, kept out of sight until the ship settles; null hides none.</summary>
    public FireSync? Fire { get; init; }

    /// <summary>Sounds; null plays none.</summary>
    public SoundSync? Sounds { get; init; }

    /// <summary>The player's keyboard and mouse, turned to face the wreck at the end; null when there is none.</summary>
    public Func<LocalPlayerInput?> Input { get; init; } = () => null;

    /// <summary>False in automated tests: keys are not read.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>True until it ends: the HUD is hidden, dialogue waits and the player stands still.</summary>
    public bool Playing { get; private set; }

    /// <summary>Seconds since it started (a skip jumps to the cut to the player).</summary>
    public float Time => _t;

    /// <summary>True once the camera is the player's own again.</summary>
    public bool HandedOff { get; private set; }

    /// <summary>The cinematic's camera, current until the hand-off.</summary>
    public Camera3D Camera => _camera;

    /// <summary>The flying copies of the pieces, the housing and the containers (empty and null after the hand-off), for tests.</summary>
    public (IReadOnlyList<Node3D?> Pieces, Node3D? Housing, IReadOnlyList<Node3D?> Containers) Copies => (_pieces, _housing, _containers);

    /// <summary>The skip hint on screen.</summary>
    public string HintText => _hint.Visible ? _hint.Text : "";

    /// <summary>How dark the picture is now, 0 to 1.</summary>
    public float Darkness => _black.Color.A;

    /// <summary>Raised when it ends.</summary>
    public event Action? Finished;

    /// <summary>Reads <see cref="SettingsPath"/>, failing loudly on bad data.</summary>
    public static OpeningSettings LoadSettings()
    {
        var settings = OpeningSettings.FromJson(Godot.FileAccess.GetFileAsString(SettingsPath));
        if (settings.Problems() is { Count: > 0 } problems)
            throw new InvalidDataException($"{SettingsPath}: {string.Join("; ", problems)}");
        return settings;
    }

    /// <summary>
    /// The scene for the crash site the player at <paramref name="at"/> starts in, or null when the world has none there
    /// (a mod moved the spawn, or replaced the site without the wreck's models).
    /// </summary>
    public static OpeningScene? SceneFor(WorldGenerator world, OpeningSettings settings, Vector3 at)
    {
        var site = world.Pois.FirstOrDefault(p => p.Poi.QualifiedId == settings.Poi
            && new Vector2(at.X - p.Centre.X, at.Z - p.Centre.Y).Length() <= p.Radius);
        if (site is null)
            return null;
        try
        {
            return new OpeningScene(settings, site, world.HeightAt);
        }
        catch (InvalidOperationException e)
        {
            GD.PushWarning($"[opening] {e.Message}");
            return null;
        }
    }

    public override void _Ready()
    {
        Playing = true;
        _wasHidden = HudRoot.Hidden;
        HudRoot.Hidden = true;
        Player.Visible = false;
        var settings = _scene.Settings;
        var models = _scene.Poi.Poi.Prefab.Models!;
        _pieces = [.. _scene.PieceIndices.Select((index, i) => Copy(models[index], $"Piece{i}"))];
        _housing = Copy(models[_scene.HousingIndex], "Housing");
        _containers = [.. _scene.ContainerIndices.Select((index, i) => Copy(models[index], $"Container{i}"))];
        _landed = new bool[_containers.Length];
        _housingCentre = OpeningScene.Blender(settings.HousingLying).Position;
        _camera = new Camera3D { Name = "Camera", Near = 0.1f, Far = Player.Camera.Far };
        AddChild(_camera);
        _camera.MakeCurrent();
        BuildEffects();
        BuildOverlay();
        if (settings.EngineLoop is { } loop && Sounds?.Registry.Find(loop) is { } engine)
            _engine = Sounds.Player.Play(engine, _scene.Ship(0f).Position.ToGodot(), () => HandedOff ? null : _scene.Ship(_t).Position.ToGodot());
        Animate();
        Overlay();
    }

    public override void _ExitTree()
    {
        if (!Playing)
            return;
        // Left mid-way (the world closed): give everything back.
        Playing = false;
        HudRoot.Hidden = _wasHidden;
        if (IsInstanceValid(Player))
            Player.Visible = true;
        if (Sounds is not null && IsInstanceValid(Sounds))
            StopSounds();
    }

    public override void _Process(double delta)
    {
        if (!Playing)
            return;
        _t += (float)delta;
        var settings = _scene.Settings;
        if (!HandedOff && _skipFrom is { } from && _t - from >= settings.SkipFade)
            _t = settings.Black[1];
        if (!HandedOff && _t >= settings.Black[1])
            HandOff();
        if (!HandedOff)
            Animate();
        Overlay();
        if (_t >= settings.Duration)
            Finish();
    }

    // Before the hand-off every key and click is the cinematic's: Jump or Pause skips, the rest does nothing.
    public override void _Input(InputEvent @event)
    {
        if (!ReadDeviceInput || !Playing || HandedOff || @event is not (InputEventKey or InputEventMouseButton or InputEventMouseMotion))
            return;
        if (GameInput.Pressed(@event, InputActions.Jump) || GameInput.Pressed(@event, InputActions.Pause))
            Skip();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Goes to black and on to the player getting up.</summary>
    public void Skip()
    {
        if (Playing && !HandedOff && _skipFrom is null)
            _skipFrom = _t;
    }

    // A copy of a POI model to fly, or null when its file doesn't load.
    private Node3D? Copy(PoiModel model, string name)
    {
        if (World.Generator.Data.ModelFileOf(_scene.Poi.Poi, model) is not { } file || StructureModel.Of(file, model.File!) is not { } built)
            return null;
        var node = built.Instantiate(name, Transform3D.Identity);
        AddChild(node);
        return node;
    }

    private void Animate()
    {
        var settings = _scene.Settings;
        var pieces = _scene.Pieces(_t);
        var settled = _t >= settings.Settle;
        for (var i = 0; i < _pieces.Length; i++)
            Place(_pieces[i], pieces.Pieces[i], !settled);
        Place(_housing, pieces.Housing, !settled);
        for (var i = 0; i < _containers.Length; i++)
            Place(_containers[i], pieces.Containers[i], !settled);
        Hide(!settled);

        // The housing burns from when it catches fire to the end, harder once it has blown.
        _housingFire.GlobalPosition = pieces.Housing.Apply(_housingCentre).ToGodot();
        foreach (var particles in _housingFire.GetChildren().OfType<CpuParticles3D>())
            particles.Emitting = _t >= settings.HousingBurn;
        _housingFlames.ScaleAmountMax = _t >= settings.Explode ? 1.8f : 1.1f;
        // The torn spine trails smoke until the ship is down.
        var blast = _scene.Blast(_t).ToGodot();
        _stub.GlobalPosition = blast;
        foreach (var particles in _stub.GetChildren().OfType<CpuParticles3D>())
            particles.Emitting = _t >= settings.Explode && _t < settings.Settle + 1.5f;
        if (!_blown && _t >= settings.Explode)
        {
            _blown = true;
            _burst.GlobalPosition = blast;
            _burst.Restart();
        }
        _flash.GlobalPosition = blast;
        _flash.LightEnergy = _blown ? 12f * Mathf.Max(0f, 1f - (_t - settings.Explode) / 0.8f) : 0f;
        // Each container throws up dirt where it lands.
        for (var i = 0; i < _landed.Length; i++)
        {
            if (_landed[i] || _t < _scene.Releases[i] + settings.Containers.Fall)
                continue;
            _landed[i] = true;
            _impacts[i].GlobalPosition = _scene.ContainerWrecks[i].Position.ToGodot();
            _impacts[i].Restart();
            if (settings.Containers.Land is { } land && Sounds?.Registry.Find(land) is { } e)
                Sounds.Player.Play(e, _impacts[i].GlobalPosition);
        }
        // Dust from under the hull while it hits and slides.
        _dust.GlobalPosition = pieces.Ship.Position.ToGodot();
        _dust.Emitting = _t >= settings.Touchdown && _t < settings.Settle;

        if (_scene.Camera(_t) is { } camera)
            _lastCamera = camera;
        var shake = _scene.Shake(_t);
        var jitter = shake * new Vector3(Mathf.Sin(_t * 37f), Mathf.Sin(_t * 43f + 1f), Mathf.Sin(_t * 29f + 2f));
        var at = _lastCamera.Position.ToGodot() + jitter;
        var target = _lastCamera.Target.ToGodot() + jitter * 0.5f;
        if (at.DistanceSquaredTo(target) > 1e-4f)
            _camera.LookAtFromPosition(at, target, Mathf.Abs((target - at).Normalized().Y) > 0.99f ? Vector3.Forward : Vector3.Up);
        _camera.Fov = _lastCamera.Fov;

        while (_nextSound < settings.Sounds.Count && settings.Sounds[_nextSound].At <= _t)
            Play(settings.Sounds[_nextSound++]);
        if (_engine is not null && _t >= settings.Touchdown)
            StopEngine();
    }

    private static void Place(Node3D? node, Core.Creations.Pose pose, bool shown)
    {
        if (node is null)
            return;
        node.GlobalTransform = pose.ToTransform();
        node.Visible = shown;
    }

    private void Play(OpeningSound sound)
    {
        if (Sounds?.Registry.Find(sound.Event) is not { } e)
            return;
        var settings = _scene.Settings;
        Func<Vector3?> track = sound.On switch
        {
            "housing" => () => HandedOff ? null : _scene.Housing(_t).Apply(_housingCentre).ToGodot(),
            "explosion" => () => HandedOff ? null : _scene.Blast(_t).ToGodot(),
            "camera" => () => HandedOff ? null : _camera.GlobalPosition,
            _ => () => HandedOff ? null : _scene.Ship(_t).Position.ToGodot(),
        };
        if (Sounds.Player.Play(e, track() ?? Vector3.Zero, track) is { Loops: true } loop)
            _loops.Add(loop);
    }

    private void StopEngine()
    {
        if (_engine is not null && Sounds is not null)
            Sounds.Player.Stop(_engine);
        _engine = null;
    }

    private void StopSounds()
    {
        StopEngine();
        foreach (var loop in _loops)
            Sounds?.Player.Stop(loop);
        _loops.Clear();
    }

    // Keeps the wreck's models, the cargo it spills and the site's fires out of sight (or shows them again). Chunks are
    // only gone through again when the streamed set changes.
    private void Hide(bool hide)
    {
        if (!hide && _revealed)
            return;
        if (hide && Fire is not null)
            foreach (var fire in Fire.Nodes.Values)
                if (fire.View is { Part: null } view && _scene.NearCentre(view.Position.ToGodot().ToNumerics()))
                    fire.Visible = false;
        if (hide && _hiddenRevision == World.Revision)
            return;
        _hiddenRevision = World.Revision;
        var reach = _scene.Settings.HideRadius + World.Streamer.Settings.ChunkSize;
        foreach (var chunk in World.Chunks.Values.Concat(World.Scenery.Values))
        {
            var middle = chunk.Position + new Vector3(chunk.Data.Size / 2f, 0, chunk.Data.Size / 2f);
            if (!_scene.NearCentre(middle.ToNumerics()) && new Vector2(middle.X - _scene.Poi.Centre.X, middle.Z - _scene.Poi.Centre.Y).Length() > reach)
                continue;
            foreach (var index in _scene.HiddenModels)
                if (chunk.Model(_scene.Poi.Index, index) is { } model)
                    model.Visible = !hide;
            chunk.HideObjects(hide ? _scene.Hides : null);
        }
        if (hide)
            return;
        _revealed = true;
        if (Fire is not null)
            foreach (var fire in Fire.Nodes.Values)
                if (fire.View is { Part: null })
                    fire.Visible = true;
    }

    private void HandOff()
    {
        HandedOff = true;
        StopSounds();
        Hide(false);
        foreach (var node in new Node?[] { _housing, _housingFire, _stub, _burst, _flash, _dust, _camera }.Concat(_pieces).Concat(_containers).Concat(_impacts))
            node?.QueueFree();
        (_pieces, _housing, _containers, _impacts) = ([], null, [], []);
        Player.Visible = true;
        Player.MakeCameraCurrent();
        // Up off the ground, facing the wreck.
        var toWreck = _scene.Wreck.Position.ToGodot() - Player.GlobalPosition;
        Input()?.Face(Mathf.Atan2(-toWreck.X, -toWreck.Z));
        (Player.Character as CharacterModel)?.Animator.GetUp();
        (Player.Arms as CharacterModel)?.Animator.GetUp();
    }

    private void Finish()
    {
        Playing = false;
        HudRoot.Hidden = _wasHidden;
        Finished?.Invoke();
        QueueFree();
    }

    private void Overlay()
    {
        var settings = _scene.Settings;
        var dark = _scene.Black(_t);
        if (_skipFrom is { } from && !HandedOff)
            dark = Mathf.Max(dark, Mathf.Clamp((_t - from) / settings.SkipFade, 0f, 1f));
        _black.Color = Colors.Black with { A = dark };
        _top.Visible = _bottom.Visible = !HandedOff;
        _hint.Visible = !HandedOff && _skipFrom is null;
        _hint.Modulate = Colors.White with { A = Mathf.Clamp(_t - settings.FadeIn, 0f, 1f) };
        _hint.Text = Loc.T("{0}  Skip", GameInput.Label(InputActions.Jump));
    }

    private void BuildOverlay()
    {
        var layer = new CanvasLayer { Name = "Overlay", Layer = 15 };
        AddChild(layer);
        var bar = _scene.Settings.Letterbox;
        _top = new ColorRect { Name = "Top", Color = Colors.Black, AnchorRight = 1, AnchorBottom = bar, MouseFilter = Control.MouseFilterEnum.Ignore };
        _bottom = new ColorRect { Name = "Bottom", Color = Colors.Black, AnchorTop = 1 - bar, AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        _hint = new Label
        {
            Name = "Hint", AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled, HorizontalAlignment = HorizontalAlignment.Right,
            AnchorLeft = 1, AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1, OffsetLeft = -320, OffsetTop = -48, OffsetRight = -24, OffsetBottom = -16,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _hint.AddThemeColorOverride("font_color", UiTokens.TextSecondary);
        _black = new ColorRect { Name = "Black", Color = Colors.Black, AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(_top);
        layer.AddChild(_bottom);
        layer.AddChild(_hint);
        layer.AddChild(_black);
    }

    private void BuildEffects()
    {
        _housingFire = new Node3D { Name = "HousingFire" };
        AddChild(_housingFire);
        _housingFlames = Particles("Flames", 90, 0.7, 1.0f, FireNode.Additive(), FlameColors, 1.1f, 2f, 4f);
        _housingFlames.ScaleAmountCurve = FireNode.Shrinking();
        _housingFire.AddChild(_housingFlames);
        _housingFire.AddChild(Particles("Smoke", 120, 4.5, 4f, FireNode.Blended(), SmokeColors, 1f, 1.5f, 3f));
        _stub = new Node3D { Name = "Stub" };
        AddChild(_stub);
        _stub.AddChild(Particles("Smoke", 260, 6.0, 4f, FireNode.Blended(), SmokeColors, 0.6f, 1f, 2f));
        var sparks = Particles("Flames", 30, 0.5, 0.9f, FireNode.Additive(), FlameColors, 0.5f, 1f, 3f);
        sparks.ScaleAmountCurve = FireNode.Shrinking();
        _stub.AddChild(sparks);
        _burst = Particles("Burst", 90, 1.3, 2.4f, FireNode.Additive(), FlameColors, 1.5f, 8f, 18f);
        _burst.OneShot = true;
        _burst.Explosiveness = 1f;
        _burst.Spread = 180f;
        _burst.Gravity = new Vector3(0, -4f, 0);
        _burst.ScaleAmountCurve = FireNode.Shrinking();
        AddChild(_burst);
        _flash = new OmniLight3D { Name = "Flash", LightColor = new Color(1f, 0.65f, 0.3f), OmniRange = 40f, LightEnergy = 0f, ShadowEnabled = false };
        AddChild(_flash);
        _dust = Particles("Dust", 160, 3.0, 5f, FireNode.Blended(), DustColors, 4f, 3f, 7f);
        _dust.Direction = new Vector3(0, 0.4f, 1f);
        _dust.Spread = 70f;
        _dust.Gravity = new Vector3(0, -0.6f, 0);
        AddChild(_dust);
        _impacts = [.. _containers.Select((_, i) =>
        {
            var impact = Particles($"Impact{i}", 40, 2.0, 3f, FireNode.Blended(), DustColors, 1.5f, 2f, 5f);
            impact.OneShot = true;
            impact.Explosiveness = 0.9f;
            impact.Spread = 70f;
            impact.Gravity = new Vector3(0, -1.5f, 0);
            AddChild(impact);
            return impact;
        })];
    }

    // World-space particles (they trail behind what carries them), off until switched on.
    private static CpuParticles3D Particles(string name, int amount, double lifetime, float size, StandardMaterial3D material, Gradient colors,
        float radius, float speedMin, float speedMax) => new()
        {
            Name = name,
            Amount = amount,
            Lifetime = lifetime,
            Emitting = false,
            LocalCoords = false,
            Mesh = new QuadMesh { Size = new Vector2(size, size), Material = material },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = radius,
            Direction = Vector3.Up,
            Spread = 25f,
            Gravity = new Vector3(0.3f, 1.2f, 0),
            InitialVelocityMin = speedMin,
            InitialVelocityMax = speedMax,
            ScaleAmountMin = 0.7f,
            ScaleAmountMax = 1.4f,
            ColorRamp = colors,
        };
}
