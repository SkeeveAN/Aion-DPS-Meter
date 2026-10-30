using System.IO;
using System.Text.Json;

namespace AionDPS.Schedule;

/// <summary>One recurring time window of an event: the weekdays (0 = Sunday ... 6 = Saturday, like
/// <see cref="DayOfWeek"/>) it starts on and its start and end time of day. An end at or before the
/// start runs past midnight.</summary>
public sealed record EventWindow(IReadOnlyList<int> Days, TimeSpan Start, TimeSpan End);

/// <summary>A single dated occurrence (the next Abyss raid, read off the game's countdown) in local
/// time. Without a known length (<see cref="Minutes"/> 0) it is only ever "starts in ...".</summary>
public sealed record EventDate(DateTime At, int Minutes);

/// <summary>A recurring game event (a battlefield's matchmaking times), named per language. An
/// <see cref="Always"/> event can be queued for at any time (the 1 vs 1 arena) and has no windows.</summary>
public sealed record ScheduledEvent(string Id, string Kind, string? Players, IReadOnlyDictionary<string, string> Names, IReadOnlyList<EventWindow> Windows, bool Always = false, IReadOnlyList<EventDate>? Dates = null, bool Utc = false, bool Possible = false, string? Zone = null)
{
    /// <summary>The times of <see cref="Windows"/> and <see cref="Dates"/> are UTC, the clock the game's events run on (the same in every region); they are turned into the PC's local time when the occurrences are worked out. Without it they are read as local time.</summary>
    public bool IsUtc => Utc;

    /// <summary>The time zone the times of this event are given in when it has its own (the spacetime rift runs on the European local clock, so it keeps 14:00, 17:00 ... through the change to winter time), else null: <see cref="IsUtc"/> or local time applies.</summary>
    public TimeZoneInfo? SourceZone { get; } = Zone is null ? null : EventSchedule.FindZone(Zone);

    /// <summary>The slots are only possible times: the game starts the event at random within them (the Dimensional Invasion does not take place at every slot), so the timetable says "possible" instead of promising it.</summary>
    public bool IsPossible => Possible;

    /// <summary>The name in a language (an ISO 639-1 code), else English, else the id.</summary>
    public string NameIn(string language) =>
        Names.TryGetValue(language, out string? name) ? name : Names.TryGetValue("en", out string? english) ? english : Id;
}

/// <summary>An event that is on right now (<see cref="Remaining"/> until it ends) or starts soon
/// (<see cref="Until"/> it starts).</summary>
public sealed record EventOccurrence(ScheduledEvent Event, DateTime Start, DateTime End)
{
    public bool IsAlways => Event.Always;

    public bool IsActiveAt(DateTime now) => IsAlways || (Start <= now && now < End);
}

