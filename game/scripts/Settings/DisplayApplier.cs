using Godot;
using Mechanica.Core.Settings;
using Mechanica.Game.Sound;
using DisplayMode = Mechanica.Core.Settings.DisplayMode;

namespace Mechanica.Game.Settings;

/// <summary>
/// Puts graphics and audio settings into effect (UI spec §16). The window (size, mode, VSync) and the root
/// viewport (anti-aliasing, texture sharpness, shadow atlas) change at once; cameras get the view distance
/// and world environments the effects level as they enter the tree, so a world loaded later still matches.
/// Volumes go to the sound buses; Ambient follows SFX.
/// </summary>
public static class DisplayApplier
{
    private static GraphicsSettings _graphics = new();

    /// <summary>Applies the window settings: display mode, size in windowed mode and VSync.</summary>
    public static void ApplyWindow(GraphicsSettings g)
    {
        if (DisplayServer.GetName() == "headless")
            return;
        var mode = g.DisplayMode switch
        {
            DisplayMode.Borderless => DisplayServer.WindowMode.Fullscreen,
            DisplayMode.Fullscreen => DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        };
        if (DisplayServer.WindowGetMode() != mode)
            DisplayServer.WindowSetMode(mode);
        if (mode == DisplayServer.WindowMode.Windowed && g.ParseResolution() is { } size)
            DisplayServer.WindowSetSize(new Vector2I(size.Width, size.Height));
        DisplayServer.WindowSetVsyncMode(g.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }

    /// <summary>Applies the quality levels to the root viewport and everything already in the tree.</summary>
    public static void ApplyQuality(SceneTree tree, GraphicsSettings g)
    {
        _graphics = g.Effective();
        var root = tree.Root;
        var aa = _graphics.Antialiasing;
        root.Msaa3D = aa switch { Quality.High => Viewport.Msaa.Msaa2X, Quality.Ultra => Viewport.Msaa.Msaa4X, _ => Viewport.Msaa.Disabled };
        root.ScreenSpaceAA = aa == Quality.Medium ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        root.TextureMipmapBias = _graphics.Textures switch { Quality.Low => 1f, Quality.Medium => 0.5f, Quality.Ultra => -0.25f, _ => 0f };
        var atlas = _graphics.Shadows switch { Quality.Low => 1024, Quality.Medium => 2048, Quality.Ultra => 8192, _ => 4096 };
        RenderingServer.DirectionalShadowAtlasSetSize(atlas, true);
        root.PositionalShadowAtlasSize = atlas;
        foreach (var node in Walk(root))
            Configure(node);
    }

    /// <summary>Gives a node entering the tree the current view distance or effects level.</summary>
    public static void Configure(Node node)
    {
        switch (node)
        {
            case Camera3D camera:
                camera.Far = _graphics.ViewDistance;
                break;
            case WorldEnvironment { Environment: { } env }:
                env.GlowEnabled = _graphics.Effects >= Quality.Medium;
                env.SsaoEnabled = _graphics.Effects >= Quality.High;
                env.SsilEnabled = _graphics.Effects >= Quality.Ultra;
                break;
            case DirectionalLight3D light:
                light.ShadowEnabled = _graphics.Shadows >= Quality.Medium;
                break;
        }
    }

    /// <summary>Sets the bus volumes and mute.</summary>
    public static void ApplyAudio(AudioSettings a)
    {
        SoundPlayer.EnsureBuses();
        SetBus("Master", a.Master);
        SetBus(SoundPlayer.BusNames[Core.Sound.SoundBus.Music], a.Music);
        SetBus(SoundPlayer.BusNames[Core.Sound.SoundBus.Sfx], a.Sfx);
        SetBus(SoundPlayer.BusNames[Core.Sound.SoundBus.Ambient], a.Sfx);
        SetBus(SoundPlayer.BusNames[Core.Sound.SoundBus.Ui], a.Ui);
        AudioServer.SetBusMute(0, a.Mute);
        if (a.OutputDevice is { } device && AudioServer.GetOutputDeviceList().Contains(device))
            AudioServer.OutputDevice = device;
    }

    private static void SetBus(string name, float volume)
    {
        var index = AudioServer.GetBusIndex(name);
        if (index >= 0)
            AudioServer.SetBusVolumeDb(index, AudioSettings.ToDecibels(volume));
    }

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
        foreach (var n in Walk(child))
            yield return n;
    }
}
