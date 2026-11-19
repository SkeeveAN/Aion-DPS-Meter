using System.Windows.Threading;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>Looks at the live directory four times a second and plays the PvP sounds of <see cref="PvpWatch"/> (one sound at a time, not a burst when many enemies come at once; the marking sound
/// wins over the arrival sound). The settings are read again every few seconds. Never takes the meter down: an error ends the ticker, not the meter.</summary>
public sealed class PvpAlertTicker
{
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly PvpWatch _watch = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private DateTime _lastSound = DateTime.MinValue;

    public PvpAlertTicker(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();

    private void Tick()
    {
        try
        {
            Aion2EntityDirectory? entities = _directory();
            if (entities is null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (now - _settingsAt >= TimeSpan.FromSeconds(3))
            {
                _settings = MeterSettings.Load();
                _settingsAt = now;
            }

            IReadOnlyList<PvpAlert> alerts = _watch.Poll(entities, entities.ZoneId, now, _settings.PvpAlertNear, _settings.PvpAlertTargeted);
            if (alerts.Count == 0 || now - _lastSound < TimeSpan.FromSeconds(3))
            {
                return;
            }

            _lastSound = now;
            if (alerts.Any(a => a.Kind == PvpAlertKind.TargetedByEnemy))
            {
                NotifySounds.PlayPvpTargeted(_settings.PvpAlertVolume);
            }
            else
            {
                NotifySounds.PlayPvpNear(_settings.PvpAlertVolume);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.WriteLine($"[pvp alert] ticker stopped after an error: {e.GetType().Name}: {e.Message}");
            _timer.Stop();
        }
    }
}
