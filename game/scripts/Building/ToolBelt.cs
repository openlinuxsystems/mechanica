using Godot;
using Mechanica.Core.Input;
using Mechanica.Game.Items;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Survival;

namespace Mechanica.Game.Building;

/// <summary>
/// The local player's hand tools and which one is in hand. Keys 1 to 7 pick the Hammer, Lift, Weld
/// Tool, Paint Tool, Sledgehammer, the item in hand (<see cref="ItemTool"/>) or the Connect Tool; the HUD at the bottom left shows the tool in hand and its keys.
/// </summary>
public partial class ToolBelt : Node
{
    private readonly List<HandTool> _tools = [];
    private Label? _hud;

    /// <summary>The player whose tools these are. Set before adding to the tree.</summary>
    public PlayerController Player { get; init; } = null!;

    /// <summary>Where requests go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Settings, content and the world. Set before adding to the tree.</summary>
    public BuildSync Build { get; init; } = null!;

    /// <summary>Where the tools' own feedback sounds play; null plays none.</summary>
    public Sound.SoundSync? Sounds { get; init; }

    /// <summary>Inventories, for the item in hand. Set before adding to the tree.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Survival state, for the item in hand. Set before adding to the tree.</summary>
    public SurvivalSync Survival { get; init; } = null!;

    /// <summary>False in automated tests: keys and mouse do nothing and no HUD is shown.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>The Hammer.</summary>
    public HammerTool Hammer { get; private set; } = null!;

    /// <summary>The Lift.</summary>
    public LiftTool Lift { get; private set; } = null!;

    /// <summary>The Weld Tool.</summary>
    public WeldTool Weld { get; private set; } = null!;

    /// <summary>The Paint Tool.</summary>
    public PaintTool Paint { get; private set; } = null!;

    /// <summary>The Sledgehammer.</summary>
    public SledgeTool Sledge { get; private set; } = null!;

    /// <summary>The item in hand: food, weapons, seeds and water.</summary>
    public ItemTool Item { get; private set; } = null!;

    /// <summary>The Connect Tool (chunk 10).</summary>
    public ConnectTool Connector { get; private set; } = null!;

    /// <summary>Every tool, in key order.</summary>
    public IReadOnlyList<HandTool> Tools => _tools;

    /// <summary>The tool in hand.</summary>
    public HandTool Current => _tools.First(t => t.Active);

    public override void _Ready()
    {
        Hammer = Add(new HammerTool { Name = "Hammer", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Lift = Add(new LiftTool { Name = "Lift", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Weld = Add(new WeldTool { Name = "Weld", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Paint = Add(new PaintTool { Name = "Paint", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Sledge = Add(new SledgeTool { Name = "Sledge", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Item = Add(new ItemTool { Name = "Item", Player = Player, Actions = Actions, Build = Build, Inventory = Inventory, Survival = Survival, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Connector = Add(new ConnectTool { Name = "Connect", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Hammer.Active = true;
        if (!ReadDeviceInput)
            return;
        var layer = new CanvasLayer { Name = "Hud" };
        // Bottom left, clear of the connect panel at the top.
        _hud = new Label { Name = "ToolLabel", GrowVertical = Control.GrowDirection.Begin };
        layer.AddChild(_hud);
        _hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.KeepSize, 16);
        var crosshair = new Label { Name = "Crosshair", Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        layer.AddChild(crosshair);
        crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        AddChild(layer);
    }

    public override void _Process(double delta)
    {
        if (_hud is not null)
            _hud.Text = $"[{GameInput.Label(InputActions.Hotbar(1))}-{GameInput.Label(InputActions.Hotbar(_tools.Count))}] {string.Join("  ", _tools.Select(t => t.Active ? $"[{t.ToolName}]" : t.ToolName))}\n{Current.HudText()}";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ReadDeviceInput || Input.MouseMode != Input.MouseModeEnum.Captured)
            return;
        for (var slot = 1; slot <= _tools.Count && slot <= InputActions.HotbarSlots; slot++)
        {
            if (GameInput.Pressed(@event, InputActions.Hotbar(slot)))
            {
                Use(_tools[slot - 1]);
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }

    /// <summary>Puts <paramref name="tool"/> in hand.</summary>
    public void Use(HandTool tool)
    {
        foreach (var t in _tools)
            t.Active = t == tool;
    }

    private T Add<T>(T tool) where T : HandTool
    {
        AddChild(tool);
        _tools.Add(tool);
        return tool;
    }
}
