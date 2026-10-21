using System.Runtime.InteropServices;

namespace AionDPS.Ui;

/// <summary>
/// Hides the meter's overlays while a game menu is open (see <see cref="MenuDetector"/>): off until switched on in the settings, and only in the control mode that
/// hides the mouse cursor while playing. <see cref="Hidden"/> is what <see cref="GameWindow"/> reads, so every overlay that is shown only while the game is in front
/// steps aside by itself. Runs on the UI thread, so the flag needs no lock.
/// </summary>
public sealed class MenuWatcher : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly MenuDetector _detector = new();
    private readonly ControlModeLearner _learner = new();
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private DateTime _lastOwnHit = DateTime.MinValue;
    private int _look;
    private bool _held;

    public MenuWatcher()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>True while a menu is open and the overlays should be out of the way.</summary>
    public static bool Hidden { get; private set; }

    /// <summary>The control mode seen so far (also kept in the settings).</summary>
    public static ControlMode Mode { get; private set; } = ControlMode.Unknown;

    /// <summary>The local player just dealt damage (the control mode is learnt while fighting).</summary>
    public void NoteOwnHit() => _lastOwnHit = DateTime.UtcNow;

    public void Dispose()
    {
        _timer.Stop();
        Hidden = false;
    }

    private static bool AllOverlaysLocked(MeterSettings s) =>
        s.PetFarmLocked && s.PetInfoLocked && s.TimetableLocked && s.BossOverlayLocked && s.NetworkOverlayLocked && s.CombatOverlayLocked && (s.CombatCastOverlayLocked ?? true) && (s.CombatHpOverlayLocked ?? true) && (s.PetListLocked ?? true) && (s.HotkeyOverlayLocked ?? true) && s.AssistOverlayLocked && s.LootOverlayLocked && s.CraftOverlayLocked;

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
            if (Enum.TryParse(_settings.MenuControlMode, true, out ControlMode saved) && _learner.Mode == ControlMode.Unknown && saved != ControlMode.Unknown)
            {
                Mode = saved; // what an earlier session learnt counts until this one has seen enough
            }
        }

        bool inGame = GameWindow.ForegroundIsGame();
        bool cursor = inGame && GameWindow.IsCursorVisible();
        ControlMode learned = _learner.Note(DateTime.UtcNow, inGame, DateTime.UtcNow - _lastOwnHit < TimeSpan.FromSeconds(2), cursor);
        if (learned != ControlMode.Unknown && learned != Mode)
        {
            Mode = learned;
            _settings.MenuControlMode = learned.ToString();
            var onDisk = MeterSettings.Load();
            onDisk.MenuControlMode = learned.ToString();
            onDisk.Save();
        }

        if (!_settings.HideDuringMenus || Mode == ControlMode.Classic)
        {
            _detector.Reset();
            Hidden = false;
            return;
        }

        // the keys are asked only while the cursor shows, and every other look: during play this costs nothing
        if (!cursor)
        {
            _held = false;
            _look = 0;
        }
        else if (_look++ % 2 == 0)
        {
            _held = GameWindow.AnyKeyDown();
        }

        bool menu = _detector.Update(DateTime.UtcNow, inGame, cursor, _held, !AllOverlaysLocked(_settings));
        if (!menu)
        {
        }

        Hidden = menu;
    }
}
