namespace Mechanica.Core.World;

/// <summary>
/// The world's time of day: a world starts at <see cref="StartHour"/> and one <see cref="WorldGenDefinition.DayLength"/>
/// of world time (seconds simulated, saved with the world) is a whole day. Scripts read it as <c>world.time_of_day()</c>
/// and the sky follows it.
/// </summary>
public static class WorldClock
{
    /// <summary>The hour a new world starts at.</summary>
    public const double StartHour = 6;

    /// <summary>The hour, 0 (midnight) up to 24, after <paramref name="time"/> seconds of a world whose day lasts
    /// <paramref name="dayLength"/> seconds (a bad length counts as <see cref="WorldGenDefinition.DefaultDayLength"/>).</summary>
    public static double HourAt(double time, double dayLength)
    {
        if (!(dayLength > 0) || !double.IsFinite(dayLength))
            dayLength = WorldGenDefinition.DefaultDayLength;
        if (!double.IsFinite(time))
            time = 0;
        var hours = (StartHour + 24 * (time % dayLength) / dayLength) % 24;
        return hours < 0 ? hours + 24 : hours;
    }
}
