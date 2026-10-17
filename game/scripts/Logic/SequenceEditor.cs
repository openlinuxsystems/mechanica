using System.Text.Json;
using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Logic;

/// <summary>
/// A controller's sequence editor (UI spec §12): a timeline of up to <see cref="SettingDefinition.MaxCount"/> frames,
/// each listing the bearings and pistons the part is linked to with a target (an angle in degrees or an extension in
/// m) and a speed. Frames are added after the picked one (copying it) and removed; Play previews the frames on the
/// Creation through the part's script before they are applied (<see cref="SettingDefinition.Preview"/>).
/// </summary>
public partial class SequenceEditor : VBoxContainer
{
    /// <summary>Speed a newly added joint starts at, capped at the setting's max.</summary>
    public const float NewSpeed = 1f;

    private readonly List<List<SequenceStep>> _frames = [];
    private HBoxContainer _timeline = null!;
    private VBoxContainer _joints = null!;
    private Button _add = null!;
    private Button _remove = null!;
    private Button _play = null!;
    private int _picked;
    private bool _building;

    /// <summary>One joint the part is linked to.</summary>
    /// <param name="Part">The bearing or piston.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="Piston">True for a piston (targets in m), false for a bearing (degrees).</param>
    /// <param name="Min">Lowest target.</param>
    /// <param name="Max">Highest target.</param>
    public readonly record struct Joint(int Part, string Name, bool Piston, float Min, float Max);

    /// <summary>The setting edited. Set before adding to the tree.</summary>
    public SettingDefinition Setting { get; init; } = null!;

    /// <summary>The joints the part is linked to, in part order. Set before adding to the tree.</summary>
    public IReadOnlyList<Joint> Joints { get; init; } = [];

    /// <summary>The frames it starts with. Set before adding to the tree.</summary>
    public IReadOnlyList<IReadOnlyList<SequenceStep>> Initial { get; init; } = [];

    /// <summary>Called with the whole sequence after every change.</summary>
    public Action<JsonElement>? Changed { get; init; }

    /// <summary>Called when Play (true, with the frames) or Pause (false) is pressed; null hides the preview button.</summary>
    public Action<bool, JsonElement>? Preview { get; init; }

    /// <summary>The frames as edited now.</summary>
    public IReadOnlyList<IReadOnlyList<SequenceStep>> Frames => _frames;

    /// <summary>The frame picked on the timeline (0 based).</summary>
    public int Picked => _picked;

    /// <summary>True while the preview plays.</summary>
    public bool Playing { get; private set; }

