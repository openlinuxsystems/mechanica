using System.Globalization;

namespace Mechanica.Core.Ui;

/// <summary>
/// The blue industrial palette every menu, HUD element and mod tool uses (UI spec §13 "Visual Language").
/// Colours are <c>#RRGGBB</c> hex strings so Core stays free of Godot; the game turns them into its theme.
/// There is no orange anywhere: <see cref="IsOrange"/> is how tests check that.
/// </summary>
public static class UiPalette
{
    /// <summary>Panel background, dark navy.</summary>
    public const string Navy = "#1A2332";

    /// <summary>Opacity of panel backgrounds.</summary>
    public const float PanelOpacity = 0.85f;

    /// <summary>Full-screen loading and boot background, darker than panels (§15).</summary>
    public const string DeepNavy = "#0D1520";

    /// <summary>Button and input fill, darker than panels so they read as controls.</summary>
    public const string ControlFill = "#121A26";

    /// <summary>Pressed button fill.</summary>
    public const string ControlPressed = "#0B111A";

    /// <summary>Borders and accents.</summary>
    public const string Blue = "#2E86DE";

    /// <summary>Highlights, hover, selection, focus and progress.</summary>
    public const string Cyan = "#00D4FF";

    /// <summary>Primary text.</summary>
    public const string Text = "#FFFFFF";

    /// <summary>Secondary text.</summary>
    public const string TextSecondary = "#B0B8C1";

    /// <summary>Disabled controls and their text.</summary>
    public const string Disabled = "#5A6470";

    /// <summary>Errors, refusals and failed loads (muted red, spec §14–15).</summary>
    public const string Error = "#E5534B";

    /// <summary>Warnings: pale yellow, kept well away from orange.</summary>
    public const string Warning = "#E8E3A0";

    /// <summary>Success and "running" states.</summary>
    public const string Success = "#4CD07D";

    /// <summary>The health bar's fill: a green to go with <see cref="Blue"/>, drawn with a faint top-to-bottom shade.</summary>
    public const string Health = "#36B36B";

    /// <summary>Behind a full-screen station window, dims the world (UI spec §0); drawn at <see cref="ScrimOpacity"/>.</summary>
    public const string Scrim = "#0A1220";

    /// <summary>Opacity of <see cref="Scrim"/>.</summary>
    public const float ScrimOpacity = 0.45f;

    /// <summary>Window headers and inactive tabs (§0 design language).</summary>
    public const string Chrome = "#1E2F48";

    /// <summary>Buttons and fields on <see cref="Chrome"/>.</summary>
    public const string Chrome2 = "#263A57";

    /// <summary>The few hairlines left: field edges and chip outlines.</summary>
    public const string ChromeLine = "#34507A";

    /// <summary>The browsing surface: grids, lists and the active tab.</summary>
    public const string Surface = "#4B9BE6";

    /// <summary>Bands on the surface: the queue strip, drawers and scroll tracks.</summary>
    public const string SurfaceDeep = "#3A86D2";

    /// <summary>Item and recipe tiles, and hotbar slots.</summary>
    public const string Tile = "#6CB2EE";

    /// <summary>A hovered tile.</summary>
    public const string TileHover = "#86C1F4";

    /// <summary>The selected tile and the current hotbar slot (with a white ring).</summary>
    public const string TileSelected = "#9ACDF7";

    /// <summary>Detail cards, dialogs and tooltips.</summary>
    public const string Card = "#22344F";

    /// <summary>Panels inside a card: stats, material tiles and fields.</summary>
    public const string Inset = "#2B4060";

    /// <summary>Text and numbers on <see cref="Surface"/> and <see cref="Tile"/> (white never sits on them).</summary>
    public const string Ink = "#0E2340";

    /// <summary>Secondary text on <see cref="Chrome"/> and <see cref="Card"/>.</summary>
    public const string TextMuted = "#B7C7DC";

    /// <summary>Primary buttons, progress, focus, filled pips and the level pill.</summary>
    public const string Accent = "#38C8FF";

    /// <summary>Text on <see cref="Accent"/>.</summary>
    public const string AccentInk = "#0B1E33";

    /// <summary>Text for "enough" and running states on a card.</summary>
    public const string SuccessText = "#5EE08A";

    /// <summary>Fill for finished, ready items (the queue strip's ready cell).</summary>
    public const string SuccessFill = "#7FE0A6";

    /// <summary>Text for short and refused states on a card.</summary>
    public const string ErrorText = "#FF7A7A";

    /// <summary>Dots and rings for short and refused states.</summary>
    public const string ErrorMark = "#E5484D";

    /// <summary>Empty stat pips.</summary>
    public const string PipOff = "#3A5070";