/// <summary>
/// The event timetable from assets/aion2/schedule/events.json and the question the timetable overlay
/// asks of it: what is active NOW, and what starts within the next minutes. The times are those the
/// game's own pages show, on the European clock ("timeZone": "Europe/Berlin": the events keep their hour through the change to winter time), as UTC ("UTC") or, with "local", as the PC's local time.
/// </summary>
public static class EventSchedule
{
    private static readonly string Path_ = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "schedule", "events.json");

    private static volatile IReadOnlyList<ScheduledEvent>? _events;

    /// <summary>The timetable in use: the last one the server sent (<see cref="ScheduleClient"/>), else the one shipped with the client.</summary>
    public static IReadOnlyList<ScheduledEvent> Events => _events ??= LoadCachedOrShipped();

    /// <summary>Takes a timetable the server sent in use, once it was read as a whole.</summary>
    public static void Replace(IReadOnlyList<ScheduledEvent> events) => _events = events;

    private static IReadOnlyList<ScheduledEvent> LoadCachedOrShipped()
    {
        IReadOnlyList<ScheduledEvent> cached = Load(ScheduleClient.CachePath);
        return cached.Count > 0 ? cached : Load(Path_);
    }

    public static IReadOnlyList<ScheduledEvent> Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Array.Empty<ScheduledEvent>();
        }
        catch (IOException)
        {
            return Array.Empty<ScheduledEvent>();
        }
    }

    /// <summary>Reads a timetable from its JSON text; an empty list when it is not one.</summary>
    public static IReadOnlyList<ScheduledEvent> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var events = new List<ScheduledEvent>();
            bool hasZone = document.RootElement.TryGetProperty("timeZone", out var zone);
            bool utc = hasZone && string.Equals(zone.GetString(), "UTC", StringComparison.OrdinalIgnoreCase);
            // A named zone for the whole table (the game's events run on the European clock); "UTC" and "local" are handled by the flag above.
            string? tableZone = hasZone && zone.GetString() is { } zoneName && !utc && !string.Equals(zoneName, "local", StringComparison.OrdinalIgnoreCase) ? zoneName : null;
            foreach (JsonElement e in document.RootElement.GetProperty("events").EnumerateArray())
            {
                var names = e.GetProperty("names").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                var windows = e.GetProperty("windows").EnumerateArray()
                    .Select(w => new EventWindow(
                        w.GetProperty("days").EnumerateArray().Select(d => d.GetInt32()).ToList(),
                        TimeSpan.Parse(w.GetProperty("start").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                        TimeSpan.Parse(w.GetProperty("end").GetString()!, System.Globalization.CultureInfo.InvariantCulture)))
                    .ToList();
                var dates = e.TryGetProperty("dates", out var dateList)
                    ? dateList.EnumerateArray().Select(x => new EventDate(
                        DateTime.Parse(x.GetProperty("at").GetString()!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None),
                        x.TryGetProperty("minutes", out var minutes) ? minutes.GetInt32() : 0)).ToList()
                    : new List<EventDate>();
                string? eventZone = e.TryGetProperty("timeZone", out var ownZone) ? ownZone.GetString() : tableZone;
                events.Add(new ScheduledEvent(
                    e.GetProperty("id").GetString()!,
                    e.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "" : "",
                    e.TryGetProperty("players", out var players) ? players.GetString() : null,
                    names, windows, e.TryGetProperty("always", out var always) && always.GetBoolean(), dates, utc,
                    e.TryGetProperty("possible", out var possible) && possible.GetBoolean(), eventZone));
            }

            return events;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            // No table: an empty timetable.
            return Array.Empty<ScheduledEvent>();
        }
    }

    /// <summary>A time zone by its IANA name (Windows only knows its own ids), or null when this machine has neither.</summary>
    public static TimeZoneInfo? FindZone(string id)
    {
        foreach (string candidate in id == "Europe/Berlin" ? new[] { id, "W. Europe Standard Time" } : new[] { id })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return null;
    }

    /// <summary>
    /// The occurrences on at <paramref name="now"/> (<c>Active</c>, soonest end first) and those
    /// starting within <paramref name="lookahead"/> (<c>Soon</c>, soonest start first).
    /// </summary>
    public static (List<EventOccurrence> Active, List<EventOccurrence> Soon) Evaluate(
        IEnumerable<ScheduledEvent> events, DateTime now, TimeSpan lookahead)
    {
        var active = new List<EventOccurrence>();
        var soon = new List<EventOccurrence>();
        foreach (ScheduledEvent scheduled in events)
        {
            if (scheduled.Always)
            {
                active.Add(new EventOccurrence(scheduled, now.Date, now.Date.AddDays(1)));
                continue;
            }

            foreach (EventOccurrence occurrence in OccurrencesAround(scheduled, now))
            {
                if (occurrence.IsActiveAt(now))
                {
                    active.Add(occurrence);
                }
                else if (occurrence.Start > now && occurrence.Start - now <= lookahead)
                {
                    soon.Add(occurrence);
                }
            }
        }

        return (active.OrderBy(o => o.IsAlways).ThenBy(o => o.End).ToList(), soon.OrderBy(o => o.Start).ToList());
    }

    /// <summary>The occurrences of an event around <paramref name="now"/> (from yesterday to tomorrow), for the sound reminders.</summary>
    public static IEnumerable<EventOccurrence> Upcoming(ScheduledEvent scheduled, DateTime now) => OccurrencesAround(scheduled, now);

    /// <summary>The next occurrence of an event after <paramref name="now"/> (within a week), or null.</summary>
    public static EventOccurrence? NextAfter(ScheduledEvent scheduled, DateTime now) =>
        scheduled.Always ? null : OccurrencesAround(scheduled, now, daysBefore: 0, daysAfter: 7).Where(o => o.Start > now).OrderBy(o => o.Start).FirstOrDefault();

    /// <summary>Occurrences that start from the day before to the day after (a window past midnight
    /// started yesterday).</summary>
    private static IEnumerable<EventOccurrence> OccurrencesAround(ScheduledEvent scheduled, DateTime now, int daysBefore = 1, int daysAfter = 1)
    {
        // Events on their own clock (UTC or a named zone): the days and times are worked out on that clock and handed back as local time (so a winter or a PC in another zone shows the right hour).
        TimeZoneInfo? zone = scheduled.SourceZone ?? (scheduled.IsUtc ? TimeZoneInfo.Utc : null);
        DateTime reference = zone is null ? now : TimeZoneInfo.ConvertTimeFromUtc(now.ToUniversalTime(), zone);
        DateTime ToLocalTime(DateTime t)
        {
            if (zone is null)
            {
                return t;
            }

            // A time in the hour that the spring change skips does not exist: it falls to the hour after.
            if (zone.IsInvalidTime(t))
            {
                t = t.AddHours(1);
            }

            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), zone).ToLocalTime();
        }

        foreach (EventDate date in scheduled.Dates ?? Array.Empty<EventDate>())
        {
            yield return new EventOccurrence(scheduled, ToLocalTime(date.At), ToLocalTime(date.At.AddMinutes(date.Minutes)));
        }

        for (int offset = -daysBefore; offset <= daysAfter; offset++)
        {
            DateTime day = reference.Date.AddDays(offset);
            foreach (EventWindow window in scheduled.Windows)
            {
                if (!window.Days.Contains((int)day.DayOfWeek))
                {
                    continue;
                }

                DateTime start = day + window.Start;
                DateTime end = day + window.End;
                if (end <= start)
                {
                    end = end.AddDays(1);
                }

                yield return new EventOccurrence(scheduled, ToLocalTime(start), ToLocalTime(end));
            }
        }
    }
}
