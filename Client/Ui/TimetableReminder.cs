using AionDPS.Schedule;

namespace AionDPS.Ui;

/// <summary>
/// The sound reminders of the timetable: once a second it looks at every event that has a sound chosen (Settings: Timetable) and plays it when the
/// event starts in the chosen number of minutes. Each start is announced once; an event already inside the lead time when the meter starts is
/// not announced late (only the first seconds after the moment count).
/// </summary>
public sealed class TimetableReminder : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<string> _played = new();
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;

    public TimetableReminder()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 5)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
        }

        if (!_settings.TimetableNotify)
        {
            return;
        }

        TimeSpan lead = TimeSpan.FromMinutes(Math.Clamp(_settings.TimetableSoundMinutes, 0, 60));
        DateTime now = DateTime.Now;
        foreach (ScheduledEvent scheduled in EventSchedule.Events)
        {
            if (scheduled.Always || !_settings.TimetableEvents.TryGetValue(scheduled.Id, out var setting) || setting.Sound.Length == 0 || setting.Volume <= 0)
            {
                continue;
            }

            foreach (EventOccurrence occurrence in EventSchedule.Upcoming(scheduled, now))
            {
                TimeSpan sinceMoment = now - (occurrence.Start - lead); // 0 at the moment the sound is due
                if (sinceMoment < TimeSpan.Zero || sinceMoment > TimeSpan.FromSeconds(5))
                {
                    continue;
                }

                if (_played.Add($"{scheduled.Id}|{occurrence.Start:O}|{lead.TotalMinutes}"))
                {
                    NotifySounds.Play(setting.Sound, setting.Volume);
                }
            }
        }

        if (_played.Count > 500)
        {
            _played.Clear();
        }
    }

    public void Dispose() => _timer.Stop();
}
