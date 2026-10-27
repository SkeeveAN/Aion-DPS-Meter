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
    private DateTime? _lastClick;

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

    public static bool AllOverlaysLocked(MeterSettings s) => UnlockedOverlays(s).Count == 0;

    /// <summary>Localization keys of the names of the overlays that are unlocked (being positioned), which keeps the menus from hiding anything.</summary>
    public static List<string> UnlockedOverlays(MeterSettings s)
    {
        var open = new List<string>();
        void Check(bool locked, string key)
        {
            if (!locked)
            {
                open.Add(key);
            }
        }

        Check(s.PetFarmLocked, "Settings.PetMap.CardMap");
        Check(s.PetListLocked ?? true, "Settings.PetMap.CardList");
        Check(s.PetInfoLocked, "Settings.PetInfo.Title");
        Check(s.TimetableLocked, "Settings.Hotkey.Timetable");
        Check(s.BossOverlayLocked, "Settings.Menu.Name.Boss");
        Check(s.NetworkOverlayLocked, "Settings.Menu.Name.Network");
        Check(s.CombatOverlayLocked, "Combat.Element.TargetOfTarget");
        Check(s.CombatCastOverlayLocked ?? true, "Settings.Combat.CardBossCast");
        Check(s.CombatHpOverlayLocked ?? true, "Settings.Combat.CardBossHp");
        Check(s.HotkeyOverlayLocked ?? true, "Settings.HotkeyOverlay.Title");
        Check(s.AssistOverlayLocked, "Settings.Menu.Name.Assist");
        Check(s.LootOverlayLocked, "Settings.Menu.Name.Loot");
        Check(s.CraftOverlayLocked, "Settings.Menu.Name.Craft");
        return open;
    }

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

        if (inGame && GameWindow.MouseButtonDown())
        {
            _lastClick = DateTime.UtcNow;
        }

        bool menu = _detector.Update(DateTime.UtcNow, inGame, cursor, _held, !AllOverlaysLocked(_settings), _lastClick);
        if (!menu)
        {
        }

        Hidden = menu;
    }
}
