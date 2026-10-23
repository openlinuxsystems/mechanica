using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Net;
using Mechanica.Core.Player;
using Mechanica.Game.Building;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Player characters, host + clients over localhost ENet with the vanilla mods. Both shipped characters load and
/// pose on the shared rig. The host and a client each have to choose once, every peer draws each player as what
/// they chose, a second pick is refused, and a client that leaves and comes back keeps theirs. A walking player's
/// feet make footsteps on another peer, a held tool shows in their hand, a swing plays there, and a player who
/// goes into deep water swims everywhere. On a slope another peer draws both feet on the ground.
/// </summary>
public partial class CharacterTest : StagedTest
{
    private const string Female = "vanilla:scrapper_female";
    private const string Male = "vanilla:scrapper_male";

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _a = null!;
    private Instance _b = null!;
    private readonly List<CharacterEvent> _events = [];
    private CharacterModel? _watched;
    private bool _swung;
    private float _weldClosed;
    private bool _armTurned;
    private Basis? _armOpen;
    private string? _refused;
    private ulong _drawnFrame;
    private Func<Vector3, float>? _seaLevel;
    private readonly List<StaticBody3D> _ramps = [];
    private Vector3 _slopeFrom;
    private const float SlopeDegrees = 18f;

    protected override string Tag => "characters";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();

        Add("the host spawns and must choose", _ => _host.Main.LocalPlayer is not null && _host.Main.Characters.LocalNeedsChoice);
        Add("both characters load on the shared rig", _ =>
        {
            var book = _host.Main.Characters.Book;
            Check(book.All.Select(c => c.QualifiedId).SequenceEqual([Male, Female]), $"vanilla has both scrappers ({string.Join(", ", book.All.Select(c => c.QualifiedId))})");
            var assets = _host.Main.Characters.Assets;
            Check(CharacterRig.Clips.All(assets.Clips.ContainsKey), "every clip of the animation set loads");
            foreach (var character in book.All)
            {
                var model = assets.Instantiate(character);
                Check(model?.FindChildren("*", nameof(Skeleton3D), owned: false).OfType<Skeleton3D>().FirstOrDefault() is { } skeleton
                    && CharacterRig.Bones.All(b => skeleton.FindBone(b) >= 0), $"{character.Name} is skinned to every rig bone");
                model?.Free();
            }
            foreach (var prop in Enum.GetValues<CharacterProp>().Where(p => CharacterRig.PropNode(p) is not null))
            {
                var made = assets.Prop(prop);
                Check(made is not null, $"the {prop} prop loads");
                made?.Node.Free();
            }
            Check(_host.Main.LocalPlayer!.Character is CharacterModel { Character.QualifiedId: Male }, "a player who has not chosen shows as the default");
            return true;
        });
        Add("the host chooses the female scrapper", frame =>
        {
            if (frame == 0)
                _host.Main.Characters.Choose("scrapper_female");
            return _host.Main.Characters.Choices.ChosenBy(_host.Peer) == Female && !_host.Main.Characters.LocalNeedsChoice
                && Check(Shown(_host, _host) == Female, "the host's own body is the female scrapper");
        });

        Add("a client joins and must choose", frame =>
        {
            if (frame == 0)
                _a = _harness.StartClient("ClientA");
            return _a.Main.LocalPlayer is not null && _a.Main.Characters.LocalNeedsChoice && Shown(_a, _host) == Female;
        });
        Add("the client chooses the male scrapper", frame =>
        {
            if (frame == 0)
                _a.Main.Characters.Choose(Male);
            return Shown(_host, _a) == Male && Shown(_a, _a) == Male && !_a.Main.Characters.LocalNeedsChoice;
        });
        Add("only the local player has first-person arms, on its camera", frame =>
        {
            var player = _host.Main.LocalPlayer!;
            if (frame == 0)
            {
                player.ThirdPerson = false;
                _drawnFrame = Engine.GetProcessFrames() + 2;
            }
            if (Engine.GetProcessFrames() < _drawnFrame)
                return false;
            if (!Check(player.Arms is CharacterModel { FirstPerson: true }, "the host's player has first-person arms"))
                return true;
            var arms = (CharacterModel)player.Arms!;
            Check(arms.GetParent() == player.Camera, "they ride the camera");
            Check(_host.Main.PlayerSync.Players[_a.Peer].Arms is null && _a.Main.PlayerSync.Players[_host.Peer].Arms is null, "nobody has arms for another player");
            Check(arms.Visible || !player.Camera.Current, "they show while the player looks out of its own eyes");
            var body = (CharacterModel)player.Character!;
            Check(Triangles(arms) > 0 && Triangles(arms) < Triangles(body) / 4, $"they are only the forearms and hands ({Triangles(arms)} of {Triangles(body)} triangles)");
            player.ThirdPerson = true;
            return true;
        });
        Add("in third person the arms hide", frame =>
        {
            var player = _host.Main.LocalPlayer!;
            // The arms follow the view in _Process; stages step on physics frames, which can run several to one drawn frame.
            if (frame == 0)
                _drawnFrame = Engine.GetProcessFrames() + 2;
            if (Engine.GetProcessFrames() < _drawnFrame)
                return false;
            var hidden = !player.Arms!.Visible;
            player.ThirdPerson = false;
            return Check(hidden, "the first-person arms hide when the body shows");
        });
        Add("a second pick in the same world is refused", frame =>
        {
            if (frame == 0)
                _a.Main.Characters.Choose(Female, result => _refused = result.Ok ? "accepted" : result.Message);
            if (_refused is null)
                return false;
            Check(_refused != "accepted", $"the host refuses a second pick ({_refused})");
            return Check(Shown(_host, _a) == Male, "the client is still the male scrapper");
        });

