using Godot;
using Mechanica.Core.Characters;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Player;

/// <summary>
/// A row of cards, one per character, each with a live 3D preview standing idle, its name and description.
/// Clicking a card selects it. New game and the first-join window both use it.
/// </summary>
public partial class CharacterPicker : HBoxContainer
{
    private readonly ButtonGroup _group = new();
    private readonly List<(Button Card, Character Character)> _cards = [];

    /// <summary>The characters to show. Set before adding to the tree.</summary>
    public CharacterBook Book { get; init; } = CharacterBook.Empty;

    /// <summary>Models for the previews; null shows names only. Set before adding to the tree.</summary>
    public CharacterAssets? Assets { get; init; }

    /// <summary>Size of each preview.</summary>
    public Vector2I PreviewSize { get; init; } = new(200, 260);

    /// <summary>The selected character's qualified id, or null when there are none.</summary>
    public string? Selected { get; private set; }

    /// <summary>Raised when the selection changes.</summary>
    public event Action<string>? SelectionChanged;

    /// <summary>The cards, in order.</summary>
    public IReadOnlyList<Button> Cards => [.. _cards.Select(c => c.Card)];

    public override void _Ready()
    {
        Alignment = AlignmentMode.Center;
        AddThemeConstantOverride("separation", 16);
        foreach (var character in Book.All)
        {
            // Character names and descriptions are mod text: shown as the mod wrote them.
            var card = Loc.Verbatim(new Button
            {
                Name = character.Definition.Id ?? character.QualifiedId,
                ToggleMode = true,
                ButtonGroup = _group,
                TooltipText = character.Description,
                CustomMinimumSize = new Vector2(PreviewSize.X + 16, PreviewSize.Y + 64),
            });
            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            card.AddChild(column);
            column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 8);
            if (Assets is not null)
                column.AddChild(new CharacterPreview { Character = character, Assets = Assets, Size = PreviewSize, MouseFilter = MouseFilterEnum.Ignore });
            column.AddChild(Loc.Verbatim(new Label { Text = character.Name, HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "TitleLabel", MouseFilter = MouseFilterEnum.Ignore }));
            if (character.Description.Length > 0)
            {
                column.AddChild(Loc.Verbatim(new Label
                {
                    Text = character.Description, HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "SecondaryLabel",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(PreviewSize.X, 0), MouseFilter = MouseFilterEnum.Ignore,
                }));
            }
            card.Toggled += on =>
            {
                if (on)
                    SetSelected(character.QualifiedId);
            };
            AddChild(card);
            _cards.Add((card, character));
        }
        if (Book.Default is { } first)
            Select(first.QualifiedId);
    }

    /// <summary>Selects the character with <paramref name="id"/> (a bare id means vanilla). False when there is none.</summary>
    public bool Select(string id)
    {
        if (Book.Find(id) is not { } character)
            return false;
        foreach (var (card, c) in _cards)
            card.SetPressedNoSignal(c == character);
        SetSelected(character.QualifiedId);
        return true;
    }

    private void SetSelected(string id)
    {
        if (Selected == id)
            return;
        Selected = id;
        SelectionChanged?.Invoke(id);
    }
}

/// <summary>A small 3D view of one character standing idle, turning slowly.</summary>
public partial class CharacterPreview : SubViewportContainer
{
    private Node3D? _turntable;

    /// <summary>The character. Set before adding to the tree.</summary>
    public Character Character { get; init; } = null!;

    /// <summary>Its model. Set before adding to the tree.</summary>
    public CharacterAssets Assets { get; init; } = null!;

    /// <summary>The preview's size in pixels.</summary>
    public new Vector2I Size { get; init; } = new(200, 260);

    /// <summary>The model shown, or null when it failed to load.</summary>
    public CharacterModel? Model { get; private set; }

    public override void _Ready()
    {
        CustomMinimumSize = Size;
        Stretch = true;
        var viewport = new SubViewport { Size = Size, OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X, GuiDisableInput = true };
        AddChild(viewport);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.75f, 0.8f, 0.9f),
            AmbientLightEnergy = 0.6f,
        };
        viewport.AddChild(new WorldEnvironment { Environment = environment });
        viewport.AddChild(new DirectionalLight3D { Rotation = new Vector3(Mathf.DegToRad(-40), Mathf.DegToRad(30), 0), LightEnergy = 1.4f });
        viewport.AddChild(new Camera3D { Position = new Vector3(0, 1.0f, 3.0f), Rotation = new Vector3(Mathf.DegToRad(-4), 0, 0), Fov = 40, Current = true });
        _turntable = new Node3D { Name = "Turntable", Rotation = new Vector3(0, Mathf.Pi + 0.4f, 0) };
        viewport.AddChild(_turntable);
        if (Assets.Instantiate(Character) is { } model)
        {
            Model = new CharacterModel { Character = Character, Assets = Assets, Model = model };
            _turntable.AddChild(Model);
        }
        else
        {
            AddChild(new Label { Text = "(no model)", ThemeTypeVariation = "SecondaryLabel" });
        }
    }

    public override void _Process(double delta)
    {
        if (_turntable is not null)
            _turntable.RotateY((float)delta * 0.4f);
    }
}

/// <summary>
/// Asks the local player which character to play as, the first time they are in a world. It cannot be closed
/// without choosing: every player needs a character, and the choice is kept for this world.
/// </summary>
public partial class CharacterChoiceWindow : Control
{
    /// <summary>The characters to choose from. Set before adding to the tree.</summary>
    public CharacterBook Book { get; init; } = CharacterBook.Empty;

    /// <summary>Models for the previews. Set before adding to the tree.</summary>
    public CharacterAssets? Assets { get; init; }

    /// <summary>The cards.</summary>
    public CharacterPicker Picker { get; private set; } = null!;

    /// <summary>The confirm button.</summary>
    public Button ConfirmButton { get; private set; } = null!;

    /// <summary>Raised with the chosen character's qualified id.</summary>
    public event Action<string>? Chosen;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        UiInput.KeepMouse(this);
        // Under the in-game menu (layer 10), so Esc still opens it to quit without choosing.
        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);
        var dim = UiInput.KeepMouse(new ColorRect { Color = new Color(UiTokens.DeepNavy, 0.6f) });
        layer.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        layer.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // The dim is beside the panel, not behind it in the tree, so the panel keeps its own wheel turns.
        var panel = UiInput.KeepMouse(new PanelContainer());
        center.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);
        column.AddChild(new Label { Text = "Choose your character", ThemeTypeVariation = "HeaderLarge", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(new Label
        {
            Text = "You play as this character in this world. Every character moves and plays the same.",
            ThemeTypeVariation = "SecondaryLabel", HorizontalAlignment = HorizontalAlignment.Center,
        });
        Picker = new CharacterPicker { Name = "Picker", Book = Book, Assets = Assets };
        column.AddChild(Picker);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        column.AddChild(buttons);
        ConfirmButton = Loc.Live(new Button { Name = "Confirm", ThemeTypeVariation = "PrimaryButton" }, ConfirmText);
        ConfirmButton.Pressed += Confirm;
        buttons.AddChild(ConfirmButton);
        Picker.SelectionChanged += _ => ConfirmButton.Text = ConfirmText();
        ConfirmButton.Text = ConfirmText();
    }

    private string ConfirmText() =>
        Picker.Selected is { } id ? Loc.T("Play as {0}", Book.Find(id)?.Name ?? id) : Loc.T("Play");

    /// <summary>Chooses the selected character.</summary>
    public void Confirm()
    {
        if (Picker.Selected is { } id)
            Chosen?.Invoke(id);
    }
}