    /// <summary>A disabled primary button's flat fill.</summary>
    public const string DisabledFill = PipOff;

    /// <summary>A disabled primary button's text.</summary>
    public const string DisabledText = "#8EA3BF";

    /// <summary>Rigid (welded) connections in previews and gizmos: green, like success.</summary>
    public const string JointRigid = Success;

    /// <summary>Bearings (hinges) and rotational sockets.</summary>
    public const string JointHinge = Blue;

    /// <summary>Pistons (sliders) and linear sockets: teal.</summary>
    public const string JointSlider = "#2FD9C4";

    /// <summary>Suspension (springs) and spring sockets: violet.</summary>
    public const string JointSpring = "#A47CF3";

    /// <summary>Faces where nothing would attach.</summary>
    public const string NoConnection = Error;

    /// <summary>The selected socket or handle in a gizmo.</summary>
    public const string GizmoSelected = Text;

    /// <summary>Collision wireframes.</summary>
    public const string Collision = Cyan;

    /// <summary>Centre-of-mass markers.</summary>
    public const string CentreOfMass = Error;

    /// <summary>Locked joints in the joint overlay.</summary>
    public const string JointLocked = Disabled;

    /// <summary>A logic connection whose source is on.</summary>
    public const string SignalOn = Cyan;

    /// <summary>A logic connection whose source is off.</summary>
    public const string SignalOff = Blue;

    /// <summary>A logic connection node (UI spec §12): solid blue.</summary>
    public const string NodeLogic = Blue;

    /// <summary>A power connection node: solid cyan.</summary>
    public const string NodePower = Cyan;

    /// <summary>A bearing connection node: white, with a <see cref="Blue"/> ring.</summary>
    public const string NodeBearing = Text;

    /// <summary>A structural or weld-related connection node: light gray.</summary>
    public const string NodeStructural = "#C3CBD6";

    /// <summary>Force arrows in the force overlay (aero and thrust).</summary>
    public const string ForceArrow = Cyan;

    /// <summary>Torque lines in the force overlay.</summary>
    public const string TorqueArrow = JointSpring;

    /// <summary>Background of 3D previews in the mod tools.</summary>
    public const string PreviewBackground = Navy;

    /// <summary>Lua keywords (<c>local</c>, <c>function</c>, …).</summary>
    public const string SyntaxKeyword = "#C586C0";

    /// <summary>Lua standard libraries (<c>math</c>, <c>string</c>, …).</summary>
    public const string SyntaxLibrary = "#4EC9B0";

    /// <summary>Mechanica's Lua API names and members.</summary>
    public const string SyntaxApi = "#4FC1FF";

    /// <summary>Callbacks the game calls (<c>on_tick</c>, …).</summary>
    public const string SyntaxCallback = Cyan;

    /// <summary>Other function calls.</summary>
    public const string SyntaxFunction = "#8AB4F8";

    /// <summary>Table members.</summary>
    public const string SyntaxMember = "#9CDCFE";

    /// <summary>String literals.</summary>
    public const string SyntaxString = "#8FD9A8";

    /// <summary>Number literals.</summary>
    public const string SyntaxNumber = "#D7BAFF";

    /// <summary>Operators and punctuation.</summary>
    public const string SyntaxSymbol = "#D4D4D4";

    /// <summary>Comments.</summary>
    public const string SyntaxComment = "#6A8A9A";

    /// <summary>Hover scale for buttons (§13 "Buttons").</summary>
    public const float ButtonHoverScale = 1.05f;

    /// <summary>Seconds of hover before a tooltip shows (§9, §13).</summary>
    public const float TooltipDelaySeconds = 0.25f;

    /// <summary>Smallest click target at 1080p, in pixels (§13 "Layout Rules").</summary>
    public const int MinClickTarget = 32;

    /// <summary>Smallest supported window (§11).</summary>
    public const int MinWindowWidth = 1280, MinWindowHeight = 720;

    /// <summary>The UI scale range offered in Options (§11, §16).</summary>
    public const float MinUiScale = 0.75f, MaxUiScale = 1.5f;

