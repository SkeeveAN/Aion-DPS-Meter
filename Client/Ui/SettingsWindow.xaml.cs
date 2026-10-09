using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Schedule;

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
        UploadOtherProfilesBox.IsChecked = settings.UploadOtherPlayersProfiles;
        SetHotkeyBox(HotkeyHideUiBox, HotkeyBinding.Parse(settings.HotkeyHideUi).ToString());
        SetHotkeyBox(HotkeyPauseBox, HotkeyBinding.Parse(settings.HotkeyPause).ToString());
        SetHotkeyBox(HotkeyCopyDamageBox, HotkeyBinding.Parse(settings.HotkeyCopyDamage).ToString());
        SetHotkeyBox(HotkeyClearBox, HotkeyBinding.Parse(settings.HotkeyClear).ToString());
        SetHotkeyBox(HotkeyUploadBossBox, HotkeyBinding.Parse(settings.HotkeyUploadBoss).ToString());
        SetHotkeyBox(HotkeyModeBox, HotkeyBinding.Parse(settings.HotkeyMode).ToString());
        ShowBossHpBox.IsChecked = settings.ShowBossHp;
        ShowTimetableBox.IsChecked = settings.ShowTimetable;
        ShowPetFarmBox.IsChecked = settings.ShowPetFarm;
        PetFarmLockedBox.IsChecked = settings.PetFarmLocked;
        InitPetMap(settings);
        SetHotkeyBox(HotkeyTimetableBox, HotkeyBinding.Parse(settings.HotkeyTimetable).ToString());
        SetHotkeyBox(HotkeyPetMapBox, HotkeyBinding.Parse(settings.HotkeyPetMap).ToString());
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
        TimetableLookaheadSlider.Value = Math.Clamp(settings.TimetableLookaheadMinutes, 0, 60);
        InitTimetableEvents(settings);
        ShowTimetableLookahead(); // the handler does not fire when the value equals the slider's default
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

    /// <summary>Shows the lookahead slider's value with the language's minute unit.</summary>
    private Dictionary<string, TimetableEventSetting> _timetableEvents = new();

    private void OnTimetableSoundMinutesChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ShowTimetableSoundMinutes();

    private readonly List<(ComboBox Sound, Slider Volume, Button Test, Func<bool> HasSound)> _soundControls = new();

    /// <summary>The master switch is off: the minutes, the sound choices, the volumes and the play buttons are greyed out (the choices stay).</summary>
    private void OnTimetableNotifyChanged(object? sender, RoutedEventArgs? e)
    {
        if (TimetableMinutesRow is null)
        {
            return;
        }

        bool on = TimetableNotifyBox.IsChecked == true;
        TimetableMinutesRow.IsEnabled = on;
        TimetableMinutesRow.Opacity = on ? 1.0 : 0.4;
        foreach (var (sound, volume, test, hasSound) in _soundControls)
        {
            foreach (UIElement control in new UIElement[] { sound, volume })
            {
                control.IsEnabled = on;
                control.Opacity = on ? 1.0 : 0.4;
            }

            test.IsEnabled = on && hasSound();
            test.Opacity = test.IsEnabled ? 1.0 : 0.4;
        }
    }

    private void ShowTimetableSoundMinutes()
    {
        if (TimetableSoundValue is not null)
        {
            TimetableSoundValue.Text = string.Format(LocalizationManager.Instance["Settings.Timetable.LookaheadValue"], (int)TimetableSoundSlider.Value);
        }
    }

    /// <summary>
    /// The list of the timetable's events (one row per kind of event, not per time): a switch for the overlay, the name, the reminder sound with its
    /// volume, and a button to try both. Events that can be joined at any time (the arenas) have no start, so no sound.
    /// </summary>
    private void InitTimetableEvents(MeterSettings settings)
    {
        TimetableSoundSlider.Value = Math.Clamp(settings.TimetableSoundMinutes, 0, 60);
        ShowTimetableSoundMinutes();
        TimetableNotifyBox.IsChecked = settings.TimetableNotify;
        _timetableEvents = settings.TimetableEvents.ToDictionary(kv => kv.Key, kv => new TimetableEventSetting { Show = kv.Value.Show, Sound = kv.Value.Sound, Volume = kv.Value.Volume });
        var loc = LocalizationManager.Instance;
        TimetableEventRows.Children.Clear();
        _soundControls.Clear();
        foreach (var scheduled in EventSchedule.Events.OrderBy(e => e.Always)) // events without a start (the arenas) go last
        {
            string id = scheduled.Id;
            if (!_timetableEvents.TryGetValue(id, out var state))
            {
                _timetableEvents[id] = state = new TimetableEventSetting();
            }

            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var show = new CheckBox { IsChecked = state.Show, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = loc["Settings.Timetable.ShowEvent"] };
            show.Checked += (_, _) => state.Show = true;
            show.Unchecked += (_, _) => state.Show = false;
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            name.Inlines.Add(new Run(scheduled.NameIn(loc.Language)));
            if (scheduled.Players is { Length: > 0 } players)
            {
                name.Inlines.Add(new LineBreak());
                var count = new Run(players) { FontSize = 11 };
                count.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextMuted");
                name.Inlines.Add(count);
            }
            Grid.SetColumn(name, 1);

            var sound = new ComboBox { Margin = new Thickness(0, 0, 8, 0), Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
            sound.Items.Add(new ComboBoxItem { Content = loc["Settings.Timetable.NoSound"], Tag = "" });
            foreach (string id2 in NotifySounds.All)
            {
                sound.Items.Add(new ComboBoxItem { Content = loc["Sound." + id2], Tag = id2 });
            }

            sound.SelectedItem = sound.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == state.Sound) ?? sound.Items[0];
            Grid.SetColumn(sound, 2);

            var volume = new Slider { Minimum = 0, Maximum = 100, Value = Math.Clamp(state.Volume, 0, 100), TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = loc["Settings.Timetable.Volume"] };
            volume.SetResourceReference(FrameworkElement.StyleProperty, "ThemedSlider");
            Grid.SetColumn(volume, 3);

            var test = new Button { Content = "▶", Width = 28, Height = 26, Padding = new Thickness(0), ToolTip = loc["Settings.Timetable.Test"] };
            Grid.SetColumn(test, 4);

            // with "no sound" there is nothing to try: the play button is greyed out
            void ShowTestState()
            {
                test.IsEnabled = state.Sound.Length > 0 && TimetableNotifyBox.IsChecked == true;
                test.Opacity = test.IsEnabled ? 1.0 : 0.4;
            }

            sound.SelectionChanged += (_, _) =>
            {
                state.Sound = (sound.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                ShowTestState();
            };
            volume.ValueChanged += (_, _) => state.Volume = (int)volume.Value;
            test.Click += (_, _) => NotifySounds.Play(state.Sound, state.Volume);
            ShowTestState();

            row.Children.Add(show);
            row.Children.Add(name);
            if (scheduled.Always)
            {
                // no start time: nothing to remind of, so no sound, volume or play button, just the reason
                var why = new TextBlock { Text = loc["Settings.Timetable.AlwaysTip"], FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                why.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                Grid.SetColumn(why, 2);
                Grid.SetColumnSpan(why, 2); // the width of the sound and volume columns, the text wraps inside it
                row.Children.Add(why);
            }
            else
            {
                row.Children.Add(sound);
                row.Children.Add(volume);
                row.Children.Add(test);
                _soundControls.Add((sound, volume, test, () => state.Sound.Length > 0));
            }

            TimetableEventRows.Children.Add(row);
        }

        OnTimetableNotifyChanged(null, null);
    }

    private void OnTimetableLookaheadChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ShowTimetableLookahead();

    private void ShowTimetableLookahead()
    {
        // The slider raises ValueChanged while the XAML is still being read, before the value label exists.
        if (TimetableLookaheadValue is null)
        {
            return;
        }

        TimetableLookaheadValue.Text = string.Format(LocalizationManager.Instance["Settings.Timetable.LookaheadValue"], (int)TimetableLookaheadSlider.Value);
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
        // The overlays save where they were put straight to disk while this window is open; this working copy is older and
        // would write the old places back.
        MeterSettings onDisk = MeterSettings.Load();
        _settings.PetFarmLeft = onDisk.PetFarmLeft;
        _settings.PetFarmTop = onDisk.PetFarmTop;
        _settings.PetFarmScale = onDisk.PetFarmScale;
        _settings.PetFarmWidth = onDisk.PetFarmWidth;
        _settings.PetFarmHeight = onDisk.PetFarmHeight;
        _settings.PetMapWidth = onDisk.PetMapWidth;
        _settings.PetMapHeight = onDisk.PetMapHeight;
        _settings.PetListWidth = onDisk.PetListWidth;
        _settings.PetListHeight = onDisk.PetListHeight;
        _settings.PetMapLeft = onDisk.PetMapLeft;
        _settings.PetMapTop = onDisk.PetMapTop;
        _settings.PetMapScale = onDisk.PetMapScale;
        _settings.PetListLeft = onDisk.PetListLeft;
        _settings.PetListTop = onDisk.PetListTop;
        _settings.PetListScale = onDisk.PetListScale;

        _settings.CheckForUpdates = CheckForUpdatesBox.IsChecked ?? true;
        _settings.AutoUploadProfile = AutoUploadProfileBox.IsChecked ?? false;
        _settings.UploadOtherPlayersProfiles = UploadOtherProfilesBox.IsChecked ?? false;
        _settings.HotkeyHideUi = (string?)HotkeyHideUiBox.Tag ?? "";
        _settings.HotkeyPause = (string?)HotkeyPauseBox.Tag ?? "";
        _settings.HotkeyCopyDamage = (string?)HotkeyCopyDamageBox.Tag ?? "";
        _settings.HotkeyClear = (string?)HotkeyClearBox.Tag ?? "";
        _settings.HotkeyUploadBoss = (string?)HotkeyUploadBossBox.Tag ?? "";
        _settings.HotkeyMode = (string?)HotkeyModeBox.Tag ?? "";
        _settings.ShowBossHp = ShowBossHpBox.IsChecked ?? false;
        _settings.ShowTimetable = ShowTimetableBox.IsChecked ?? true;
        _settings.ShowPetFarm = ShowPetFarmBox.IsChecked ?? false;
        _settings.PetFarmLocked = PetFarmLockedBox.IsChecked ?? true;
        _settings.ShowPetMap = ShowPetMapBox.IsChecked ?? false;
        _settings.PetMapRadius = (int)PetMapRadiusSlider.Value;
        _settings.PetMapOpacity = PetMapOpacitySlider.Value / 100.0;
        _settings.PetMapPets = _petPicked.OrderBy(i => i).ToList();
        _settings.PetMapGatherItems = _gatherPicked.OrderBy(k => k).ToList();
        _settings.HotkeyTimetable = (string?)HotkeyTimetableBox.Tag ?? "";
        _settings.HotkeyPetMap = (string?)HotkeyPetMapBox.Tag ?? "";
        _settings.AutoResetEnabled = AutoResetBox.IsChecked ?? false;
        _settings.AutoResetSeconds = int.TryParse(AutoResetSecondsBox.Text, out int seconds) ? Math.Clamp(seconds, 1, 600) : 10;
        _settings.AutoUploadBoss = AutoUploadBossBox.IsChecked ?? false;
        _settings.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.Theme;
        _settings.FontSize = (FontSizeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.FontSize;
        _settings.OverlayStyle = (OverlayStyleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.OverlayStyle;
        _settings.OverlayOpacity = double.Parse((string)((ComboBoxItem)OverlayOpacityBox.SelectedItem).Tag, System.Globalization.CultureInfo.InvariantCulture);
        _settings.TimetableOpacity = double.Parse((string)((ComboBoxItem)TimetableOpacityBox.SelectedItem).Tag, System.Globalization.CultureInfo.InvariantCulture);
        _settings.TimetableLookaheadMinutes = (int)TimetableLookaheadSlider.Value;
        _settings.TimetableNotify = TimetableNotifyBox.IsChecked ?? true;
        _settings.TimetableSoundMinutes = (int)TimetableSoundSlider.Value;
        _settings.TimetableEvents = _timetableEvents.ToDictionary(kv => kv.Key, kv => kv.Value);
        _settings.Language = LocalizationManager.Instance.Language;
        _settings.AlwaysOnTopOnStartup = AlwaysOnTopBox.IsChecked ?? false;
        _settings.ShowShareBars = ShowShareBarsBox.IsChecked ?? true;
        _settings.ShowDamageTaken = ShowDamageTakenBox.IsChecked ?? false;
        _settings.RecordFightHistory = RecordFightHistoryBox.IsChecked ?? true;
        _settings.CaptureAdapterId = (CaptureAdapterBox.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } adapterTag ? adapterTag : null;

        Saved?.Invoke(); // the window stays open: Save applies, the ✕ / Cancel button closes
    }

    /// <summary>
    /// Fired on Save (the window stays open) -- replaces the old ShowDialog()/DialogResult flow, per
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

    // ---- Pet-Karte page: which pets the map shows (species list on the left, the pets of the chosen species with switches on the right) ----
    private string _petSpecies = Aion2Pets.Species[0];
    private HashSet<int> _petPicked = new();
    private Dictionary<int, int> _petLevels = new();

    private void InitPetMap(MeterSettings settings)
    {
        _petPicked = settings.PetMapPets.ToHashSet();
        _gatherPicked = settings.PetMapGatherItems.ToHashSet();
        BuildGatherPage();
        // the levels of the own pets (login frame, kept in the character file): pets at the top level need no farming and are greyed out
        _petLevels = Aion2CharacterStore.Load(Aion2CharacterStore.DefaultPath)?.Pets.ToDictionary(p => p.Id, p => p.Level) ?? new();
        ShowPetMapBox.IsChecked = settings.ShowPetMap;
        PetMapRadiusSlider.Value = Math.Clamp(settings.PetMapRadius, 50, 500);
        PetMapOpacitySlider.Value = Math.Clamp(settings.PetMapOpacity * 100, 20, 100);
        ShowPetMapSliderValues();
        OnPetMapShowChanged(null!, null!);
        BuildPetSpecies();
        BuildPetRows();
    }

    /// <summary>A hotkey flipped the pet map or the timetable while this window is open: the switches follow.</summary>
    public void SyncOverlaySwitches(bool showPetMap, bool showTimetable)
    {
        ShowPetMapBox.IsChecked = showPetMap;
        ShowTimetableBox.IsChecked = showTimetable;
    }

    /// <summary>The map's sliders are read-only (greyed out) while the map is switched off; the pet list stays editable.</summary>
    private void OnPetMapShowChanged(object? sender, RoutedEventArgs? e)
    {
        if (PetMapSliders is null)
        {
            return;
        }

        bool on = ShowPetMapBox.IsChecked == true;
        PetMapSliders.IsEnabled = on;
        PetMapSliders.Opacity = on ? 1.0 : 0.4;
    }

    private void OnPetMapSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ShowPetMapSliderValues();

    private void ShowPetMapSliderValues()
    {
        if (PetMapRadiusValue is null || PetMapOpacityValue is null)
        {
            return;
        }

        PetMapRadiusValue.Text = string.Format(LocalizationManager.Instance["Settings.PetMap.RadiusValue"], (int)PetMapRadiusSlider.Value);
        PetMapOpacityValue.Text = $"{(int)PetMapOpacitySlider.Value} %";
    }

    private HashSet<string> _gatherPicked = new();

    /// <summary>
    /// The Sammeln page: one line per kind (its name and "all / none" on the left), the collectibles as switchable pills on the right. Only what the
    /// player's own faction can use is listed, and only what the world maps hold a place for.
    /// </summary>
    private void BuildGatherPage()
    {
        GatherCards.Children.Clear();
        var loc = LocalizationManager.Instance;
        string? faction = Aion2Gather.OwnFaction();
        GatherFactionNote.Text = faction is null ? loc["Settings.Gather.FactionUnknown"] : "";
        GatherFactionNote.Visibility = faction is null ? Visibility.Visible : Visibility.Collapsed;
        var card = new Border { Padding = new Thickness(12, 4, 12, 4) };
        card.SetResourceReference(FrameworkElement.StyleProperty, "SettingsCard");
        var lines = new StackPanel();
        foreach (string kind in Aion2Gather.Kinds)
        {
            var items = Aion2Gather.Items().Where(i => i.Kind == kind && i.Count > 0 && i.IsFor(faction)).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            var line = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new Point(6, 0), new Point(12, 7), new Point(6, 14), new Point(0, 7) },
                Fill = new SolidColorBrush(PetMapPalette.OfGather(kind)), Stroke = Brushes.Black, StrokeThickness = 1,
                Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Width = 12, Height = 14,
            });
            var heading = new TextBlock { Text = loc["Settings.PetMap.Gather." + kind], FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 92 };
            title.Children.Add(heading);
            var count = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 0) };
            count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            var all = new Button { Padding = new Thickness(0), Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 11, Cursor = Cursors.Hand };
            all.SetResourceReference(FrameworkElement.StyleProperty, "GatherLink");
            var left = new StackPanel();
            left.Children.Add(title);
            left.Children.Add(count);
            left.Children.Add(all);

            var wrap = new WrapPanel();
            Grid.SetColumn(wrap, 1);
            var pills = new List<(System.Windows.Controls.Primitives.ToggleButton Pill, string Key)>();
            void Refresh()
            {
                int on = pills.Count(p => _gatherPicked.Contains(p.Key));
                count.Text = $"{on} / {pills.Count}";
                all.Content = on < pills.Count ? loc["Settings.PetMap.AllOn"] : loc["Settings.PetMap.AllOff"];
            }

            foreach (var item in items)
            {
                string key = item.Key;
                var pill = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Content = item.NameIn(loc.Language), IsChecked = _gatherPicked.Contains(key),
                    ToolTip = string.Format(loc["Settings.Gather.Places"], faction == "Elyos" ? item.Elyos : faction == "Asmodian" ? item.Asmodian : item.Count),
                };
                pill.SetResourceReference(FrameworkElement.StyleProperty, "GatherPill");
                pill.Checked += (_, _) => { _gatherPicked.Add(key); Refresh(); };
                pill.Unchecked += (_, _) => { _gatherPicked.Remove(key); Refresh(); };
                pills.Add((pill, key));
                wrap.Children.Add(pill);
            }

            all.Click += (_, _) =>
            {
                bool turnOn = pills.Any(p => !_gatherPicked.Contains(p.Key));
                foreach (var (pill, _) in pills)
                {
                    pill.IsChecked = turnOn; // fires Checked/Unchecked, which keeps the set and the counter
                }
            };
            Refresh();

            line.Children.Add(left);
            line.Children.Add(wrap);
            if (lines.Children.Count > 0)
            {
                var rule = new Border { Height = 1, BorderThickness = new Thickness(0, 1, 0, 0) };
                rule.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                lines.Children.Add(rule);
            }

            lines.Children.Add(line);
        }

        card.Child = lines;
        GatherCards.Children.Add(card);
    }

    private IEnumerable<(int PetId, string Name)> PetsOfSpecies(string species) => Aion2Pets.MapPetsOf(species, LocalizationManager.Instance.Language);

    private bool IsMax(int petId) => _petLevels.TryGetValue(petId, out int level) && level >= Aion2Pets.TopLevel;

    /// <summary>Counts the pets that are on: those at the top level do not count.</summary>
    private (int On, int All) PetCounts(string? species)
    {
        var pets = (species is null ? Aion2Pets.Species.SelectMany(PetsOfSpecies) : PetsOfSpecies(species)).ToList();
        return (pets.Count(p => _petPicked.Contains(p.PetId) && !IsMax(p.PetId)), pets.Count);
    }

    private void BuildPetSpecies()
    {
        PetSpeciesList.Children.Clear();
        foreach (string species in Aion2Pets.Species.Where(sp => PetsOfSpecies(sp).Any()))
        {
            var (on, all) = PetCounts(species);
            bool selected = species == _petSpecies;
            var name = new TextBlock { Text = species, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Brush.Accent" : "Brush.Text");
            // three numbers: pets on / pets in the species / pets at the top level (blue, "Max")
            int maxCount = PetsOfSpecies(species).Count(p => IsMax(p.PetId));
            var counts = new TextBlock { FontSize = 11 };
            var onRun = new Run(on.ToString());
            onRun.SetResourceReference(TextElement.ForegroundProperty, on > 0 ? "Brush.Accent" : "Brush.TextSubtle");
            var allRun = new Run("/" + all);
            allRun.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextSubtle");
            var maxRun = new Run("/" + maxCount) { Foreground = new SolidColorBrush(Color.FromRgb(0x4D, 0x9B, 0xFF)) };
            counts.Inlines.Add(onRun);
            counts.Inlines.Add(allRun);
            counts.Inlines.Add(maxRun);
            var badge = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(7, 1, 7, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = counts, ToolTip = string.Format(LocalizationManager.Instance["Settings.PetMap.BadgeTip"], on, all, maxCount) };
            badge.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            var row = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(badge, Dock.Right);
            row.Children.Add(badge);
            row.Children.Add(name);
            var item = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand, Child = row,
                BorderThickness = new Thickness(1), Background = Brushes.Transparent };
            if (selected)
            {
                item.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
                item.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            }
            else
            {
                item.BorderBrush = Brushes.Transparent;
            }

            string chosen = species;
            item.MouseLeftButtonDown += (_, _) =>
            {
                _petSpecies = chosen;
                BuildPetSpecies();
                BuildPetRows();
            };
            PetSpeciesList.Children.Add(item);
        }

        var total = PetCounts(null);
        PetTotalText.Text = string.Format(LocalizationManager.Instance["Settings.PetMap.Total"], total.On, total.All);
    }

    private List<(int PetId, string Name)>? _petRowsTest;

    /// <summary>For layout checks (render-settings): lists the pets with the longest names of every species instead of the chosen species.</summary>
    internal void ShowLongestPetNames(int count)
    {
        _petRowsTest = Aion2Pets.Species.SelectMany(PetsOfSpecies).OrderByDescending(p => p.Name.Length).Take(count).ToList();
        foreach (var (id, _) in _petRowsTest.Where((_, i) => i % 2 == 0))
        {
            _petPicked.Add(id);
        }

        BuildPetRows();
    }

    private void BuildPetRows()
    {
        PetRows.Children.Clear();
        string filter = PetSearchBox.Text.Trim();
        // a search looks through every species, otherwise the chosen one is shown
        var pets = (_petRowsTest ?? (filter.Length > 0 ? Aion2Pets.Species.SelectMany(PetsOfSpecies) : PetsOfSpecies(_petSpecies)))
            .Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        foreach ((int petId, string petName) in pets)
        {
            bool max = IsMax(petId);
            var row = new Grid { Margin = new Thickness(0, 2, 18, 2), Opacity = max ? 0.45 : 1.0 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = petName, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) }; // a long name wraps onto a second line instead of being cut off
            name.SetResourceReference(TextBlock.ForegroundProperty, !max && _petPicked.Contains(petId) ? "Brush.Text" : "Brush.TextSubtle");
            var maxText = new TextBlock { Text = max ? LocalizationManager.Instance["Settings.PetMap.Max"] : "", Margin = new Thickness(0, 0, 8, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            maxText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            var toggle = new CheckBox { IsChecked = !max && _petPicked.Contains(petId), IsEnabled = !max, VerticalAlignment = VerticalAlignment.Center };
            int id = petId;
            toggle.Click += (_, _) =>
            {
                if (toggle.IsChecked == true)
                {
                    _petPicked.Add(id);
                }
                else
                {
                    _petPicked.Remove(id);
                }

                name.SetResourceReference(TextBlock.ForegroundProperty, toggle.IsChecked == true ? "Brush.Text" : "Brush.TextSubtle");
                BuildPetSpecies();
            };
            Grid.SetColumn(maxText, 1);
            Grid.SetColumn(toggle, 2);
            row.Children.Add(name);
            row.Children.Add(maxText);
            row.Children.Add(toggle);
            PetRows.Children.Add(row);
        }
    }

    private void OnPetSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (PetRows is not null)
        {
            BuildPetRows();
        }
    }

    private void OnPetAllOn(object sender, RoutedEventArgs e) => SetVisiblePets(true);

    private void OnPetAllOff(object sender, RoutedEventArgs e) => SetVisiblePets(false);

    /// <summary>"All on / all off" works on the pets in the list: the chosen species, or the search result. Pets at the top level are left alone.</summary>
    private void SetVisiblePets(bool on)
    {
        string filter = PetSearchBox.Text.Trim();
        var pets = (filter.Length > 0 ? Aion2Pets.Species.SelectMany(PetsOfSpecies) : PetsOfSpecies(_petSpecies))
            .Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
        foreach ((int petId, _) in pets.Where(p => !IsMax(p.PetId)))
        {
            if (on)
            {
                _petPicked.Add(petId);
            }
            else
            {
                _petPicked.Remove(petId);
            }
        }

        BuildPetSpecies();
        BuildPetRows();
    }
}
