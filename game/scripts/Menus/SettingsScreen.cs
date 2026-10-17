using Godot;
using Mechanica.Core.Saves;

namespace Mechanica.Game.Menus;

/// <summary>The main menu's Settings: the player's name, kept in the player profile (<c>user://profile.json</c>).</summary>
public partial class SettingsScreen : PanelContainer
{
    private LineEdit _name = null!;
    private Label _status = null!;

    /// <summary>Raised by Back.</summary>
    public event Action? CloseRequested;

    /// <summary>The profile file; null uses <see cref="Main.ProfilePath"/>.</summary>
    public string? ProfileFile { get; set; }

    /// <summary>The name box.</summary>
    public LineEdit PlayerName => _name;

    /// <summary>What Save last said.</summary>
    public string Status => _status.Text;

    private string Path => ProfileFile ?? ProjectSettings.GlobalizePath(Main.ProfilePath);

    public override void _Ready()
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        AddChild(column);
        column.AddChild(new Label { Text = "Settings", ThemeTypeVariation = "HeaderLarge", HorizontalAlignment = HorizontalAlignment.Center });
        var row = new HBoxContainer();
        column.AddChild(row);
        row.AddChild(new Label { Text = "Player name" });
        _name = new LineEdit { Text = Main.UserProfile(null, Path).Name, MaxLength = PlayerProfile.MaxNameLength, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _name.TextSubmitted += _ => Save();
        row.AddChild(_name);
        column.AddChild(new Label
        {
            Text = "Other players see this name. Saved worlds know you by your profile, not your name, so you can change it any time.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _status = new Label();
        column.AddChild(_status);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        column.AddChild(buttons);
        var back = new Button { Text = "Back" };
        back.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(back);
        var save = new Button { Text = "Save" };
        save.Pressed += Save;
        buttons.AddChild(save);
    }

    /// <summary>Writes the name to the profile.</summary>
    public void Save()
    {
        var profile = Main.UserProfile(_name.Text, Path);
        _name.Text = profile.Name;
        _status.Text = $"Saved. You are {profile.Name}.";
    }
}
