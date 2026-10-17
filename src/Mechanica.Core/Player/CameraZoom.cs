namespace Mechanica.Core.Player;

/// <summary>
/// Third-person camera zoom (UI spec §1, §6): the camera starts <see cref="PlayerMovementSettings.CameraDistance"/>
/// behind the player. Zoom in (X) and zoom out (C) move it by <see cref="PlayerMovementSettings.CameraZoomStep"/>
/// between <see cref="PlayerMovementSettings.CameraMinDistance"/> and <see cref="PlayerMovementSettings.CameraMaxDistance"/>;
/// zooming in past the closest distance goes first-person (0), and zooming out from first-person comes back to it.
/// </summary>
public static class CameraZoom
{
    /// <summary>The camera distance after one zoom step from <paramref name="distance"/>, in metres (0 is first-person).</summary>
    public static float Step(float distance, bool zoomIn, PlayerMovementSettings settings)
    {
        var (min, max, step) = (settings.CameraMinDistance, settings.CameraMaxDistance, settings.CameraZoomStep);
        if (zoomIn)
            return distance <= min + 1e-4f ? 0f : Math.Max(min, distance - step);
        return distance < min - 1e-4f ? min : Math.Min(max, distance + step);
    }
}