        Add("a walking player's feet make footsteps on another peer", frame =>
        {
            if (frame == 0)
                Watch(_a, _host);
            _host.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            var steps = _events.Where(e => e is CharacterEvent.StepLeft or CharacterEvent.StepRight).ToList();
            if (frame < 90)
                return false;
            _host.Main.LocalPlayer!.CurrentInput = PlayerInput.None;
            Check(steps.Count >= 2, $"the client heard {steps.Count} footsteps");
            return Check(steps.Zip(steps.Skip(1)).All(p => p.First != p.Second), "feet alternate");
        });

        Add("a held Weld Tool shows in the hand and a weld plays on another peer", frame =>
        {
            var player = _host.Main.LocalPlayer!;
            player.HeldProp = CharacterProp.WeldTool;
            if (frame == 10)
                player.Gesture(CharacterGesture.Weld);
            var model = (CharacterModel)_a.Main.PlayerSync.Players[_host.Peer].Character!;
            _swung |= model.Animator.Gesture == CharacterRig.GestureClip(CharacterGesture.Weld);
            // The arms close round the spinning tip as the weld registers, on the peer watching.
            var closed = _a.Main.WeldEffects.ClosedOf(_host.Peer) ?? 0f;
            _weldClosed = Math.Max(_weldClosed, closed);
            if (model.PropNode?.FindChild(WeldEffects.ArmLeft, owned: false) is Node3D arm)
            {
                if (closed == 0f && _armOpen is null)
                    _armOpen = arm.Transform.Basis;
                else if (closed > 0.5f && _armOpen is { } open)
                    _armTurned |= !arm.Transform.Basis.IsEqualApprox(open);
            }
            if ((!_swung || _weldClosed < 0.5f || model.PropNode?.Name != "weld_tool") && frame < 120)
                return false;
            Check(_swung && model.PropNode?.GetParent() is BoneAttachment3D { BoneName: "hand_r" }, "the Weld Tool rides the right hand");
            Check(_weldClosed > 0.5f && _armTurned, $"its clamping arms close round the tip ({_weldClosed:F2})");
            Check((_a.Main.WeldEffects.TipTurnsOf(_host.Peer) ?? 0f) > 0f, "and its tip spins");
            Check(model.PropNode!.FindChild(WeldEffects.GlowNode, owned: false) is MeshInstance3D && model.PropNode.FindChild(WeldEffects.TipNode, owned: false) is Node3D,
                "it has its glowing ring and its tip");
            return true;
        });
        Add("the arms open again once the weld is done", frame => frame > 5 && _a.Main.WeldEffects.ClosedOf(_host.Peer) is 0f);
        Add("putting the tool away empties the hand", _ =>
        {
            _host.Main.LocalPlayer!.HeldProp = CharacterProp.None;
            return ((CharacterModel)_a.Main.PlayerSync.Players[_host.Peer].Character!).PropNode is null;
        });

