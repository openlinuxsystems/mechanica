using System.Text.Json;
using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Logic;

namespace Mechanica.Game.Tools;

// Given `logic_parts`, the capture puts the audit batch D2 parts on a slab of metal blocks in front of the player: a
// Switch linked to a Horn, a Timer (blinking) and a Logic Gate set to or, a second gate left on and with no inputs, and
// a Radio on station 3. It shoots the row at rest, then switches on and plays the radio, and shoots the row and each
// part close up from its front (tools/capture-logic-parts.sh).
public partial class CaptureScene
{
    private const int LogicPartsCreation = 9030;

    private void LogicPartsShots()
    {
        Engine.TimeScale = 1.0;
        var at = Vector3.Zero;
        var ids = new Dictionary<string, int>();
        Node3D? Part(string name) => _main.Creations.NodeOf(LogicPartsCreation)?.PartNode(ids[name]);
        void Activate(string name) =>
            _main.Actions.Request(LogicActions.ActivatePart, new LogicPartArgs(LogicPartsCreation, ids[name]), r => GD.Print($"[capture] use {name}: {r}"));
        void View(Vector3 from, Vector3 to)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = at + from;
            _camera.LookAt(at + to);
        }
        void Near(string name, Vector3 offset, float distance, float height = 0f)
        {
            if (Part(name) is not { } node)
                return;
            var t = node.GlobalTransform;
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = t.Origin + t.Basis * offset.Normalized() * distance;
            _camera.LookAt(t.Origin + t.Basis * new Vector3(0, height, 0));
        }
        void Report()
        {
            var r = _main.Scripts.Runtime.Readouts;
            foreach (var (name, id) in ids)
                GD.Print($"[capture] {name}: pose={r.Text(LogicPartsCreation, id, "pose")} face={r.Text(LogicPartsCreation, id, "face")} "
                    + $"lit={r.Number(LogicPartsCreation, id, "lit")} tune={r.Number(LogicPartsCreation, id, "tune")}");
        }
        static Dictionary<string, JsonElement> Set(params (string Key, object Value)[] values) =>
            values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value));

        Shot("logic_parts_00_wait", 60, () => { });
        Shot("logic_parts_01_placed", 60, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -2.5f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            at = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Creation(LogicPartsCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new GridCell(x, y, z), GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -4; x <= 4; x++)
                for (var z = -1; z <= 1; z++)
                    Put("vanilla:metal_block", x, 0, z);
            ids["switch"] = Put("vanilla:switch", -4, 1, 1);
            ids["horn"] = Put("vanilla:horn", -3, 1, 0);
            ids["timer"] = Put("vanilla:timer", -2, 1, 0);
            ids["gate"] = Put("vanilla:logic_gate", -1, 1, 0);
            ids["gate_idle"] = Put("vanilla:logic_gate", 0, 1, 0);
            ids["radio"] = Put("vanilla:radio", 2, 1, 0);
            creation.SetPartSettings(ids["timer"], Set(("mode", "repeat"), ("delay", 0.6)));
            creation.SetPartSettings(ids["gate"], Set(("mode", "or")));
            creation.SetPartSettings(ids["gate_idle"], Set(("mode", "xnor")));
            creation.SetPartSettings(ids["radio"], Set(("station", 3)));
            foreach (var to in new[] { "horn", "timer", "gate" })
                creation.TryLink(ids["switch"], ids[to], out _);
            creation.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(at.X, at.Y + 0.2f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
        });
        Shot("logic_parts_02_row_off", 30, () => { Report(); View(new Vector3(0.2f, 1.1f, 2.3f), new Vector3(-0.1f, 0.35f, 0)); });
        Shot("logic_parts_03_switched_on", 40, () => { Activate("switch"); Activate("radio"); });
        Shot("logic_parts_04_row_on", 30, () => { Report(); View(new Vector3(0.2f, 1.1f, 2.3f), new Vector3(-0.1f, 0.35f, 0)); });
        Shot("logic_parts_05_row_low", 20, () => View(new Vector3(-1.4f, 0.7f, 1.6f), new Vector3(-0.2f, 0.35f, 0)));
        Shot("logic_parts_06_horn", 20, () => Near("horn", new Vector3(0.7f, 0.6f, 1f), 0.6f));
        Shot("logic_parts_07_timer", 20, () => Near("timer", new Vector3(0.5f, 0.9f, 1f), 0.6f));
        Shot("logic_parts_08_timer_later", 20, () => Near("timer", new Vector3(0.5f, 0.9f, 1f), 0.6f));
        Shot("logic_parts_09_gate_top", 20, () => Near("gate", new Vector3(0f, 1f, 0.35f), 0.55f));
        Shot("logic_parts_10_gate_idle_top", 20, () => Near("gate_idle", new Vector3(0f, 1f, 0.35f), 0.55f));
        Shot("logic_parts_11_radio", 20, () => Near("radio", new Vector3(0.6f, 0.5f, 1f), 1.2f, 0.1f));
        Shot("logic_parts_12_switched_off", 40, () => { Activate("switch"); Activate("radio"); });
        Shot("logic_parts_13_row_off_again", 20, () => { Report(); View(new Vector3(0.2f, 1.1f, 2.3f), new Vector3(-0.1f, 0.35f, 0)); });
    }
}