    /// <summary>The sequence as <c>ConfigurePart</c> sends it.</summary>
    public JsonElement Value => PartSettings.Sequence(_frames);

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        foreach (var frame in Initial)
            _frames.Add(Complete(frame));
        var header = new HBoxContainer();
        header.AddChild(Loc.Verbatim(new Label { Text = Setting.Name ?? Setting.Id ?? "", SizeFlagsHorizontal = SizeFlags.ExpandFill }));
        _add = new Button { Name = "AddFrame", Text = "Add frame", TooltipText = "Add a frame after the picked one, copying it" };
        _add.Pressed += AddFrame;
        header.AddChild(_add);
        _remove = new Button { Name = "RemoveFrame", Text = "Remove frame", TooltipText = "Remove the picked frame" };
        _remove.Pressed += RemoveFrame;
        header.AddChild(_remove);
        if (Preview is not null)
        {
            _play = new Button { Name = "Play", Text = "Play", TooltipText = "Preview these frames on the creation without applying them" };
            _play.Pressed += () => SetPlaying(!Playing);
            header.AddChild(_play);
        }
        AddChild(header);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 44) };
        _timeline = new HBoxContainer { Name = "Timeline" };
        scroll.AddChild(_timeline);
        AddChild(scroll);
        _joints = new VBoxContainer { Name = "Joints" };
        AddChild(_joints);
        Rebuild();
    }

    /// <summary>Picks frame <paramref name="frame"/> on the timeline.</summary>
    public void Pick(int frame)
    {
        _picked = Math.Clamp(frame, 0, Math.Max(0, _frames.Count - 1));
        Rebuild();
    }

    /// <summary>Adds a frame after the picked one, copying it (or every joint at 0 when there is none).</summary>
    public void AddFrame()
    {
        if (_frames.Count >= (Setting.MaxCount ?? 0) || Joints.Count == 0)
            return;
        var copy = _frames.Count == 0 ? Complete([]) : [.. _frames[_picked]];
        var at = _frames.Count == 0 ? 0 : _picked + 1;
        _frames.Insert(at, copy);
        _picked = at;
        Edited();
    }

    /// <summary>Removes the picked frame.</summary>
    public void RemoveFrame()
    {
        if (_frames.Count == 0)
            return;
        _frames.RemoveAt(_picked);
        _picked = Math.Clamp(_picked, 0, Math.Max(0, _frames.Count - 1));
        Edited();
    }

    /// <summary>Sets one joint's target and speed in the picked frame.</summary>
    public void SetStep(int part, float target, float speed)
    {
        if (_frames.Count == 0)
            return;
        var frame = _frames[_picked];
        var index = frame.FindIndex(s => s.Part == part);
        if (index < 0)
            return;
        frame[index] = new SequenceStep(part, target, speed);
        Changed?.Invoke(Value);
        if (Playing)
            Preview?.Invoke(true, Value);
    }

    /// <summary>Starts or stops the preview.</summary>
    public void SetPlaying(bool playing)
    {
        if (Preview is null || (playing && _frames.Count == 0))
            playing = false;
        Playing = playing;
        if (_play is not null)
            _play.Text = playing ? "Pause" : "Play";
        Preview?.Invoke(playing, Value);
    }

    // Every linked joint in the frame, in order; joints no longer linked are dropped, new ones start at 0.
    private List<SequenceStep> Complete(IReadOnlyList<SequenceStep> frame)
    {
        var speed = Math.Min(NewSpeed, Setting.MaxSpeed ?? NewSpeed);
        return [.. Joints.Select(j => frame.Where(s => s.Part == j.Part).Select(s => (SequenceStep?)s).FirstOrDefault() is { } step
            ? step with { Target = Math.Clamp(step.Target, j.Min, j.Max) }
            : new SequenceStep(j.Part, Math.Clamp(0f, j.Min, j.Max), speed))];
    }

    private void Edited()
    {
        Rebuild();
        Changed?.Invoke(Value);
        if (Playing)
            Preview?.Invoke(_frames.Count > 0, Value);
    }

    private void Rebuild()
    {
        _building = true;
        foreach (var child in _timeline.GetChildren().Concat(_joints.GetChildren()))
        {
            child.GetParent().RemoveChild(child);
            child.QueueFree();
        }
        var group = new ButtonGroup();
        for (var i = 0; i < _frames.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Name = $"Frame{i + 1}", Text = $"{i + 1}", ToggleMode = true, ButtonGroup = group, ButtonPressed = i == _picked,
                CustomMinimumSize = new Vector2(36, 36), TooltipText = Loc.T("Frame {0}", i + 1),
            };
            button.Pressed += () => Pick(index);
            _timeline.AddChild(Loc.Verbatim(button));
        }
        _add.Disabled = _frames.Count >= (Setting.MaxCount ?? 0) || Joints.Count == 0;
        _remove.Disabled = _frames.Count == 0;
        if (Joints.Count == 0)
            _joints.AddChild(new Label { Text = "Link bearings or pistons to this part with the Connect Tool to give it frames.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        else if (_frames.Count == 0)
            _joints.AddChild(new Label { Text = "No frames yet. Add a frame to set where each joint goes.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        else
        {
            foreach (var step in _frames[_picked])
                _joints.AddChild(StepRow(Joints.First(j => j.Part == step.Part), step));
        }
        _building = false;
    }

    private HBoxContainer StepRow(Joint joint, SequenceStep step)
    {
        var row = new HBoxContainer { Name = $"Joint{joint.Part}" };
        row.AddChild(Loc.Verbatim(new Label { Text = Loc.T("{0} (part {1})", joint.Name, joint.Part), CustomMinimumSize = new Vector2(150, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }));
        var target = new SpinBox
        {
            Name = "Target", MinValue = joint.Min, MaxValue = joint.Max, Step = joint.Piston ? 0.05 : 1, Value = step.Target,
            Suffix = joint.Piston ? "m" : "°", TooltipText = joint.Piston ? "Extension" : "Angle", CustomMinimumSize = new Vector2(110, 0),
        };
        var speed = new SpinBox
        {
            Name = "Speed", MinValue = 0.05, MaxValue = Setting.MaxSpeed ?? 1, Step = 0.05, Value = step.Speed,
            Suffix = joint.Piston ? "m/s" : "rad/s", TooltipText = "Speed", CustomMinimumSize = new Vector2(120, 0),
        };
        target.ValueChanged += v =>
        {
            if (!_building)
                SetStep(joint.Part, (float)v, (float)speed.Value);
        };
        speed.ValueChanged += v =>
        {
            if (!_building)
                SetStep(joint.Part, (float)target.Value, (float)v);
        };
        row.AddChild(target);
        row.AddChild(speed);
        return row;
    }
}