        Add("a crouching player is crouched on another peer", frame =>
        {
            _host.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, 0, 0, 0, false, false, Crawl: true);
            var seen = _a.Main.PlayerSync.Players[_host.Peer];
            var model = (CharacterModel)seen.Character!;
            if (!seen.Crouched || model.Animator.Mode != CharacterMode.Crouch)
                return false;
            Check(Mathf.IsEqualApprox(seen.CapsuleHeight, seen.Settings.CrouchHeight), $"the client's copy has the crouching capsule ({seen.CapsuleHeight})");
            _host.Main.LocalPlayer!.CurrentInput = PlayerInput.None;
            return true;
        });
        Add("standing up shows on another peer", _ => !_a.Main.PlayerSync.Players[_host.Peer].Crouched);
        Add("a player in deep water swims everywhere", frame =>
        {
            var player = _host.Main.LocalPlayer!;
            if (frame == 0)
            {
                // The test seed has no sea near spawn, so raise the host's water over where the player stands.
                Watch(_a, _host);
                _seaLevel = player.WaterLevel;
                var level = player.GlobalPosition.Y + 1.6f;
                player.WaterLevel = _ => level;
            }
            var model = (CharacterModel)_a.Main.PlayerSync.Players[_host.Peer].Character!;
            if (!player.Body.HasFlag(BodyFlags.Swimming) || model.Animator.Mode != CharacterMode.Swim || !_events.Contains(CharacterEvent.WaterEnter))
                return false;
            return Check(MathF.Abs(player.Velocity.Y) < 3f, $"the swimmer floats ({player.Velocity.Y:F2} m/s)");
        });
        Add("leaving the water walks again", frame =>
        {
            var player = _host.Main.LocalPlayer!;
            if (frame == 0)
                player.WaterLevel = _seaLevel;
            var model = (CharacterModel)_a.Main.PlayerSync.Players[_host.Peer].Character!;
            return !player.Body.HasFlag(BodyFlags.Swimming) && model.Animator.Mode != CharacterMode.Swim && _events.Contains(CharacterEvent.WaterExit);
        });

        Add("on a slope the feet stand on the ground on another peer", frame =>
        {
            var player = _host.Main.LocalPlayer!;
            if (frame == 0)
            {
                // A ramp high above the ground, tilted across the way the player faces, so one foot stands higher.
                _slopeFrom = player.GlobalPosition;
                var basis = new Basis(Vector3.Up, player.Rotation.Y) * new Basis(Vector3.Back, Mathf.DegToRad(SlopeDegrees));
                var at = player.GlobalPosition + Vector3.Up * 40f;
                foreach (var peer in new[] { _host, _a })
                {
                    var ramp = new StaticBody3D { Name = "Ramp", CollisionLayer = World.PhysicsLayers.Solid };
                    ramp.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6f, 0.4f, 6f) } });
                    peer.Main.AddChild(ramp);
                    ramp.GlobalTransform = new Transform3D(basis, at);
                    _ramps.Add(ramp);
                }
                player.GlobalPosition = at + Vector3.Up * 1.2f;
                player.Velocity = Vector3.Zero;
            }
            player.CurrentInput = new PlayerInput(0, 0, 0, 0, false, false);
            if (frame < 120)
                return false;
            var model = (CharacterModel)_a.Main.PlayerSync.Players[_host.Peer].Character!;
            var skeleton = model.Skeleton;
            var space = model.GetWorld3D().DirectSpaceState;
            var gaps = new List<float>();
            var heights = new List<float>();
            foreach (var name in new[] { "foot_l", "foot_r" })
            {
                var bone = skeleton.FindBone(name);
                var ankle = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone).Origin;
                var rest = (skeleton.GlobalTransform.Basis * new Vector3(0, skeleton.GetBoneGlobalRest(bone).Origin.Y, 0)).Length();
                var query = PhysicsRayQueryParameters3D.Create(ankle + Vector3.Up, ankle + Vector3.Down * 2f, World.PhysicsLayers.Solid);
                query.Exclude = [_a.Main.PlayerSync.Players[_host.Peer].GetRid()];
                var hit = space.IntersectRay(query);
                if (hit.Count == 0)
                    return Check(false, $"there is ramp under {name}");
                var ground = hit["position"].AsVector3().Y;
                gaps.Add(ankle.Y - rest - ground);
                heights.Add(ground);
            }
            Check(MathF.Abs(heights[0] - heights[1]) > 0.03f, $"the feet stand at different heights on the ramp ({heights[0] - heights[1]:F3} m)");
            Check(gaps.All(g => MathF.Abs(g) < 0.035f), $"both feet are on the ground ({string.Join(", ", gaps.Select(g => $"{g:F3}"))} m)");
            foreach (var ramp in _ramps)
                ramp.QueueFree();
            _ramps.Clear();
            player.GlobalPosition = _slopeFrom;
            player.Velocity = Vector3.Zero;
            return true;
        });

        Add("a client that leaves and comes back keeps its character", frame =>
        {
            if (frame == 0)
            {
                _harness.Stop(_a);
                _a = null!;
            }
            if (frame == 30)
                _a = _harness.StartClient("ClientA");
            if (_a?.Main.LocalPlayer is null || _a.Main.PlayerSync.Players.Count < 2)
                return false;
            return Shown(_a, _a) == Male && Shown(_host, _a) == Male && Shown(_a, _host) == Female
                && Check(!_a.Main.Characters.LocalNeedsChoice, "the returning client is not asked again");
        });
        Add("a late client sees everyone as they chose and chooses itself", frame =>
        {
            if (frame == 0)
                _b = _harness.StartClient("ClientB");
            if (_b.Main.LocalPlayer is null || _b.Main.PlayerSync.Players.Count < 3 || !_b.Main.Characters.LocalNeedsChoice)
                return false;
            return Shown(_b, _host) == Female && Shown(_b, _a) == Male;
        });
    }

    private static int Triangles(Node node) =>
        node.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>().Where(m => m.Visible && m.Mesh is not null)
            .Sum(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Sum(i => m.Mesh.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Index].AsInt32Array().Length / 3));

    // What player `who` is drawn as on `on`.
    private static string? Shown(Instance on, Instance who) =>
        on.Main.PlayerSync.Players.TryGetValue(who.Peer, out var player) && player.Character is CharacterModel model ? model.Character.QualifiedId : null;

    private void Watch(Instance on, Instance who)
    {
        if (_watched is not null)
            _watched.Event -= _events.Add;
        _events.Clear();
        _watched = (CharacterModel)on.Main.PlayerSync.Players[who.Peer].Character!;
        _watched.Event += _events.Add;
    }
}
