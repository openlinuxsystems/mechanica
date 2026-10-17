using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// The boot splash (UI spec §14): the title logo on deep navy for a moment when the game starts, then a fade into
/// the home screen. It keeps clicks off the menu while it shows and frees itself when done.
/// </summary>
public partial class BootSplash : CanvasLayer
{
    /// <summary>Seconds of the fade into the home screen.</summary>
    public const float FadeSeconds = 0.3f;

    private Control _root = null!;

    /// <summary>Seconds the logo shows before the fade; the shipped timing unless set before adding to the tree.</summary>
    public double? Seconds { get; set; }

    public override void _Ready()
    {
        Layer = 30;
        Seconds ??= LoadingScreen.LoadSettings().SplashSeconds;
        _root = UiInput.KeepMouse(new Control { Name = "Splash" });
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        _root.AddChild(new ColorRect { Color = UiTokens.DeepNavy, AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore });
        var centre = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(centre);
        centre.AddChild(Logo.Create(96));
        var fade = CreateTween();
        fade.TweenInterval(Seconds.Value);
        fade.TweenCallback(Callable.From(() => _root.MouseFilter = Control.MouseFilterEnum.Ignore));
        fade.TweenProperty(_root, "modulate:a", 0f, FadeSeconds);
        fade.TweenCallback(Callable.From(QueueFree));
    }
}
