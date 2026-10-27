namespace AionDPS.Ui;

/// <summary>How the game is controlled: with the mouse cursor hidden while playing (it shows only when a menu opens), or with the cursor always shown.</summary>
public enum ControlMode
{
    Unknown,
    Hidden,
    Classic,
}

/// <summary>
/// Tells a game menu from play by the mouse cursor (in Aion 2 only Esc and the menus show it; Alt shows it without a menu): in the control mode that hides the cursor while playing, the game shows it when a menu opens (map,
/// inventory, legion, shop, Esc ...).
/// Pure: the time and what the system says are handed in, so a test can walk through a menu.
/// </summary>
public sealed class MenuDetector
{
    /// <summary>The cursor must have been shown this long, with nothing held, before it counts as a menu (a key press that shows it for a blink is no menu).</summary>
    public static readonly TimeSpan MenuAfter = TimeSpan.FromMilliseconds(250);

    /// <summary>A key held longer than this is taken as stuck (or a game that always shows the cursor) and ignored.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(20);

    /// <summary>After the cursor hides, the menu state stays this long (the menu is closing; no flicker).</summary>
    public static readonly TimeSpan Linger = TimeSpan.FromMilliseconds(300);

    /// <summary>A click in a menu makes the game swap the cursor for a moment (the glow at its tip), which can read as "hidden": after a click the menu state stays this long.</summary>
    public static readonly TimeSpan ClickLinger = TimeSpan.FromMilliseconds(1500);

    private DateTime? _shownSince;
    private DateTime? _heldSince;
    private DateTime? _hiddenSince;

    public bool MenuOpen { get; private set; }

    public void Reset()
    {
        _shownSince = _heldSince = _hiddenSince = null;
        MenuOpen = false;
    }

    /// <summary>One look. <paramref name="suppressed"/>: the meter's own windows are being used (an unlocked overlay is placed), which must never hide.</summary>
    public bool Update(DateTime now, bool gameInFront, bool cursorShown, bool keyHeld, bool suppressed, DateTime? lastClick = null)
    {
        if (!gameInFront || suppressed)
        {
            Reset();
            return false;
        }

        if (!cursorShown)
        {
            _shownSince = _heldSince = null;
            if (MenuOpen)
            {
                _hiddenSince ??= now;
                var linger = lastClick is DateTime click && now - click < ClickLinger ? ClickLinger : Linger;
                if (now - _hiddenSince >= linger)
                {
                    MenuOpen = false;
                    _hiddenSince = null;
                }
            }

            return MenuOpen;
        }

        _hiddenSince = null;
        _shownSince ??= now;
        if (keyHeld)
        {
            _heldSince ??= now;
        }
        else
        {
            _heldSince = null;
        }

        // Alt shows the cursor without a menu; it is held before the cursor shows. A key pressed inside a menu already open (a hotkey, Shift ...) is no peek
        bool peek = !MenuOpen && keyHeld && now - _heldSince < StuckAfter;
        if (peek)
        {
            MenuOpen = false;
            _shownSince = now; // the menu clock starts when the key is let go
        }
        else if (now - _shownSince >= MenuAfter)
        {
            MenuOpen = true;
        }

        return MenuOpen;
    }
}

/// <summary>
/// Works out which control mode the player uses, from what is seen while they fight: with the cursor hidden for about fifteen seconds of dealing damage it is the
/// mode this feature works in; with it shown for about twelve it is the classic one (where menus cannot be told apart, so the feature switches itself off).
/// The counts decay when the other kind of evidence turns up.
/// </summary>
public sealed class ControlModeLearner
{
    public const double ClassicAfterSeconds = 12, HiddenAfterSeconds = 15;

    private double _shown, _hidden;
    private DateTime? _last;

    public ControlMode Mode { get; private set; } = ControlMode.Unknown;

    public ControlMode Note(DateTime now, bool gameInFront, bool dealingDamage, bool cursorShown)
    {
        double dt = _last is DateTime last ? Math.Clamp((now - last).TotalSeconds, 0, 0.5) : 0;
        _last = now;
        if (!gameInFront || !dealingDamage)
        {
            return Mode;
        }

        if (cursorShown)
        {
            _shown += dt;
            _hidden = Math.Max(0, _hidden - dt);
        }
        else
        {
            _hidden += dt;
            _shown = Math.Max(0, _shown - dt);
        }

        if (_shown >= ClassicAfterSeconds)
        {
            Mode = ControlMode.Classic;
        }
        else if (_hidden >= HiddenAfterSeconds)
        {
            Mode = ControlMode.Hidden;
        }

        return Mode;
    }
}