    /// <summary>Every named colour, for tests and the style guide.</summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [nameof(Navy)] = Navy,
        [nameof(DeepNavy)] = DeepNavy,
        [nameof(ControlFill)] = ControlFill,
        [nameof(ControlPressed)] = ControlPressed,
        [nameof(Blue)] = Blue,
        [nameof(Cyan)] = Cyan,
        [nameof(Text)] = Text,
        [nameof(TextSecondary)] = TextSecondary,
        [nameof(Disabled)] = Disabled,
        [nameof(Error)] = Error,
        [nameof(Warning)] = Warning,
        [nameof(Success)] = Success,
        [nameof(Health)] = Health,
        [nameof(Scrim)] = Scrim,
        [nameof(Chrome)] = Chrome,
        [nameof(Chrome2)] = Chrome2,
        [nameof(ChromeLine)] = ChromeLine,
        [nameof(Surface)] = Surface,
        [nameof(SurfaceDeep)] = SurfaceDeep,
        [nameof(Tile)] = Tile,
        [nameof(TileHover)] = TileHover,
        [nameof(TileSelected)] = TileSelected,
        [nameof(Card)] = Card,
        [nameof(Inset)] = Inset,
        [nameof(Ink)] = Ink,
        [nameof(TextMuted)] = TextMuted,
        [nameof(Accent)] = Accent,
        [nameof(AccentInk)] = AccentInk,
        [nameof(SuccessText)] = SuccessText,
        [nameof(SuccessFill)] = SuccessFill,
        [nameof(ErrorText)] = ErrorText,
        [nameof(ErrorMark)] = ErrorMark,
        [nameof(PipOff)] = PipOff,
        [nameof(DisabledText)] = DisabledText,
    };

    /// <summary>Every overlay, gizmo and preview colour (mod tools and debug views), for tests and the style guide.</summary>
    public static IReadOnlyDictionary<string, string> Overlays { get; } = new Dictionary<string, string>
    {
        [nameof(JointRigid)] = JointRigid,
        [nameof(JointHinge)] = JointHinge,
        [nameof(JointSlider)] = JointSlider,
        [nameof(JointSpring)] = JointSpring,
        [nameof(NoConnection)] = NoConnection,
        [nameof(GizmoSelected)] = GizmoSelected,
        [nameof(Collision)] = Collision,
        [nameof(CentreOfMass)] = CentreOfMass,
        [nameof(JointLocked)] = JointLocked,
        [nameof(SignalOn)] = SignalOn,
        [nameof(SignalOff)] = SignalOff,
        [nameof(NodeLogic)] = NodeLogic,
        [nameof(NodePower)] = NodePower,
        [nameof(NodeBearing)] = NodeBearing,
        [nameof(NodeStructural)] = NodeStructural,
        [nameof(ForceArrow)] = ForceArrow,
        [nameof(TorqueArrow)] = TorqueArrow,
        [nameof(PreviewBackground)] = PreviewBackground,
    };

    /// <summary>Every Lua syntax colour in the script editor, for tests and the style guide.</summary>
    public static IReadOnlyDictionary<string, string> Syntax { get; } = new Dictionary<string, string>
    {
        [nameof(SyntaxKeyword)] = SyntaxKeyword,
        [nameof(SyntaxLibrary)] = SyntaxLibrary,
        [nameof(SyntaxApi)] = SyntaxApi,
        [nameof(SyntaxCallback)] = SyntaxCallback,
        [nameof(SyntaxFunction)] = SyntaxFunction,
        [nameof(SyntaxMember)] = SyntaxMember,
        [nameof(SyntaxString)] = SyntaxString,
        [nameof(SyntaxNumber)] = SyntaxNumber,
        [nameof(SyntaxSymbol)] = SyntaxSymbol,
        [nameof(SyntaxComment)] = SyntaxComment,
    };

    /// <summary>Parses <c>#RRGGBB</c> into channels from 0 to 1.</summary>
    public static (float R, float G, float B) Parse(string hex)
    {
        var s = hex.StartsWith('#') ? hex[1..] : hex;
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
        return (((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
    }

    /// <summary>
    /// True for colours a player would call orange or amber: hue from 10° to 50° with enough saturation
    /// and brightness to read as a colour rather than brown-grey. The theme rule (§7, §13) is no orange.
    /// </summary>
    public static bool IsOrange(float r, float g, float b)
    {
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var chroma = max - min;
        if (max < 0.35f || chroma / max < 0.45f || max != r)
            return false;
        var hue = 60f * ((g - b) / chroma);
        if (hue < 0)
            hue += 360f;
        return hue is >= 10f and <= 50f;
    }

    /// <summary>The WCAG contrast ratio between two <c>#RRGGBB</c> colours (1 to 21).</summary>
    public static float Contrast(string a, string b)
    {
        static float Luminance(string hex)
        {
            var (r, g, bl) = Parse(hex);
            static float Linear(float c) => c <= 0.03928f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Linear(r) + 0.7152f * Linear(g) + 0.0722f * Linear(bl);
        }
        var (la, lb) = (Luminance(a), Luminance(b));
        return (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);
    }

    /// <summary>Clamps a UI scale to the supported range.</summary>
    public static float ClampUiScale(float scale) => Math.Clamp(scale, MinUiScale, MaxUiScale);
}
