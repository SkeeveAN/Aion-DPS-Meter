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
public sealed record ScheduledEvent(string Id, string Kind, string? Players, IReadOnlyDictionary<string, string> Names, IReadOnlyList<EventWindow> Windows, bool Always = false, IReadOnlyList<EventDate>? Dates = null)
{
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
/// game's own pages show; "timeZone": "local" means they are read as the PC's local time.
/// </summary>
public static class EventSchedule
{
    private static readonly string Path_ = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "schedule", "events.json");

    private static IReadOnlyList<ScheduledEvent>? _events;

    public static IReadOnlyList<ScheduledEvent> Events => _events ??= Load(Path_);

    public static IReadOnlyList<ScheduledEvent> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Array.Empty<ScheduledEvent>();
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var events = new List<ScheduledEvent>();
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
                events.Add(new ScheduledEvent(
                    e.GetProperty("id").GetString()!,
                    e.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "" : "",
                    e.TryGetProperty("players", out var players) ? players.GetString() : null,
                    names, windows, e.TryGetProperty("always", out var always) && always.GetBoolean(), dates));
            }

            return events;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            // No table: an empty timetable.
            return Array.Empty<ScheduledEvent>();
        }
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
        foreach (EventDate date in scheduled.Dates ?? Array.Empty<EventDate>())
        {
            yield return new EventOccurrence(scheduled, date.At, date.At.AddMinutes(date.Minutes));
        }

        for (int offset = -daysBefore; offset <= daysAfter; offset++)
        {
            DateTime day = now.Date.AddDays(offset);
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

                yield return new EventOccurrence(scheduled, start, end);
            }
        }
    }
}
