namespace Mechanica.Core.Input;

/// <summary>When an action can fire. Two actions on one key only clash if their contexts overlap.</summary>
public enum InputContext
{
    /// <summary>Any time: tools, menus, the camera.</summary>
    General,

    /// <summary>Only on foot (Crawl).</summary>
    OnFoot,

    /// <summary>Only while seated (the seated camera).</summary>
    Seated,
}

/// <summary>One bindable action with its default keys (UI spec §1).</summary>
/// <param name="Id">Stable id, also the Godot input action name.</param>
/// <param name="Name">What the Controls list shows.</param>
/// <param name="Category">The Controls list section.</param>
/// <param name="Primary">Default primary binding.</param>
/// <param name="Alternate">Default alternate binding, or null.</param>
/// <param name="Context">When it can fire.</param>
public sealed record InputActionDef(string Id, string Name, string Category, string Primary, string? Alternate = null,
    InputContext Context = InputContext.General);

/// <summary>
/// Every bindable action and its default keys. The defaults are the UI spec's §1 table; actions the
/// spec doesn't list (tip, drop, the debug overlays) keep the keys they had. All of them can be rebound
/// in Options, and <see cref="Keymap"/> holds the player's changes.
/// </summary>
public static class InputActions
{
    public const string MoveForward = "move_forward";
    public const string MoveBack = "move_back";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Jump = "jump";
    public const string Crawl = "crawl";
    public const string Sprint = "sprint";
    public const string Primary = "use_primary";
    public const string Secondary = "use_secondary";
    public const string Interact = "interact";
    public const string Upgrade = "upgrade";
    public const string Inventory = "inventory";
    public const string Logbook = "logbook";
    public const string Map = "map";
    public const string Rotate = "rotate";
    public const string RotateBack = "rotate_back";
    public const string Tip = "tip";
    public const string LiftUp = "lift_up";
    public const string LiftDown = "lift_down";
    public const string ForceBuild = "force_build";
    public const string Reload = "reload";
    public const string Drop = "drop_item";
    public const string HotbarNext = "hotbar_next";
    public const string HotbarPrevious = "hotbar_previous";
    public const string ZoomIn = "zoom_in";
    public const string ZoomOut = "zoom_out";
    public const string SeatCamera = "seat_camera";
    public const string Spectate = "spectate";
    public const string Chat = "chat";
    public const string Handbook = "handbook";
    public const string PagePrevious = "page_previous";
    public const string PageNext = "page_next";
    public const string Pause = "pause";
    public const string HideGui = "hide_gui";
    public const string LogicVisualizer = "toggle_logic_visualizer";
    public const string CreationInspector = "toggle_creation_inspector";
    public const string ForceOverlay = "toggle_force_overlay";
    public const string SandboxSwitch = "sandbox_switch";

    /// <summary>Hotbar slots 1 to 10 (keys 1 to 9, then 0).</summary>
    public const int HotbarSlots = 10;

    /// <summary>The action for hotbar slot <paramref name="slot"/> (1 to 10).</summary>
    public static string Hotbar(int slot) => $"hotbar_{slot}";

    /// <summary>Every action, in the order the Controls list shows them.</summary>
    public static IReadOnlyList<InputActionDef> All { get; } = Build();

    /// <summary>The action called <paramref name="id"/>, or null.</summary>
    public static InputActionDef? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    private static List<InputActionDef> Build()
    {
        const string move = "Movement", tools = "Tools and building", menus = "Menus", camera = "Camera", hotbar = "Hotbar", debug = "Mod tools";
        var all = new List<InputActionDef>
        {
            new(MoveForward, "Move forward", move, "W"),
            new(MoveBack, "Move backward", move, "S"),
            new(MoveLeft, "Strafe left", move, "A"),
            new(MoveRight, "Strafe right", move, "D"),
            new(Jump, "Jump", move, "Space"),
            new(Crawl, "Crawl", move, "Ctrl", Context: InputContext.OnFoot),
            new(Sprint, "Sprint", move, "Shift"),
            new(Primary, "Create / attack / place", tools, "MouseLeft"),
            new(Secondary, "Remove / pick up", tools, "MouseRight"),
            new(Interact, "Use / interact", tools, "E"),
            new(Upgrade, "Upgrade", tools, "U"),
            new(Rotate, "Next rotation / colour", tools, "Q", "Shift+WheelDown"),
            new(RotateBack, "Previous rotation / colour", tools, "Shift+Q", "Shift+WheelUp"),
            new(Tip, "Tip over", tools, "T"),
            new(LiftUp, "Lift up", tools, "Up"),
            new(LiftDown, "Lift down", tools, "Down"),
            new(ForceBuild, "Force build", tools, "F"),
            new(Reload, "Reload", tools, "R"),
            new(Drop, "Drop one", tools, "G"),
            new(HotbarNext, "Next hotbar slot", hotbar, "WheelDown"),
            new(HotbarPrevious, "Previous hotbar slot", hotbar, "WheelUp"),
        };
        for (var slot = 1; slot <= HotbarSlots; slot++)
            all.Add(new(Hotbar(slot), $"Hotbar {slot}", hotbar, $"Key{slot % 10}"));
        all.AddRange(
        [
            new(ZoomIn, "Zoom in", camera, "X", "Alt+WheelUp"),
            new(ZoomOut, "Zoom out", camera, "C", "Alt+WheelDown"),
            new(SeatCamera, "Seated camera", camera, "Ctrl", Context: InputContext.Seated),
            new(Spectate, "Watch players", camera, "V"),
            new(Inventory, "Inventory", menus, "I", "Tab"),
            new(Logbook, "Logbook", menus, "L"),
            new(Map, "Map", menus, "M"),
            new(Handbook, "Handbook", menus, "H", "F1"),
            new(PagePrevious, "Handbook: previous page", menus, "Left", "Pageup"),
            new(PageNext, "Handbook: next page", menus, "Right", "Pagedown"),
            new(Chat, "Chat", menus, "Enter"),
            new(Pause, "Pause / close", menus, "Escape"),
            new(HideGui, "Hide GUI", menus, "Alt+Z"),
            new(LogicVisualizer, "Logic Visualizer", debug, "F7"),
            new(CreationInspector, "Creation Inspector", debug, "F8"),
            new(ForceOverlay, "Force overlay", debug, "F9"),
            new(SandboxSwitch, "Test Sandbox: switch player", debug, "F6"),
        ]);
        return all;
    }
}
