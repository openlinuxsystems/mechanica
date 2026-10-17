using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// Keeps a panel's mouse input to itself. Godot stops clicks at a <see cref="Control.MouseFilterEnum.Stop"/>
/// control but lets a wheel turn that nothing scrolled (past the end of a list) fall through to the game,
/// where it changes the hotbar or takes the mouse back and closes the menu. Windows and menus call
/// <see cref="KeepMouse"/> so neither happens.
/// </summary>
public static class UiInput
{
    /// <summary>Stops clicks and wheel turns over <paramref name="panel"/> from reaching the game.</summary>
    public static T KeepMouse<T>(T panel) where T : Control
    {
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        panel.GuiInput += e =>
        {
            if (e is InputEventMouseButton)
                panel.AcceptEvent();
        };
        return panel;
    }
}
