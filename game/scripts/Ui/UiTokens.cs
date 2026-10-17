using Godot;
using Mechanica.Core.Ui;

namespace Mechanica.Game.Ui;

/// <summary>
/// The <see cref="UiPalette"/> colours as Godot colours, for code that draws or tints by hand (status
/// text, overlays, materials). Menus get the same colours from <see cref="MechanicaTheme"/> instead.
/// </summary>
public static class UiTokens
{
    /// <summary>Panel background at full opacity.</summary>
    public static readonly Color Navy = From(UiPalette.Navy);

    /// <summary>Panel background at the panel opacity.</summary>
    public static readonly Color Panel = From(UiPalette.Navy, UiPalette.PanelOpacity);

    /// <summary>Loading and boot background.</summary>
    public static readonly Color DeepNavy = From(UiPalette.DeepNavy);

    /// <summary>Button and input fill.</summary>
    public static readonly Color ControlFill = From(UiPalette.ControlFill);

    /// <summary>Pressed button fill.</summary>
    public static readonly Color ControlPressed = From(UiPalette.ControlPressed);

    /// <summary>Borders and accents.</summary>
    public static readonly Color Blue = From(UiPalette.Blue);

    /// <summary>Highlights, selection, focus and progress.</summary>
    public static readonly Color Cyan = From(UiPalette.Cyan);

    /// <summary>Primary text.</summary>
    public static readonly Color Text = From(UiPalette.Text);

    /// <summary>Secondary text.</summary>
    public static readonly Color TextSecondary = From(UiPalette.TextSecondary);

    /// <summary>Disabled controls.</summary>
    public static readonly Color Disabled = From(UiPalette.Disabled);

    /// <summary>Errors and refusals.</summary>
    public static readonly Color Error = From(UiPalette.Error);

    /// <summary>Warnings.</summary>
    public static readonly Color Warning = From(UiPalette.Warning);

    /// <summary>Success and running states.</summary>
    public static readonly Color Success = From(UiPalette.Success);

    /// <summary>Rigid (welded) connections.</summary>
    public static readonly Color JointRigid = From(UiPalette.JointRigid);

    /// <summary>Bearings and rotational sockets.</summary>
    public static readonly Color JointHinge = From(UiPalette.JointHinge);

    /// <summary>Pistons and linear sockets.</summary>
    public static readonly Color JointSlider = From(UiPalette.JointSlider);

    /// <summary>Suspension and spring sockets.</summary>
    public static readonly Color JointSpring = From(UiPalette.JointSpring);

    /// <summary>Faces where nothing would attach.</summary>
    public static readonly Color NoConnection = From(UiPalette.NoConnection);

    /// <summary>The selected socket or handle.</summary>
    public static readonly Color GizmoSelected = From(UiPalette.GizmoSelected);

    /// <summary>Collision wireframes.</summary>
    public static readonly Color Collision = From(UiPalette.Collision);

    /// <summary>Centre-of-mass markers.</summary>
    public static readonly Color CentreOfMass = From(UiPalette.CentreOfMass);

    /// <summary>Locked joints.</summary>
    public static readonly Color JointLocked = From(UiPalette.JointLocked);

    /// <summary>Logic connections whose source is on.</summary>
    public static readonly Color SignalOn = From(UiPalette.SignalOn);

    /// <summary>Logic connections whose source is off.</summary>
    public static readonly Color SignalOff = From(UiPalette.SignalOff);

    /// <summary>Logic connection nodes.</summary>
    public static readonly Color NodeLogic = From(UiPalette.NodeLogic);

    /// <summary>Power connection nodes.</summary>
    public static readonly Color NodePower = From(UiPalette.NodePower);

    /// <summary>Bearing connection nodes (with a <see cref="Blue"/> ring).</summary>
    public static readonly Color NodeBearing = From(UiPalette.NodeBearing);

    /// <summary>Structural connection nodes.</summary>
    public static readonly Color NodeStructural = From(UiPalette.NodeStructural);

    /// <summary>Force arrows.</summary>
    public static readonly Color ForceArrow = From(UiPalette.ForceArrow);

    /// <summary>Torque lines.</summary>
    public static readonly Color TorqueArrow = From(UiPalette.TorqueArrow);

    /// <summary>3D preview backgrounds in the mod tools.</summary>
    public static readonly Color PreviewBackground = From(UiPalette.PreviewBackground);

    /// <summary>Converts a palette hex colour.</summary>
    public static Color From(string hex, float alpha = 1f)
    {
        var (r, g, b) = UiPalette.Parse(hex);
        return new Color(r, g, b, alpha);
    }

    /// <summary>True when <paramref name="c"/> reads as orange, which the theme forbids.</summary>
    public static bool IsOrange(Color c) => UiPalette.IsOrange(c.R, c.G, c.B);
}
