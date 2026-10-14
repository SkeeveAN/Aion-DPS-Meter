namespace AionDPS.Aion2;

/// <summary>
/// The clock the game's resets run on. The client's tables name the times as "16:00" (every day, and Wednesdays for the weekly ones); that is Korean time
/// (UTC+9, no daylight saving): in Central Europe it is 09:00 in summer and 08:00 in winter, which is when the daily dungeon's 14 entries come back.
/// The meter works in the player's local time, so every reset is worked out on the server clock and converted back.
/// </summary>
public static class ServerTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(9);

    /// <summary>The server's wall clock for a local time. A test replaces it to work with plain dates.</summary>
    public static Func<DateTime, DateTime> FromLocal { get; set; } = local => local.ToUniversalTime() + Offset;

    /// <summary>The local time of a moment on the server's wall clock.</summary>
    public static Func<DateTime, DateTime> ToLocal { get; set; } = server => DateTime.SpecifyKind(server - Offset, DateTimeKind.Utc).ToLocalTime();

    /// <summary>The server clock is the local one: tests use plain dates, which they read as server times.</summary>
    public static void UseDatesAsServerTimes()
    {
        FromLocal = t => t;
        ToLocal = t => t;
    }

    public static void Reset()
    {
        FromLocal = local => local.ToUniversalTime() + Offset;
        ToLocal = server => DateTime.SpecifyKind(server - Offset, DateTimeKind.Utc).ToLocalTime();
    }
}
