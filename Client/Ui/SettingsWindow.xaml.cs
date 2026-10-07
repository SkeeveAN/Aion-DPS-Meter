using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

public partial class SettingsWindow : Window
{
    private readonly MeterSettings _settings;

    public SettingsWindow(MeterSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        // Same saved-size pattern as MainWindow's own RestoreWindowGeometry/SaveWindowGeometry.
        // Position stays CenterOwner (see the XAML), not restored here.
        if (settings.SettingsWindowWidth is double width && settings.SettingsWindowHeight is double height)
        {
            Width = width;
            Height = height;
        }

        // Same GDI decode as MainWindow's own AppIconImage - see its remarks on why a plain
        // pack://siteoforigin Source doesn't render this specific .ico at all.
        try
        {
            using var appIcon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "assets", "app", "aiondps.ico"));
            AppIconImage.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
        }

        CheckForUpdatesBox.IsChecked = settings.CheckForUpdates;
        AutoUploadProfileBox.IsChecked = settings.AutoUploadProfile;
        AutoUploadBossBox.IsChecked = settings.AutoUploadBoss;
        SetHotkeyBox(HotkeyHideUiBox, HotkeyBinding.Parse(settings.HotkeyHideUi).ToString());
        SetHotkeyBox(HotkeyPauseBox, HotkeyBinding.Parse(settings.HotkeyPause).ToString());
        SetHotkeyBox(HotkeyCopyDamageBox, HotkeyBinding.Parse(settings.HotkeyCopyDamage).ToString());
        SetHotkeyBox(HotkeyClearBox, HotkeyBinding.Parse(settings.HotkeyClear).ToString());
        SetHotkeyBox(HotkeyUploadBossBox, HotkeyBinding.Parse(settings.HotkeyUploadBoss).ToString());
        SetHotkeyBox(HotkeyModeBox, HotkeyBinding.Parse(settings.HotkeyMode).ToString());
        ShowBossHpBox.IsChecked = settings.ShowBossHp;
        ShowTimetableBox.IsChecked = settings.ShowTimetable;
        SetHotkeyBox(HotkeyTimetableBox, HotkeyBinding.Parse(settings.HotkeyTimetable).ToString());
        AutoResetBox.IsChecked = settings.AutoResetEnabled;
        AutoResetSecondsBox.Text = Math.Clamp(settings.AutoResetSeconds, 1, 600).ToString();
        SelectComboItem(ThemeBox, settings.Theme);
        SelectComboItem(FontSizeBox, settings.FontSize);
        SelectComboItem(OverlayStyleBox, settings.OverlayStyle);
        // The nearest of the offered steps (a hand-edited file may hold any number).
        var nearest = OverlayOpacityBox.Items.OfType<ComboBoxItem>()
            .OrderBy(i => Math.Abs(double.Parse((string)i.Tag, System.Globalization.CultureInfo.InvariantCulture) - settings.OverlayOpacity)).First();
        OverlayOpacityBox.SelectedItem = nearest;
        double timetableOpacity = settings.TimetableOpacity ?? settings.OverlayOpacity;
        TimetableOpacityBox.SelectedItem = TimetableOpacityBox.Items.OfType<ComboBoxItem>()
            .OrderBy(i => Math.Abs(double.Parse((string)i.Tag, System.Globalization.CultureInfo.InvariantCulture) - timetableOpacity)).First();
        AlwaysOnTopBox.IsChecked = settings.AlwaysOnTopOnStartup;
        // Reflects the REAL registry state, not the last value this dialog wrote - see
        // OnStartWithWindowsChanged's own remarks.
        StartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled();
        ShowShareBarsBox.IsChecked = settings.ShowShareBars;
        ShowDamageTakenBox.IsChecked = settings.ShowDamageTaken;
        RecordFightHistoryBox.IsChecked = settings.RecordFightHistory;
        PopulateCaptureAdapters(settings.CaptureAdapterId);

        // Built from LocalizationManager.SupportedLanguages rather than hardcoded in XAML. Each item's
        // Content is the language's OWN native name - a real language picker never translates its
        // own entries.
        foreach (var (code, nativeName) in LocalizationManager.SupportedLanguages)
        {
            LanguageBox.Items.Add(new ComboBoxItem { Content = nativeName, Tag = code });
        }

        SelectComboItem(LanguageBox, LocalizationManager.Instance.Language);
    }

    /// <summary>Automatic (the adapter Windows routes internet traffic through), all adapters, then
    /// every live adapter with the recommended one starred - a gaming VPN shows up as its own entry.</summary>
    private void PopulateCaptureAdapters(string? saved)
    {
        var adapters = AionDPS.Aion2.Capture.CaptureAdapters.List();
        string auto = AionDPS.Aion2.Capture.CaptureAdapters.Recommended(adapters) is { } rec ? $"Automatic ({rec.Name}, {rec.Ipv4})" : "Automatic";
        CaptureAdapterBox.Items.Add(new ComboBoxItem { Content = auto, Tag = "" });
        CaptureAdapterBox.Items.Add(new ComboBoxItem { Content = "All adapters", Tag = AionDPS.Aion2.Capture.CaptureAdapters.AllAdapters });
        foreach (var adapter in adapters)
        {
            CaptureAdapterBox.Items.Add(new ComboBoxItem { Content = adapter.Label, Tag = adapter.Id, ToolTip = adapter.Description });
        }

        CaptureAdapterBox.SelectedItem = CaptureAdapterBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == (saved ?? "")) ?? CaptureAdapterBox.Items[0];
    }

    /// <summary>Matches by Tag, not Content: Content is now a {local:Loc ...} binding (so it reads
    /// as "Dunkel"/"Ciemny"/... depending on the current GUI language), while Tag stays the fixed,
    /// language-independent value ("Dark") that MeterSettings actually stores -- see
    /// ThemeBox's/FontSizeBox's XAML.</summary>
    private void OnDigitsOnly(object sender, System.Windows.Input.TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private static void SelectComboItem(ComboBox box, string tag)
    {
        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = cbi;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    /// <summary>Live preview, not just a save-time value: picking a language repaints every open
    /// window's {local:Loc ...} bindings immediately (see LocalizationManager.Language's remarks),
    /// so the effect of the choice is visible before Save/Cancel is even clicked -- unlike
    /// Theme/FontSize just below, which only apply once Save is pressed. Persisted to
    /// MeterSettings only in OnSaveClicked; clicking Cancel after changing the language leaves
    /// LocalizationManager changed for the rest of this run (nothing reverts it), but does not
    /// write a new default for the next launch.</summary>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is ComboBoxItem { Tag: string code })
        {
            LocalizationManager.Instance.Language = code;
        }
    }

    /// <summary>Not deferred to Save, unlike every checkbox around it -- writes the HKCU Run-key
    /// entry right away (StartupRegistration.cs), so the effect matches what the checkbox shows
    /// even if the user then clicks Cancel. Checked/Unchecked fires AFTER IsChecked has already
    /// flipped, so this reads the new state directly.</summary>
    private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e)
    {
        bool enable = StartWithWindowsBox.IsChecked == true;
        try
        {
            StartupRegistration.SetEnabled(enable);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            StartWithWindowsBox.IsChecked = !enable;
            ThemedMessageBox.Show(this, $"Could not update the Windows startup setting.\n\n{ex.Message}",
                "Start with Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Clicking into a hotkey field puts it into capture mode: a small popup under the field asks for
    // the combination, and the next one pressed becomes the hotkey and is shown in the field. Esc
    // cancels, as does leaving the field. The field's Tag holds the stored text ("Ctrl+Alt+H").
    private TextBox? _capturingBox;
    private System.Windows.Controls.Primitives.Popup? _hotkeyPopup;
    private TextBlock? _hotkeyPopupText;

    private TextBox HotkeyBoxOf(object sender) => (TextBox)FindName((string)((FrameworkElement)sender).Tag);

    private static void SetHotkeyBox(TextBox box, string value)
    {
        box.Tag = value;
        box.Text = value;
    }

    private void ShowHotkeyPopup(TextBox box, string message)
    {
        if (_hotkeyPopup is null)
        {
            _hotkeyPopupText = new TextBlock { Margin = new Thickness(10, 5, 10, 5) };
            _hotkeyPopupText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var border = new Border { BorderThickness = new Thickness(1), Child = _hotkeyPopupText };
            border.SetResourceReference(Border.BackgroundProperty, "Brush.TitleBar");
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            _hotkeyPopup = new System.Windows.Controls.Primitives.Popup
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = true,
                AllowsTransparency = true,
                Focusable = false,
                IsHitTestVisible = false,
                Child = border,
            };
        }

        _hotkeyPopupText!.Text = message;
        _hotkeyPopup.PlacementTarget = box;
        _hotkeyPopup.IsOpen = true;
    }

    private void StopCapture(bool restore)
    {
        if (_hotkeyPopup is not null)
        {
            _hotkeyPopup.IsOpen = false;
        }

        if (_capturingBox is { } box && restore)
        {
            box.Text = (string?)box.Tag ?? "";
        }

        _capturingBox = null;
    }

    private void OnHotkeyBoxGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        StopCapture(restore: true);
        _capturingBox = (TextBox)sender;
        ShowHotkeyPopup(_capturingBox, LocalizationManager.Instance["Settings.Hotkey.Press"]);
    }

    private void OnHotkeyBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(_capturingBox, sender))
        {
            StopCapture(restore: true);
        }
    }

    private void OnHotkeyClearClicked(object sender, RoutedEventArgs e)
    {
        StopCapture(restore: true);
        SetHotkeyBox(HotkeyBoxOf(sender), "");
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturingBox is not { } box)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        Key key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        if (key == Key.Escape)
        {
            StopCapture(restore: true);
            Focus();
            return;
        }

        // A bare modifier is only half a combination: keep waiting for the key itself.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed)
        {
            return;
        }

        ModifierKeys mods = Keyboard.Modifiers;
        var binding = new HotkeyBinding(mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt),
            mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Windows), key);
        if (!(binding.Ctrl || binding.Alt || binding.Shift || binding.Win))
        {
            // A global hotkey without Ctrl, Alt, Shift or Win would swallow that key in every program.
            ShowHotkeyPopup(box, LocalizationManager.Instance["Settings.Hotkey.NeedsModifier"]);
            return;
        }

        SetHotkeyBox(box, binding.ToString());
        StopCapture(restore: false);
        Focus();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        _settings.CheckForUpdates = CheckForUpdatesBox.IsChecked ?? true;
        _settings.AutoUploadProfile = AutoUploadProfileBox.IsChecked ?? false;
        _settings.HotkeyHideUi = (string?)HotkeyHideUiBox.Tag ?? "";
        _settings.HotkeyPause = (string?)HotkeyPauseBox.Tag ?? "";
        _settings.HotkeyCopyDamage = (string?)HotkeyCopyDamageBox.Tag ?? "";
        _settings.HotkeyClear = (string?)HotkeyClearBox.Tag ?? "";
        _settings.HotkeyUploadBoss = (string?)HotkeyUploadBossBox.Tag ?? "";
        _settings.HotkeyMode = (string?)HotkeyModeBox.Tag ?? "";
        _settings.ShowBossHp = ShowBossHpBox.IsChecked ?? false;
        _settings.ShowTimetable = ShowTimetableBox.IsChecked ?? true;
        _settings.HotkeyTimetable = (string?)HotkeyTimetableBox.Tag ?? "";
        _settings.AutoResetEnabled = AutoResetBox.IsChecked ?? false;
        _settings.AutoResetSeconds = int.TryParse(AutoResetSecondsBox.Text, out int seconds) ? Math.Clamp(seconds, 1, 600) : 10;
        _settings.AutoUploadBoss = AutoUploadBossBox.IsChecked ?? false;
        _settings.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.Theme;
        _settings.FontSize = (FontSizeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.FontSize;
        _settings.OverlayStyle = (OverlayStyleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.OverlayStyle;
        _settings.OverlayOpacity = double.Parse((string)((ComboBoxItem)OverlayOpacityBox.SelectedItem).Tag, System.Globalization.CultureInfo.InvariantCulture);
        _settings.TimetableOpacity = double.Parse((string)((ComboBoxItem)TimetableOpacityBox.SelectedItem).Tag, System.Globalization.CultureInfo.InvariantCulture);
        _settings.Language = LocalizationManager.Instance.Language;
        _settings.AlwaysOnTopOnStartup = AlwaysOnTopBox.IsChecked ?? false;
        _settings.ShowShareBars = ShowShareBarsBox.IsChecked ?? true;
        _settings.ShowDamageTaken = ShowDamageTakenBox.IsChecked ?? false;
        _settings.RecordFightHistory = RecordFightHistoryBox.IsChecked ?? true;
        _settings.CaptureAdapterId = (CaptureAdapterBox.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } adapterTag ? adapterTag : null;

        Saved?.Invoke();
        Close();
    }

    /// <summary>
    /// Fired on Save, right before Close() -- replaces the old ShowDialog()/DialogResult flow, per
    /// the user's request that Settings open as an independent second window instead of a modal
    /// blocking MainWindow (Show(), not ShowDialog(), from MainWindow.OnSettingsClicked). Setting
    /// DialogResult only works for a window actually shown via ShowDialog() -- doing it here would
    /// throw at runtime the moment Show() is used instead, hence this event instead.
    /// </summary>
    public event Action? Saved;

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Saves size regardless of Save/Cancel/titlebar-✕/Alt-F4 - all of them end up here,
    /// same reasoning as MainWindow's own OnClosing/SaveWindowGeometry. Deliberately a FRESH
    /// MeterSettings.Load() rather than writing through _settings (this dialog's own working copy,
    /// which Cancel must NOT persist to disk) - only the size field changes, exactly like
    /// MainWindow's own geometry save stays decoupled from whatever else might be in flight.
    /// RestoreBounds (not Width/Height directly) so closing while minimized/maximized doesn't
    /// persist that transient state as if it were the normal size - CanResizeWithGrip allows
    /// maximizing too, same as MainWindow's own.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        MeterSettings onDisk = MeterSettings.Load();
        onDisk.SettingsWindowWidth = bounds.Width;
        onDisk.SettingsWindowHeight = bounds.Height;
        onDisk.Save();
        base.OnClosing(e);
    }
}
