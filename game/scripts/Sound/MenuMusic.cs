using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Sound;
using Mechanica.Game.Mods;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Sound;

/// <summary>
/// The home screen's music (UI spec §14): the <c>music_menu</c> sound event, read from the installed mods
/// in the background so the menu opens at once, played on the Music bus (so Options' Music slider sets its
/// volume). Test scenes stay quiet.
/// </summary>
public partial class MenuMusic : Node
{
    /// <summary>The sound event played.</summary>
    public const string SoundId = "music_menu";

    private SoundPlayer _player = null!;

    /// <summary>The music playing, or null.</summary>
    public SoundPlayer.Voice? Voice { get; private set; }

    public override void _Ready()
    {
        _player = new SoundPlayer { Name = "Player" };
        AddChild(_player);
        if (!GameSettings.Persist)
            return;
        var root = ModPaths.ModsRoot;
        var profile = ModPaths.ProfilePath;
        _ = Task.Run(() => SoundRegistry.Load(ModLoader.LoadWithProfile(root, profile)))
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                    Callable.From(() => Play(t.Result)).CallDeferred();
            });
    }

    /// <summary>Starts the music from <paramref name="registry"/>.</summary>
    public void Play(SoundRegistry registry)
    {
        if (!IsInsideTree() || Voice is not null)
            return;
        _player.Registry = registry;
        if (registry.Find(SoundId, Identifiers.VanillaModId) is { } music)
            Voice = _player.Play(music, Vector3.Zero);
    }
}
