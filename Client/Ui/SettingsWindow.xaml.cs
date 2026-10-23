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

    public SettingsWindow(MeterSettings settings, DateTime? undoUntil = null)
    {
        InitializeComponent();
        _settings = settings;
        foreach (RadioButton nav in new[] { NavGeneral, NavMeter, NavInstall, NavHotkeys, NavUpdates })
        {
            nav.Checked += OnNavChecked;
        }

        ApplyView();
        ShowUndo(undoUntil);

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
        DebugRecordingBox.IsChecked = settings.DebugRecording;
        UploadOtherProfilesBox.IsChecked = settings.UploadOtherPlayersProfiles;
        // switching an upload on needs the privacy policy to be accepted: it is asked again when it was declined (set after the values above, so loading does not ask)
        foreach (CheckBox uploadSwitch in new[] { AutoUploadProfileBox, AutoUploadBossBox, UploadOtherProfilesBox })
        {
            uploadSwitch.Checked += OnUploadSwitchChecked;
        }
        // Only members of Akatsuki on Kaisinel see this switch (a member who switched it on and then left the legion keeps seeing it, to be able to switch it off).
        LoginShareCard.Visibility = settings.LoginShareEligible || settings.ShareLoginWithLegion ? Visibility.Visible : Visibility.Collapsed;
        ShareLoginBox.IsChecked = settings.ShareLoginWithLegion;
        SetHotkeyBox(HotkeyHideUiBox, HotkeyBinding.Parse(settings.HotkeyHideUi).ToString());
        SetHotkeyBox(HotkeyPauseBox, HotkeyBinding.Parse(settings.HotkeyPause).ToString());
        SetHotkeyBox(HotkeyCopyDamageBox, HotkeyBinding.Parse(settings.HotkeyCopyDamage).ToString());
        SetHotkeyBox(HotkeyClearBox, HotkeyBinding.Parse(settings.HotkeyClear).ToString());
        SetHotkeyBox(HotkeyUploadBossBox, HotkeyBinding.Parse(settings.HotkeyUploadBoss).ToString());
        SetHotkeyBox(HotkeyModeBox, HotkeyBinding.Parse(settings.HotkeyMode).ToString());
        ShowBossHpBox.IsChecked = settings.ShowBossHp;
        BossHpOpacity.Fraction = settings.BossHpOpacity;
        CombatCastOpacity.Fraction = settings.CombatCastOpacity;
        ShowTimetableBox.IsChecked = settings.ShowTimetable;
        ShowPetFarmBox.IsChecked = settings.ShowPetFarm;
        PetFarmLockedBox.IsChecked = !settings.PetFarmLocked;
        NetworkOpacity.Fraction = settings.OverlayOpacities.GetValueOrDefault(OverlayPlaces.Network, 0.6);
        LootOpacity.Fraction = settings.OverlayOpacities.GetValueOrDefault(OverlayPlaces.Loot, 0.6);
        BossOpacity.Fraction = settings.OverlayOpacities.GetValueOrDefault(OverlayPlaces.WorldBoss, 0.6);
        HotkeyOpacity.Fraction = settings.OverlayOpacities.GetValueOrDefault(OverlayPlaces.Hotkeys, 0.6);
        HotkeyOverlayLockedBox.IsChecked = !(settings.HotkeyOverlayLocked ?? settings.PetFarmLocked);
        PetListLockedBox.IsChecked = !(settings.PetListLocked ?? settings.PetFarmLocked);
        PetMapShowMapBox.IsChecked = settings.PetMapShowMap;
        PetMapShowListBox.IsChecked = settings.PetMapShowList;
        PetListOpacity.Fraction = settings.OverlayOpacities.GetValueOrDefault(OverlayPlaces.PetList, 0.6);
        PetInfoOpacity.Fraction = settings.OverlayOpacities.GetValueOrDefault(OverlayPlaces.PetInfo, settings.OverlayOpacity);
        PetInfoLockedBox.IsChecked = !settings.PetInfoLocked;
        TimetableLockedBox.IsChecked = !settings.TimetableLocked;
        InitPetMap(settings);
        InitBossPage(settings);
        SetHotkeyBox(HotkeyTimetableBox, HotkeyBinding.Parse(settings.HotkeyTimetable).ToString());
        SetHotkeyBox(HotkeyPetMapBox, HotkeyBinding.Parse(settings.HotkeyPetMap).ToString());
        SetHotkeyBox(HotkeyHotkeyOverlayBox, HotkeyBinding.Parse(settings.HotkeyHotkeyOverlay).ToString());
        SetHotkeyBox(HotkeyAllOverlaysBox, HotkeyBinding.Parse(settings.HotkeyAllOverlays).ToString());
        SetHotkeyBox(HotkeyWorldBossBox, HotkeyBinding.Parse(settings.HotkeyWorldBoss).ToString());
        SetHotkeyBox(HotkeyNetworkBox, HotkeyBinding.Parse(settings.HotkeyNetwork).ToString());
        SetHotkeyBox(HotkeyCombatBox, HotkeyBinding.Parse(settings.HotkeyCombatOverlay).ToString());
        SetHotkeyBox(HotkeyAssistBox, HotkeyBinding.Parse(settings.HotkeyAssist).ToString());
        SetHotkeyBox(HotkeyLootBox, HotkeyBinding.Parse(settings.HotkeyLoot).ToString());
        SetHotkeyBox(HotkeyCraftBox, HotkeyBinding.Parse(settings.HotkeyCraft).ToString());
        ShowLootOverlayBox.IsChecked = settings.ShowLootOverlay;
        LootOverlayLockedBox.IsChecked = !settings.LootOverlayLocked;
        LootWholeSessionBox.IsChecked = settings.LootOverlayWholeSession;
        LootHideCommonBox.IsChecked = settings.LootOverlayHideCommon;
        int[] lootLineChoices = { 3, 5, 8, 10, 15, 20, 30 };
        int lootLines = lootLineChoices.Contains(settings.LootOverlayLines) ? settings.LootOverlayLines : 8;
        foreach (int lines in lootLineChoices)
        {
            LootLinesBox.Items.Add(new ComboBoxItem { Content = lines.ToString(), Tag = lines, IsSelected = lines == lootLines });
        }

        InitCombatPage(settings);
        ShowNetworkOverlayBox.IsChecked = settings.ShowNetworkOverlay;
        NetworkOverlayLockedBox.IsChecked = !settings.NetworkOverlayLocked;
        foreach (var box in NetworkRowBoxes())
        {
            box.IsChecked = !settings.NetworkOverlayHidden.Contains((string)box.Tag);
        }
        SetHotkeyBox(HotkeyPetArrowsBox, HotkeyBinding.Parse(settings.HotkeyPetArrows).ToString());
        SetHotkeyBox(HotkeyGatherArrowsBox, HotkeyBinding.Parse(settings.HotkeyGatherArrows).ToString());
        ShowHotkeyOverlayBox.IsChecked = settings.ShowHotkeyOverlay;
        foreach (var box in HotkeyOverlayRowBoxes())
        {
            box.IsChecked = !settings.HotkeyOverlayHidden.Contains((string)box.Tag);
        }
        AutoResetBox.IsChecked = settings.AutoResetEnabled;
        AutoResetSecondsBox.Text = Math.Clamp(settings.AutoResetSeconds, 1, 600).ToString();
        PopulateThemes(settings.Theme);
        SelectComboItem(FontSizeBox, settings.FontSize);
        SelectComboItem(OverlayStyleBox, settings.OverlayStyle);
        // The nearest of the offered steps (a hand-edited file may hold any number).
        var nearest = OverlayOpacityBox.Items.OfType<ComboBoxItem>()
            .OrderBy(i => Math.Abs(double.Parse((string)i.Tag, System.Globalization.CultureInfo.InvariantCulture) - settings.OverlayOpacity)).First();
        OverlayOpacityBox.SelectedItem = nearest;
        OverlayGridSnapBox.IsChecked = settings.OverlayGridSnap;
        OverlayDockingBox.IsChecked = settings.OverlayDocking;
        SelectComboItem(OverlayGridSizeBox, settings.OverlayGridSize.ToString());
        double timetableOpacity = settings.TimetableOpacity ?? settings.OverlayOpacity;
        TimetableOpacityField.Fraction = timetableOpacity;
        TimetableLookaheadSlider.Value = Math.Clamp(settings.TimetableLookaheadMinutes, 0, 60);
        InitTimetableEvents(settings);
        ShowTimetableLookahead(); // the handler does not fire when the value equals the slider's default
        AlwaysOnTopBox.IsChecked = settings.AlwaysOnTopOnStartup;
        // Reflects the REAL registry state, not the last value this dialog wrote - see
        // OnStartWithWindowsChanged's own remarks.
        StartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled();
        ShowShareBarsBox.IsChecked = settings.ShowShareBars;
        ShowDamageTakenBox.IsChecked = settings.ShowDamageTaken;
        CompactClickThroughBox.IsChecked = settings.CompactClickThrough;
        HideDuringMenusBox.IsChecked = settings.HideDuringMenus;
        StreamOverlayOnBox.IsChecked = settings.StreamOverlayOn;
        StreamShowNamesBox.IsChecked = settings.StreamShowNames;
        StreamNetworkBox.IsChecked = settings.StreamOverlayNetwork;
        StreamPortBox.Text = settings.StreamOverlayPort.ToString();
        ShowMenuControlStatus(settings.MenuControlMode);
        var unlocked = MenuWatcher.UnlockedOverlays(settings);
        MenuPausedStatus.Visibility = settings.HideDuringMenus && unlocked.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MenuPausedStatus.Text = string.Format(LocalizationManager.Instance["Settings.Menu.Paused"], string.Join(", ", unlocked.Select(k => LocalizationManager.Instance[k])));
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

    /// <summary>Dark and every theme file saved from the editor.</summary>
    private void PopulateThemes(string selected)
    {
        foreach (string name in ThemeFile.List())
        {
            ThemeBox.Items.Add(new ComboBoxItem { Content = name, Tag = ThemeFile.SelectionFor(name) });
        }

        SelectComboItem(ThemeBox, selected);
        if (ThemeBox.SelectedItem is null)
        {
            SelectComboItem(ThemeBox, ThemeManager.DefaultTheme); // a theme file that is gone
        }
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
            RefreshPetRareForLanguage();
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

    private void OnUploadSwitchChecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box && !PrivacyConsent.EnsureForUploadSwitch(this))
        {
            box.IsChecked = false;
        }

        // the answer was written to the file by the question: this window's own copy of the settings must not write the old one back when it is saved
        MeterSettings now = MeterSettings.Load();
        _settings.PrivacyDecision = now.PrivacyDecision;
        _settings.PrivacyVersion = now.PrivacyVersion;
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

    /// <summary>The switches in front of the hotkey names: shown in the hotkey overlay or not (Tag = the hotkey's id).</summary>
    private IEnumerable<CheckBox> HotkeyOverlayRowBoxes() =>
        new[] { OvHideUiBox, OvPauseBox, OvCopyDamageBox, OvClearBox, OvUploadBossBox, OvModeBox, OvTimetableBox, OvPetMapBox, OvHotkeyOverlayBox, OvAllOverlaysBox, OvWorldBossBox, OvNetworkBox, OvCombatBox, OvAssistBox, OvLootBox, OvCraftBox, OvPetArrowsBox, OvGatherArrowsBox };

    /// <summary>The switches of the lines the network overlay shows (Tag = the line's id).</summary>
    private IEnumerable<CheckBox> NetworkRowBoxes() =>
        new[] { NetRowServerBox, NetRowPingBox, NetRowJitterBox, NetRowTrafficBox, NetRowLossBox, NetRowStallsBox, NetRowGatewayBox, NetRowInternetBox, NetRowConnectionBox, NetRowLoadBox };

    private void OnOpenDebugFolderClicked(object sender, RoutedEventArgs e)
    {
        string dir = Aion2.Capture.DebugRecording.FolderPath;
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
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

    /// <summary>
    /// Hands one panel (Pets, Gathering) to the tools window, which shows it as a page of its own; this window is never shown then. The panel keeps working
    /// on this window's controls, <see cref="ReadPanel"/> reads them back.
    /// </summary>
    public UIElement? TakePanel(string name)
    {
        if (FindName(name) is StackPanel panel && panel.Parent is Panel parent)
        {
            parent.Children.Remove(panel);
            panel.Visibility = Visibility.Visible;
            return panel;
        }

        return null;
    }

    /// <summary>Writes only what the Pets or the Gathering panel holds into the settings (the rest of this window's copy may be older than the disk).</summary>
    public void ReadPanel(string name, MeterSettings s)
    {
        if (name == "PanelPets")
        {
            s.ShowPetFarm = ShowPetFarmBox.IsChecked ?? false;
            s.PetInfoLocked = !(PetInfoLockedBox.IsChecked ?? false);
            s.OverlayOpacities[OverlayPlaces.PetInfo] = PetInfoOpacity.Fraction;
            s.ShowPetArrows = ShowPetArrowsBox.IsChecked ?? true;
            s.PetMapPets = _petPicked.OrderBy(i => i).ToList();
            s.PetRegionListWidth = PetRegionLeftColumn.Width.Value;
            s.PetRareMax = (int)PetRareMaxSlider.Value;
            s.PetRareInstanceMin = (int)PetRareInstanceSlider.Value;
        }
        else
        {
            s.ShowGatherArrows = ShowGatherArrowsBox.IsChecked ?? true;
            s.CubeSoundEnabled = CubeSoundBox.IsChecked ?? false;
            s.CubeSound = (CubeSoundCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "ping";
            s.CubeSoundVolume = (int)CubeVolumeSlider.Value;
            s.ShowMapOnCube = ShowMapOnCubeBox.IsChecked ?? false;
            s.PetMapGatherItems = _gatherPicked.OrderBy(k => k).ToList();
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        CollectControls();
        _baseline = Snapshot();
    }

    /// <summary>What the controls say, written into the working copy (nothing is saved or applied yet).</summary>
    private void CollectControls()
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
        _settings.ShowAssistOverlay = onDisk.ShowAssistOverlay;
        _settings.AssistOverlayLocked = onDisk.AssistOverlayLocked;
        _settings.AssistOverlayLeft = onDisk.AssistOverlayLeft;
        _settings.AssistOverlayTop = onDisk.AssistOverlayTop;
        _settings.AssistOverlayScale = onDisk.AssistOverlayScale;
        _settings.AssistOverlayWidth = onDisk.AssistOverlayWidth;
        _settings.AssistOverlayHeight = onDisk.AssistOverlayHeight;
        _settings.BossOverlayLeft = onDisk.BossOverlayLeft;
        _settings.BossOverlayTop = onDisk.BossOverlayTop;
        _settings.BossOverlayScale = onDisk.BossOverlayScale;
        _settings.BossOverlayWidth = onDisk.BossOverlayWidth;
        _settings.BossOverlayHeight = onDisk.BossOverlayHeight;
        _settings.ShowDummyOverlay = onDisk.ShowDummyOverlay;
        _settings.DummyOverlayLocked = onDisk.DummyOverlayLocked;
        _settings.DummyOverlayLeft = onDisk.DummyOverlayLeft;
        _settings.DummyOverlayTop = onDisk.DummyOverlayTop;
        _settings.DummyOverlayScale = onDisk.DummyOverlayScale;
        _settings.DummyOverlayWidth = onDisk.DummyOverlayWidth;
        _settings.DummyOverlayHeight = onDisk.DummyOverlayHeight;
        _settings.DummyTestSeconds = onDisk.DummyTestSeconds;
        _settings.ShowCraftOverlay = onDisk.ShowCraftOverlay;
        _settings.CraftOverlayLocked = onDisk.CraftOverlayLocked;
        _settings.CraftOverlayLeft = onDisk.CraftOverlayLeft;
        _settings.CraftOverlayTop = onDisk.CraftOverlayTop;
        _settings.CraftOverlayScale = onDisk.CraftOverlayScale;
        _settings.CraftOverlayWidth = onDisk.CraftOverlayWidth;
        _settings.CraftOverlayHeight = onDisk.CraftOverlayHeight;
        _settings.CraftOverlayLines = onDisk.CraftOverlayLines;
        _settings.CraftResolve = onDisk.CraftResolve;
        _settings.CraftCountInventory = onDisk.CraftCountInventory;
        _settings.CraftCountStorage = onDisk.CraftCountStorage;
        _settings.CraftCountAccountStorage = onDisk.CraftCountAccountStorage;
        _settings.OverlayOpacities[OverlayPlaces.Craft] = onDisk.OverlayOpacities.GetValueOrDefault(OverlayPlaces.Craft, 0.6);
        _settings.LootOverlayLeft = onDisk.LootOverlayLeft;
        _settings.LootOverlayTop = onDisk.LootOverlayTop;
        _settings.LootOverlayScale = onDisk.LootOverlayScale;
        _settings.LootOverlayWidth = onDisk.LootOverlayWidth;
        _settings.LootOverlayHeight = onDisk.LootOverlayHeight;
        _settings.NetworkOverlayLeft = onDisk.NetworkOverlayLeft;
        _settings.NetworkOverlayTop = onDisk.NetworkOverlayTop;
        _settings.NetworkOverlayScale = onDisk.NetworkOverlayScale;
        _settings.NetworkOverlayWidth = onDisk.NetworkOverlayWidth;
        _settings.NetworkOverlayHeight = onDisk.NetworkOverlayHeight;
        _settings.CombatOverlayLeft = onDisk.CombatOverlayLeft;
        _settings.CombatOverlayTop = onDisk.CombatOverlayTop;
        _settings.CombatOverlayScale = onDisk.CombatOverlayScale;
        _settings.CombatOverlayWidth = onDisk.CombatOverlayWidth;
        _settings.CombatOverlayHeight = onDisk.CombatOverlayHeight;
        _settings.CombatCastOverlayLeft = onDisk.CombatCastOverlayLeft;
        _settings.CombatCastOverlayTop = onDisk.CombatCastOverlayTop;
        _settings.CombatCastOverlayScale = onDisk.CombatCastOverlayScale;
        _settings.CombatCastOverlayWidth = onDisk.CombatCastOverlayWidth;
        _settings.CombatCastOverlayHeight = onDisk.CombatCastOverlayHeight;
        _settings.CombatHpOverlayLeft = onDisk.CombatHpOverlayLeft;
        _settings.CombatHpOverlayTop = onDisk.CombatHpOverlayTop;
        _settings.CombatHpOverlayScale = onDisk.CombatHpOverlayScale;
        _settings.CombatHpOverlayWidth = onDisk.CombatHpOverlayWidth;
        _settings.CombatHpOverlayHeight = onDisk.CombatHpOverlayHeight;
        _settings.HotkeyOverlayLeft = onDisk.HotkeyOverlayLeft;
        _settings.HotkeyOverlayTop = onDisk.HotkeyOverlayTop;
        _settings.HotkeyOverlayScale = onDisk.HotkeyOverlayScale;
        _settings.HotkeyOverlayWidth = onDisk.HotkeyOverlayWidth;
        _settings.HotkeyOverlayHeight = onDisk.HotkeyOverlayHeight;
        _settings.CombatElements = onDisk.CombatElements;
        _settings.OverlayShifts = onDisk.OverlayShifts;
        _settings.OverlayDocks = onDisk.OverlayDocks;
        _settings.ToolWidths = onDisk.ToolWidths;
        _settings.ToolsNavCollapsed = onDisk.ToolsNavCollapsed;
        _settings.ToolsWindowLeft = onDisk.ToolsWindowLeft;
        _settings.ToolsWindowTop = onDisk.ToolsWindowTop;
        _settings.ToolsWindowHeight = onDisk.ToolsWindowHeight;
        _settings.TimetableLeft = onDisk.TimetableLeft;
        _settings.TimetableTop = onDisk.TimetableTop;
        _settings.TimetableScale = onDisk.TimetableScale;
        _settings.TimetableWidth = onDisk.TimetableWidth;

        _settings.CheckForUpdates = CheckForUpdatesBox.IsChecked ?? true;
        _settings.AutoUploadProfile = AutoUploadProfileBox.IsChecked ?? false;
        _settings.UploadOtherPlayersProfiles = UploadOtherProfilesBox.IsChecked ?? false;
        _settings.ShareLoginWithLegion = ShareLoginBox.IsChecked ?? false;
        _settings.LoginShareEligible = onDisk.LoginShareEligible;
        _settings.HotkeyHideUi = (string?)HotkeyHideUiBox.Tag ?? "";
        _settings.HotkeyPause = (string?)HotkeyPauseBox.Tag ?? "";
        _settings.HotkeyCopyDamage = (string?)HotkeyCopyDamageBox.Tag ?? "";
        _settings.HotkeyClear = (string?)HotkeyClearBox.Tag ?? "";
        _settings.HotkeyUploadBoss = (string?)HotkeyUploadBossBox.Tag ?? "";
        _settings.HotkeyMode = (string?)HotkeyModeBox.Tag ?? "";
        _settings.ShowBossHp = ShowBossHpBox.IsChecked ?? false;
        _settings.BossHpOpacity = BossHpOpacity.Fraction;
        _settings.CombatCastOpacity = CombatCastOpacity.Fraction;
        _settings.ShowTimetable = ShowTimetableBox.IsChecked ?? true;
        _settings.ShowPetFarm = ShowPetFarmBox.IsChecked ?? false;
        _settings.PetFarmLocked = !(PetFarmLockedBox.IsChecked ?? false);
        _settings.OverlayOpacities[OverlayPlaces.Network] = NetworkOpacity.Fraction;
        _settings.OverlayOpacities[OverlayPlaces.Loot] = LootOpacity.Fraction;
        _settings.OverlayOpacities[OverlayPlaces.WorldBoss] = BossOpacity.Fraction;
        _settings.OverlayOpacities[OverlayPlaces.Hotkeys] = HotkeyOpacity.Fraction;
        _settings.HotkeyOverlayLocked = !(HotkeyOverlayLockedBox.IsChecked ?? false);
        _settings.PetListLocked = !(PetListLockedBox.IsChecked ?? false);
        _settings.PetMapShowMap = PetMapShowMapBox.IsChecked ?? true;
        _settings.PetMapShowList = PetMapShowListBox.IsChecked ?? true;
        _settings.OverlayOpacities[OverlayPlaces.PetList] = PetListOpacity.Fraction;
        _settings.OverlayOpacities[OverlayPlaces.PetInfo] = PetInfoOpacity.Fraction;
        _settings.PetInfoLocked = !(PetInfoLockedBox.IsChecked ?? false);
        _settings.TimetableLocked = !(TimetableLockedBox.IsChecked ?? false);
        _settings.ShowPetMap = ShowPetMapBox.IsChecked ?? false;
        _settings.PetMapRadius = (int)PetMapRadiusSlider.Value;
        _settings.PetMapOpacity = PetMapOpacity.Fraction;
        _settings.PetMapPets = _petPicked.OrderBy(i => i).ToList();
        _settings.PetRegionListWidth = PetRegionLeftColumn.Width.Value;
        _settings.PetRareMax = (int)PetRareMaxSlider.Value;
        _settings.PetRareInstanceMin = (int)PetRareInstanceSlider.Value;
        _settings.PetMapGatherItems = _gatherPicked.OrderBy(k => k).ToList();
        _settings.HotkeyTimetable = (string?)HotkeyTimetableBox.Tag ?? "";
        _settings.HotkeyPetMap = (string?)HotkeyPetMapBox.Tag ?? "";
        _settings.HotkeyHotkeyOverlay = (string?)HotkeyHotkeyOverlayBox.Tag ?? "";
        _settings.HotkeyAllOverlays = (string?)HotkeyAllOverlaysBox.Tag ?? "";
        _settings.HotkeyWorldBoss = (string?)HotkeyWorldBossBox.Tag ?? "";
        _settings.HotkeyNetwork = (string?)HotkeyNetworkBox.Tag ?? "";
        _settings.HotkeyLoot = (string?)HotkeyLootBox.Tag ?? "";
        _settings.HotkeyCraft = (string?)HotkeyCraftBox.Tag ?? "";
        _settings.HotkeyCombatOverlay = (string?)HotkeyCombatBox.Tag ?? "";
        _settings.HotkeyAssist = (string?)HotkeyAssistBox.Tag ?? "";
        ReadCombatPage(_settings);
        _settings.AudioDeviceId = Chosen(AudioDeviceCombo, "");
        _settings.ShowNetworkOverlay = ShowNetworkOverlayBox.IsChecked ?? false;
        _settings.ShowLootOverlay = ShowLootOverlayBox.IsChecked ?? false;
        _settings.LootOverlayLocked = !(LootOverlayLockedBox.IsChecked ?? false);
        _settings.LootOverlayWholeSession = LootWholeSessionBox.IsChecked ?? false;
        _settings.LootOverlayHideCommon = LootHideCommonBox.IsChecked ?? false;
        if (LootLinesBox.SelectedItem is ComboBoxItem { Tag: int lootLines })
        {
            _settings.LootOverlayLines = lootLines;
        }

        _settings.NetworkOverlayLocked = !(NetworkOverlayLockedBox.IsChecked ?? false);
        _settings.NetworkOverlayHidden = NetworkRowBoxes().Where(b => b.IsChecked != true).Select(b => (string)b.Tag).ToList();
        _settings.HotkeyPetArrows = (string?)HotkeyPetArrowsBox.Tag ?? "";
        _settings.HotkeyGatherArrows = (string?)HotkeyGatherArrowsBox.Tag ?? "";
        _settings.ShowPetArrows = ShowPetArrowsBox.IsChecked ?? true;
        _settings.ShowGatherArrows = ShowGatherArrowsBox.IsChecked ?? true;
        _settings.CubeSoundEnabled = CubeSoundBox.IsChecked ?? false;
        _settings.CubeSound = (CubeSoundCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "ping";
        _settings.CubeSoundVolume = (int)CubeVolumeSlider.Value;
        _settings.ShowMapOnCube = ShowMapOnCubeBox.IsChecked ?? false;
        _settings.ShowGuards = ShowGuardsBox.IsChecked ?? true;
        _settings.ShowHotkeyOverlay = ShowHotkeyOverlayBox.IsChecked ?? false;
        _settings.HotkeyOverlayHidden = HotkeyOverlayRowBoxes().Where(b => b.IsChecked != true).Select(b => (string)b.Tag).ToList();
        _settings.AutoResetEnabled = AutoResetBox.IsChecked ?? false;
        _settings.AutoResetSeconds = int.TryParse(AutoResetSecondsBox.Text, out int seconds) ? Math.Clamp(seconds, 1, 600) : 10;
        _settings.AutoUploadBoss = AutoUploadBossBox.IsChecked ?? false;
        _settings.DebugRecording = DebugRecordingBox.IsChecked ?? false;
        _settings.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.Theme;
        _settings.FontSize = (FontSizeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.FontSize;
        _settings.OverlayStyle = (OverlayStyleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.OverlayStyle;
        _settings.OverlayOpacity = double.Parse((string)((ComboBoxItem)OverlayOpacityBox.SelectedItem).Tag, System.Globalization.CultureInfo.InvariantCulture);
        _settings.OverlayGridSnap = OverlayGridSnapBox.IsChecked ?? false;
        _settings.OverlayDocking = OverlayDockingBox.IsChecked ?? false;
        _settings.OverlayGridSize = int.TryParse((OverlayGridSizeBox.SelectedItem as ComboBoxItem)?.Tag as string, out int cell) ? cell : _settings.OverlayGridSize;
        _settings.TimetableOpacity = TimetableOpacityField.Fraction;
        _settings.TimetableLookaheadMinutes = (int)TimetableLookaheadSlider.Value;
        _settings.TimetableNotify = TimetableNotifyBox.IsChecked ?? false;
        _settings.ShowBossOverlay = ShowBossOverlayBox.IsChecked ?? false;
        _settings.BossOverlayLocked = !(BossOverlayLockedBox.IsChecked ?? false);
        _settings.BossNotify = BossNotifyBox.IsChecked ?? false;
        _settings.BossAlerts = _bossAlerts.ToDictionary(kv => kv.Key, kv => kv.Value);
        _settings.TimetableSoundMinutes = (int)TimetableSoundSlider.Value;
        _settings.TimetableEvents = _timetableEvents.ToDictionary(kv => kv.Key, kv => kv.Value);
        _settings.Language = LocalizationManager.Instance.Language;
        _settings.AlwaysOnTopOnStartup = AlwaysOnTopBox.IsChecked ?? false;
        _settings.ShowShareBars = ShowShareBarsBox.IsChecked ?? true;
        _settings.ShowDamageTaken = ShowDamageTakenBox.IsChecked ?? false;
        _settings.CompactClickThrough = CompactClickThroughBox.IsChecked ?? false;
        _settings.HideDuringMenus = HideDuringMenusBox.IsChecked ?? false;
        _settings.StreamOverlayOn = StreamOverlayOnBox.IsChecked ?? false;
        _settings.StreamShowNames = StreamShowNamesBox.IsChecked ?? false;
        _settings.StreamOverlayNetwork = StreamNetworkBox.IsChecked ?? true;
        _settings.StreamOverlayPort = int.TryParse(StreamPortBox.Text, out int streamPort) && streamPort is >= 1024 and <= 65000 ? streamPort : Streaming.StreamOverlayServer.DefaultPort;
        _settings.StreamOverlayKey = MeterSettings.Load().StreamOverlayKey; // made and renewed by MainWindow, never by this copy
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
    private string? _baseline;
    private bool _allowClose;

    /// <summary>The settings the controls stand for, as text, without the places of the overlays (they are saved to the file by the overlays themselves while this window is open).</summary>
    private string Snapshot()
    {
        CollectControls();
        var options = new System.Text.Json.JsonSerializerOptions
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    typeInfo =>
                    {
                        for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
                        {
                            string name = typeInfo.Properties[i].Name;
                            if (System.Text.RegularExpressions.Regex.IsMatch(name, "(Left|Top|Scale|Width|Height)$") || name is "ToolsNavCollapsed")
                            {
                                typeInfo.Properties.RemoveAt(i);
                            }
                        }
                    },
                },
            },
        };
        return System.Text.Json.JsonSerializer.Serialize(_settings, options);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _baseline ??= Snapshot();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing with changes that were not saved asks first: Save / Discard / Cancel
        if (!_allowClose && _baseline is not null && IsVisible && Snapshot() != _baseline)
        {
            MessageBoxResult answer = ThemedMessageBox.AskUnsaved(this, Title);
            if (answer == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (answer == MessageBoxResult.Yes)
            {
                CollectControls();
                Saved?.Invoke();
            }
        }

        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        MeterSettings onDisk = MeterSettings.Load();
        onDisk.SettingsWindowWidth = bounds.Width;
        onDisk.SettingsWindowHeight = bounds.Height;
        onDisk.Save();
        base.OnClosing(e);
    }

    // ---- World boss page: every field boss, grouped by map (a group folds in and out), with its time and what to play when it is back ----
    private Dictionary<string, BossAlertSetting> _bossAlerts = new();
    private readonly HashSet<string> _bossOpen = new();
    private readonly List<(FieldBossMap Map, FieldBoss Boss, TextBlock Text)> _bossTexts = new();
    private readonly List<(ComboBox Sound, Slider Volume, Button Test, Func<bool> HasSound)> _bossSoundControls = new();
    private readonly System.Windows.Threading.DispatcherTimer _bossTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private void InitBossPage(MeterSettings settings)
    {
        _bossAlerts = settings.BossAlerts.ToDictionary(kv => kv.Key, kv => new BossAlertSetting { Enabled = kv.Value.Enabled, Sound = kv.Value.Sound, Volume = kv.Value.Volume });
        ShowBossOverlayBox.IsChecked = settings.ShowBossOverlay;
        BossOverlayLockedBox.IsChecked = !settings.BossOverlayLocked;
        BossNotifyBox.IsChecked = settings.BossNotify;
        BuildBossPage();
        _bossTimer.Tick += (_, _) => RefreshBossTimes();
        _bossTimer.Start();
        Closed += (_, _) => _bossTimer.Stop();
        OnBossNotifyChanged(null, null);
    }

    /// <summary>The master switch is off: the sounds, volumes and play buttons are greyed out (the choices stay; the switch before a boss still works).</summary>
    private void OnBossNotifyChanged(object? sender, RoutedEventArgs? e)
    {
        bool on = BossNotifyBox.IsChecked == true;
        foreach (var (sound, volume, test, hasSound) in _bossSoundControls)
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

    private static readonly string[] AllPanels =
    {
        "PanelGeneral", "PanelMeter", "PanelCapture", "PanelHotkeys", "PanelSystem",
        "PanelNetwork", "PanelLoot", "PanelCombat", "PanelTimetable", "PanelBoss", "PanelPetMap", "PanelPets", "PanelGather", "PanelStream",
    };

    /// <summary>The page each button of the sidebar shows (the settings proper: only what belongs to no module).</summary>
    private static readonly Dictionary<string, string> PageOfNav = new()
    {
        ["NavGeneral"] = "PanelGeneral", ["NavMeter"] = "PanelMeter", ["NavInstall"] = "PanelCapture", ["NavHotkeys"] = "PanelHotkeys", ["NavUpdates"] = "PanelSystem",
    };

    /// <summary>The tools that live in this window for now (the module's own settings, without the sidebar): title and the panels they show.</summary>
    private static readonly Dictionary<string, (string TitleKey, string[] Panels)> Modules = new()
    {
        ["network"] = ("Settings.Nav.Network", new[] { "PanelNetwork" }),
        ["loot"] = ("Main.MenuTools.Loot", new[] { "PanelLoot" }),
        ["combat"] = ("Settings.Nav.Combat", new[] { "PanelCombat" }),
        ["timetable"] = ("Settings.Nav.Timetable", new[] { "PanelTimetable" }),
        ["boss"] = ("Settings.Nav.Boss", new[] { "PanelBoss" }),
        ["petmap"] = ("Settings.Nav.PetMap", new[] { "PanelPetMap" }),
        ["stream"] = ("Settings.Nav.Stream", new[] { "PanelStream" }),
    };

    private string? _module;
    private string _page = "NavGeneral";

    private CheckBox? _masterSource;

    /// <summary>The tool's own "show overlay" box (shown as the main switch in the header), its hotkey field, per tool.</summary>
    private (CheckBox Master, TextBox? Hotkey) MasterOf(string module) => module switch
    {
        "network" => (ShowNetworkOverlayBox, HotkeyNetworkBox),
        "loot" => (ShowLootOverlayBox, HotkeyLootBox),
        "combat" => (ShowCombatOverlayBox, HotkeyCombatBox),
        "timetable" => (ShowTimetableBox, HotkeyTimetableBox),
        "boss" => (ShowBossOverlayBox, HotkeyWorldBossBox),
        "petmap" => (ShowPetMapBox, HotkeyPetMapBox),
        _ => (StreamOverlayOnBox, null),
    };

    private void OnModuleMasterClicked(object sender, RoutedEventArgs e)
    {
        if (_masterSource is not null)
        {
            _masterSource.IsChecked = ModuleMasterBox.IsChecked == true;
        }

        UpdateMasterHint();
    }

    private void OnMasterSourceChanged(object sender, RoutedEventArgs e)
    {
        ModuleMasterBox.IsChecked = _masterSource?.IsChecked == true;
        UpdateMasterHint();
    }

    /// <summary>The note under the tool's name appears only while the main switch is off (the switches below need it on).</summary>
    private void UpdateMasterHint()
    {
        ModuleMasterHint.Text = LocalizationManager.Instance["Settings.Overlay.MasterHint"];
        ModuleMasterHint.Visibility = ModuleMasterBox.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnModuleKeyClicked(object sender, RoutedEventArgs e) => ShowHotkeysPage();

    /// <summary>The hotkey page of the settings proper.</summary>
    public void ShowHotkeysPage()
    {
        _module = null;
        NavHotkeys.IsChecked = true;
        ApplyView();
    }

    private void ShowModuleHeader(string? module)
    {
        if (_masterSource is not null)
        {
            _masterSource.Checked -= OnMasterSourceChanged;
            _masterSource.Unchecked -= OnMasterSourceChanged;
            _masterSource = null;
        }

        ModuleHeader.Visibility = module is null ? Visibility.Collapsed : Visibility.Visible;
        if (module is null)
        {
            return;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        var (master, hotkey) = MasterOf(module);
        ModuleTitleText.Text = loc[Modules[module].TitleKey];
        ModuleMasterText.Text = loc["Settings.Overlay.Master"];
        _masterSource = master;
        master.Visibility = Visibility.Collapsed; // the header's switch is this box
        ModuleMasterBox.IsChecked = master.IsChecked == true;
        UpdateMasterHint();
        master.Checked += OnMasterSourceChanged;
        master.Unchecked += OnMasterSourceChanged;
        string key = hotkey?.Text ?? "";
        ModuleKeyButton.Visibility = hotkey is null ? Visibility.Collapsed : Visibility.Visible;
        ModuleKeyButton.Content = key.Length > 0 ? key : loc["Settings.Hotkey.NoKey"];
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Name: { Length: > 0 } name })
        {
            _page = name;
            ApplyView();
        }
    }

    /// <summary>Shows one tool's own settings (null: the settings proper with the sidebar). The window keeps what was typed, so switching does not lose changes.</summary>
    public void ShowModule(string? module)
    {
        _module = module;
        ApplyView();
    }

    private void ApplyView()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string[] visible;
        if (_module is not null && Modules.TryGetValue(_module, out var module))
        {
            NavBar.Visibility = Visibility.Collapsed;
            visible = module.Panels;
            Title = loc[module.TitleKey];
        }
        else
        {
            NavBar.Visibility = Visibility.Visible;
            visible = new[] { PageOfNav.GetValueOrDefault(_page, "PanelGeneral") };
            Title = loc["Settings.WindowTitle"];
        }

        ShowModuleHeader(_module is not null && Modules.ContainsKey(_module) ? _module : null);
        foreach (string name in AllPanels)
        {
            if (FindName(name) is UIElement panel)
            {
                panel.Visibility = visible.Contains(name) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        if (visible.Contains("PanelBoss"))
        {
            RefreshBossTimes(force: true);
        }
    }

    private void RefreshBossTimes(bool force = false)
    {
        if (!force && PanelBoss.Visibility != Visibility.Visible)
        {
            return;
        }

        DateTime now = DateTime.Now;
        foreach (var (_, boss, text) in _bossTexts)
        {
            var status = Aion2FieldBosses.StatusOf(boss, now);
            text.Text = WorldBossText.Of(status, now);
            bool there = status.Phase == BossPhase.Now;
            text.SetResourceReference(TextBlock.ForegroundProperty, there ? "Brush.Heal" : "Brush.TextMuted");
            text.FontWeight = there ? FontWeights.SemiBold : FontWeights.Normal;
            text.ToolTip = status.SeenAt is { } seen ? string.Format(LocalizationManager.Instance["Boss.Seen"], seen.ToString("g")) : null;
        }
    }

    private void BuildBossPage()
    {
        BossGroups.Children.Clear();
        _bossTexts.Clear();
        _bossSoundControls.Clear();
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        DateTime now = DateTime.Now;
        if (_bossOpen.Count == 0 && Aion2FieldBosses.Maps.Count > 0)
        {
            // the map the player was last seen on (the freshest list) starts open
            var freshest = Aion2FieldBosses.Maps
                .OrderByDescending(m => m.Bosses.Select(b => Aion2FieldBosses.StatusOf(b, now).SeenAt ?? DateTime.MinValue).DefaultIfEmpty(DateTime.MinValue).Max())
                .First();
            _bossOpen.Add(freshest.Key);
        }

        foreach (var map in Aion2FieldBosses.Maps)
        {
            var card = new Border { Padding = new Thickness(8, 2, 8, 2) };
            card.SetResourceReference(FrameworkElement.StyleProperty, "SettingsCard");
            var stack = new StackPanel();
            var body = new StackPanel { Visibility = _bossOpen.Contains(map.Key) ? Visibility.Visible : Visibility.Collapsed };

            var arrow = new TextBlock { Text = body.Visibility == Visibility.Visible ? "▾" : "▸", Width = 16, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            var title = new TextBlock { Text = map.NameIn(language), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var count = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            var header = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 6, 0, 6) };
            DockPanel.SetDock(arrow, Dock.Left);
            DockPanel.SetDock(count, Dock.Right);
            header.Children.Add(arrow);
            header.Children.Add(count);
            header.Children.Add(title);
            header.MouseLeftButtonUp += (_, _) =>
            {
                bool open = body.Visibility != Visibility.Visible;
                body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                arrow.Text = open ? "▾" : "▸";
                if (open) { _bossOpen.Add(map.Key); } else { _bossOpen.Remove(map.Key); }
            };

            void ShowCount() => count.Text = string.Format(loc["Settings.Boss.Count"], map.Bosses.Count(b => _bossAlerts.TryGetValue(b.Npc.ToString(), out var a) && a.Enabled), map.Bosses.Count);
            foreach (var boss in map.Bosses)
            {
                body.Children.Add(BuildBossRow(map, boss, language, ShowCount));
            }

            ShowCount();
            stack.Children.Add(header);
            stack.Children.Add(body);
            card.Child = stack;
            BossGroups.Children.Add(card);
        }

        RefreshBossTimes(force: true);
    }

    private UIElement BuildBossRow(FieldBossMap map, FieldBoss boss, string language, Action showCount)
    {
        var loc = LocalizationManager.Instance;
        string key = boss.Npc.ToString();
        var alert = _bossAlerts.GetValueOrDefault(key) ?? new BossAlertSetting();
        void Touch() => _bossAlerts[key] = alert;

        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var on = new CheckBox { IsChecked = alert.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        on.Checked += (_, _) => { alert.Enabled = true; Touch(); showCount(); };
        on.Unchecked += (_, _) => { alert.Enabled = false; Touch(); showCount(); };

        var name = new TextBlock { Text = Aion2FieldBosses.DisplayName(map, boss, language), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var status = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        texts.Children.Add(name);
        texts.Children.Add(status);
        Grid.SetColumn(texts, 1);
        _bossTexts.Add((map, boss, status));

        var sound = new ComboBox { Margin = new Thickness(0, 0, 8, 0), Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        sound.Items.Add(new ComboBoxItem { Content = loc["Settings.Timetable.NoSound"], Tag = "" });
        foreach (string id in NotifySounds.All)
        {
            sound.Items.Add(new ComboBoxItem { Content = loc["Sound." + id], Tag = id });
        }

        sound.SelectedItem = sound.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == alert.Sound) ?? sound.Items[0];
        Grid.SetColumn(sound, 2);

        var volume = new Slider { Minimum = 0, Maximum = 100, Value = Math.Clamp(alert.Volume, 0, 100), TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = loc["Settings.Timetable.Volume"] };
        volume.SetResourceReference(FrameworkElement.StyleProperty, "ThemedSlider");
        Grid.SetColumn(volume, 3);

        var test = new Button { Content = "▶", Width = 28, Height = 26, Padding = new Thickness(0), ToolTip = loc["Settings.Timetable.Test"] };
        Grid.SetColumn(test, 4);

        void ShowTestState()
        {
            test.IsEnabled = alert.Sound.Length > 0 && BossNotifyBox.IsChecked == true;
            test.Opacity = test.IsEnabled ? 1.0 : 0.4;
        }

        sound.SelectionChanged += (_, _) =>
        {
            alert.Sound = (sound.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            Touch();
            ShowTestState();
        };
        volume.ValueChanged += (_, _) => { alert.Volume = (int)volume.Value; Touch(); };
        test.Click += (_, _) => NotifySounds.Play(alert.Sound, alert.Volume);
        ShowTestState();

        row.Children.Add(on);
        row.Children.Add(texts);
        row.Children.Add(sound);
        row.Children.Add(volume);
        row.Children.Add(test);
        _bossSoundControls.Add((sound, volume, test, () => alert.Sound.Length > 0));
        return row;
    }

    // ---- Pet-Karte page: which pets the map shows (species list on the left, the pets of the chosen species with switches on the right) ----
    private string _petSpecies = Aion2Pets.Species[0];
    private HashSet<int> _petPicked = new();
    private Dictionary<int, int> _petLevels = new();
    private Dictionary<int, int> _petProgress = new();

    private void InitPetMap(MeterSettings settings)
    {
        _petPicked = settings.PetMapPets.ToHashSet();
        _gatherPicked = settings.PetMapGatherItems.ToHashSet();
        BuildGatherPage();
        // the levels of the own pets (login frame, kept in the character file): pets at the top level need no farming and are greyed out
        var savedPets = Aion2CharacterStore.Load(Aion2CharacterStore.DefaultPath)?.Pets ?? new();
        _petLevels = savedPets.ToDictionary(p => p.Id, p => p.Level);
        _petProgress = savedPets.ToDictionary(p => p.Id, p => p.Progress);
        PetRegionLeftColumn.Width = new GridLength(Math.Clamp(settings.PetRegionListWidth, 120, 600));
        ShowPetMapBox.IsChecked = settings.ShowPetMap;
        ShowPetArrowsBox.IsChecked = settings.ShowPetArrows;
        ShowGatherArrowsBox.IsChecked = settings.ShowGatherArrows;
        ShowGuardsBox.IsChecked = settings.ShowGuards;
        LoadCubeSound(settings);
        PetMapRadiusSlider.Value = Math.Clamp(settings.PetMapRadius, 50, 500);
        PetMapOpacity.Fraction = settings.PetMapOpacity;
        ShowPetMapSliderValues();
        BuildPetSpecies();
        BuildPetRows();
        BuildPetRegionTree();
        BuildPetRegionRows();
        InitPetRare(settings);
    }

    public event Action? CopyStreamLinkRequested;

    public event Action? NewStreamKeyRequested;

    private void OnCopyStreamLinkClicked(object sender, RoutedEventArgs e) => CopyStreamLinkRequested?.Invoke();

    private void OnNewStreamKeyClicked(object sender, RoutedEventArgs e) => NewStreamKeyRequested?.Invoke();

    /// <summary>Raised by the button next to the count of boss fights that were not uploaded; MainWindow opens the history with only those.</summary>


    /// <summary>Raised when the player confirmed "reset to defaults"; MainWindow does it and opens this window again, with the undo.</summary>
    public event Action? ResetRequested;

    /// <summary>Raised by "Undo reset"; MainWindow puts the settings back and opens this window again.</summary>
    public event Action? UndoRequested;

    private System.Windows.Threading.DispatcherTimer? _resetTimer;
    private System.Windows.Threading.DispatcherTimer? _undoTimer;
    private DateTime _undoUntil;

    /// <summary>"Reset all settings": the first click asks again, the second does it. The question lapses after a few seconds.</summary>
    private void OnResetSettingsClicked(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        if (_resetTimer is { IsEnabled: true })
        {
            _resetTimer.Stop();
            _allowClose = true; // the window is closed and opened again with the defaults: nothing to ask
            ResetRequested?.Invoke();
            return;
        }

        ResetSettingsButton.Content = loc["Settings.Reset.Confirm"];
        _resetTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _resetTimer.Tick += (_, _) =>
        {
            _resetTimer.Stop();
            ResetSettingsButton.Content = loc["Settings.Reset.Button"];
        };
        _resetTimer.Start();
    }

    private void OnUndoResetClicked(object sender, RoutedEventArgs e)
    {
        _undoTimer?.Stop();
        _allowClose = true;
        UndoRequested?.Invoke();
    }

    /// <summary>For 30 seconds after a reset the window offers to take it back.</summary>
    private void ShowUndo(DateTime? until)
    {
        if (until is not DateTime end || end <= DateTime.UtcNow)
        {
            return;
        }

        _undoUntil = end;
        UndoText.Visibility = UndoButton.Visibility = Visibility.Visible;
        void Update()
        {
            int left = (int)Math.Ceiling((_undoUntil - DateTime.UtcNow).TotalSeconds);
            if (left <= 0)
            {
                _undoTimer?.Stop();
                UndoText.Visibility = UndoButton.Visibility = Visibility.Collapsed;
                return;
            }

            UndoText.Text = string.Format(LocalizationManager.Instance["Settings.Reset.Undo"], left);
        }

        _undoTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _undoTimer.Tick += (_, _) => Update();
        _undoTimer.Start();
        Update();
    }

    /// <summary>Says which control mode the meter has seen: the feature works only where the game hides the cursor while playing.</summary>
    private void ShowMenuControlStatus(string mode)
    {
        var loc = LocalizationManager.Instance;
        MenuControlStatus.Text = Enum.TryParse(mode, true, out ControlMode parsed) && parsed != ControlMode.Unknown
            ? loc["Settings.Menu.Status." + parsed]
            : loc["Settings.Menu.Status.Unknown"];
    }

    /// <summary>The combat overlay's page: the two switches, the elements and their options.</summary>
    private void InitCombatPage(MeterSettings settings)
    {
        var loc = LocalizationManager.Instance;
        ShowCombatOverlayBox.IsChecked = settings.ShowCombatOverlay;
        CombatOverlayLockedBox.IsChecked = !settings.CombatOverlayLocked;
        CombatShowBossCastBox.IsChecked = settings.CombatShowBossCast;
        CombatShowTargetBox.IsChecked = settings.CombatShowTargetOfTarget;
        CombatHpLockedBox.IsChecked = !(settings.CombatHpOverlayLocked ?? settings.CombatOverlayLocked);
        CombatCastLockedBox.IsChecked = !(settings.CombatCastOverlayLocked ?? settings.CombatOverlayLocked);
        CombatTargetOpacity.Fraction = settings.CombatTargetOpacity;
        CombatHealerHpBox.IsChecked = settings.CombatHealerHpBar;
        CombatHealerEffectsBox.IsChecked = settings.CombatHealerEffects;
        CombatAllMonstersBox.IsChecked = settings.CombatCastsOfAllMonsters;
        FillChoice(CombatCastFilterCombo, new[] { ("all", loc["Settings.Combat.Filter.All"]), ("area", loc["Settings.Combat.Filter.Area"]), ("you", loc["Settings.Combat.Filter.You"]) }, settings.CombatCastFilter);
        FillChoice(CombatMaxCastsCombo, new[] { ("1", "1"), ("2", "2"), ("3", "3") }, Math.Clamp(settings.CombatMaxCasts, 1, 3).ToString());
        InitSpeech(settings);
        InitAudioDevices(settings);
    }

    /// <summary>The audio output choice: the Windows default first, then the devices the system has; a saved device that is not connected now stays listed.</summary>
    private void InitAudioDevices(MeterSettings settings)
    {
        var loc = LocalizationManager.Instance;
        var devices = AudioOutput.Devices().Select(d => (d.Id, d.Name)).ToList();
        if (settings.AudioDeviceId.Length > 0 && devices.All(d => d.Id != settings.AudioDeviceId))
        {
            devices.Add((settings.AudioDeviceId, loc["Settings.Audio.DeviceMissing"]));
        }

        devices.Insert(0, ("", loc["Settings.Audio.DeviceDefault"]));
        FillChoice(AudioDeviceCombo, devices, settings.AudioDeviceId);
    }

    private readonly CastSpeech _speech = new();
    private IReadOnlyList<SpeechVoice> _voices = Array.Empty<SpeechVoice>();
    private bool _speechLoading;

    /// <summary>The voice output's block: its switch, the voices the system has, what is announced, and the examples.</summary>
    private void InitSpeech(MeterSettings settings)
    {
        _speechLoading = true;
        _voices = CastSpeech.Voices();
        SpeechOnBox.IsChecked = settings.SpeechOn;
        SpeechSkillNameBox.IsChecked = settings.SpeechSkillName;
        SpeechOnYouBox.IsChecked = settings.SpeechOnYou;
        SpeechOnAllBox.IsChecked = settings.SpeechOnAll;
        SpeechLethalYouBox.IsChecked = settings.SpeechLethalYou;
        SpeechLethalAllBox.IsChecked = settings.SpeechLethalAll;
        SpeechVolumeOnYouSlider.Value = Math.Clamp(settings.SpeechVolumeOnYou, 0, 100);
        SpeechVolumeOnAllSlider.Value = Math.Clamp(settings.SpeechVolumeOnAll, 0, 100);
        SpeechVolumeLethalYouSlider.Value = Math.Clamp(settings.SpeechVolumeLethalYou, 0, 100);
        SpeechVolumeLethalAllSlider.Value = Math.Clamp(settings.SpeechVolumeLethalAll, 0, 100);
        ShowSpeechVolumes();
        SpeechNoVoices.Visibility = _voices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // the languages the voices come in; the saved one, else the program's own, else the first
        var languages = _voices.Select(v => v.Language).Distinct().Select(l => (l, CastSpeech.LanguageName(l))).ToList();
        string own = LocalizationManager.Instance.Language;
        string chosen = languages.Any(l => l.l == settings.SpeechLanguage) ? settings.SpeechLanguage
            : languages.FirstOrDefault(l => CastSpeech.TableLanguage(l.l) == own).l ?? languages.FirstOrDefault().l ?? "";
        FillChoice(SpeechLanguageCombo, languages.Select(l => (l.l, l.Item2)), chosen);
        FillSpeechVoices(settings.SpeechVoiceId);
        SpeechPanel.Visibility = settings.SpeechOn ? Visibility.Visible : Visibility.Collapsed;
        _speechLoading = false;
        RefreshSpeechExamples();
    }

    private void FillSpeechVoices(string selected)
    {
        var loc = LocalizationManager.Instance;
        string language = Chosen(SpeechLanguageCombo, "");
        FillChoice(SpeechVoiceCombo, _voices.Where(v => v.Language == language)
            .Select(v => (v.Id, v.Name + " – " + loc[v.Female ? "Settings.Speech.Female" : "Settings.Speech.Male"])), selected);
        if (SpeechVoiceCombo.Items.Count == 0)
        {
            SpeechVoiceCombo.SelectedIndex = -1;
        }
    }

    private void OnSpeechSwitched(object sender, RoutedEventArgs e)
    {
        if (SpeechPanel is null)
        {
            return;
        }

        SpeechPanel.Visibility = SpeechOnBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSpeechLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_speechLoading || SpeechVoiceCombo is null)
        {
            return;
        }

        FillSpeechVoices("");
        RefreshSpeechExamples();
    }

    private void ShowSpeechVolumes()
    {
        SpeechVolumeOnYouValue.Text = (int)SpeechVolumeOnYouSlider.Value + " %";
        SpeechVolumeOnAllValue.Text = (int)SpeechVolumeOnAllSlider.Value + " %";
        SpeechVolumeLethalYouValue.Text = (int)SpeechVolumeLethalYouSlider.Value + " %";
        SpeechVolumeLethalAllValue.Text = (int)SpeechVolumeLethalAllSlider.Value + " %";
    }

    private void OnSpeechVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeechVolumeLethalAllValue is not null) // sliders fire while the window is still being built
        {
            ShowSpeechVolumes();
        }
    }

    private void OnSpeechOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!_speechLoading && SpeechExamples is not null)
        {
            RefreshSpeechExamples();
        }
    }

    /// <summary>The settings as they stand in the window now (not yet saved), for the examples.</summary>
    private MeterSettings SpeechPreviewSettings()
    {
        var s = new MeterSettings();
        ReadSpeech(s);
        return s;
    }

    /// <summary>Three sample casts with what the voice would say for each under the current switches, each with a play button.</summary>
    private void RefreshSpeechExamples()
    {
        var loc = LocalizationManager.Instance;
        MeterSettings s = SpeechPreviewSettings();
        string words = CastSpeech.WordsLanguage(s);
        var samples = new[]
        {
            (loc["Settings.Speech.SampleYou"], loc["Speech.SampleIce"], true, false, false),
            (loc["Settings.Speech.SampleArea"], loc["Speech.SampleIce"], false, true, false),
            (loc["Settings.Speech.SampleLethal"], loc["Speech.SampleDeath"], false, true, true),
        };
        SpeechExamples.Children.Clear();
        for (int i = 0; i < samples.Length; i++)
        {
            var (what, skill, atYou, all, lethal) = samples[i];
            string? say = CastSpeech.Sentence(s, skill, atYou, all, lethal, words);
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var desc = new TextBlock { Text = what, VerticalAlignment = VerticalAlignment.Center, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            var text = new TextBlock { Text = say is null ? loc["Settings.Speech.NotSaid"] : "„" + say + "“", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontStyle = say is null ? FontStyles.Normal : FontStyles.Italic };
            if (say is null)
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            }

            var play = new Button { Content = "▶", Padding = new Thickness(12, 3, 12, 3), IsEnabled = say is not null && SpeechVoiceCombo.SelectedItem is not null, VerticalAlignment = VerticalAlignment.Center };
            string spoken = say ?? "";
            double volume = CastSpeech.Volume(s, atYou, all, lethal);
            play.Click += (_, _) => _speech.Speak(spoken, Chosen(SpeechVoiceCombo, ""), volume, Chosen(AudioDeviceCombo, ""));
            Grid.SetColumn(text, 1);
            Grid.SetColumn(play, 2);
            row.Children.Add(desc);
            row.Children.Add(text);
            row.Children.Add(play);
            if (i > 0)
            {
                SpeechExamples.Children.Add(DividerLine());
            }

            SpeechExamples.Children.Add(row);
        }
    }

    private static System.Windows.Shapes.Rectangle DividerLine()
    {
        var line = new System.Windows.Shapes.Rectangle { Height = 1 };
        line.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Border");
        return line;
    }

    private void ReadSpeech(MeterSettings settings)
    {
        settings.SpeechOn = SpeechOnBox.IsChecked ?? false;
        settings.SpeechLanguage = Chosen(SpeechLanguageCombo, "");
        settings.SpeechVoiceId = Chosen(SpeechVoiceCombo, "");
        settings.SpeechSkillName = SpeechSkillNameBox.IsChecked ?? true;
        settings.SpeechOnYou = SpeechOnYouBox.IsChecked ?? false;
        settings.SpeechOnAll = SpeechOnAllBox.IsChecked ?? false;
        settings.SpeechLethalYou = SpeechLethalYouBox.IsChecked ?? false;
        settings.SpeechLethalAll = SpeechLethalAllBox.IsChecked ?? false;
        settings.SpeechVolumeOnYou = (int)SpeechVolumeOnYouSlider.Value;
        settings.SpeechVolumeOnAll = (int)SpeechVolumeOnAllSlider.Value;
        settings.SpeechVolumeLethalYou = (int)SpeechVolumeLethalYouSlider.Value;
        settings.SpeechVolumeLethalAll = (int)SpeechVolumeLethalAllSlider.Value;
    }

    private static void FillChoice(ComboBox box, IEnumerable<(string Value, string Text)> choices, string selected)
    {
        box.Items.Clear();
        foreach (var (value, text) in choices)
        {
            box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        }

        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected) ?? box.Items[0];
    }

    private static string Chosen(ComboBox box, string fallback) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private void ReadCombatPage(MeterSettings settings)
    {
        settings.ShowCombatOverlay = ShowCombatOverlayBox.IsChecked ?? false;
        settings.CombatOverlayLocked = !(CombatOverlayLockedBox.IsChecked ?? false);
        settings.CombatShowBossCast = CombatShowBossCastBox.IsChecked ?? true;
        settings.CombatShowTargetOfTarget = CombatShowTargetBox.IsChecked ?? false;
        settings.CombatHpOverlayLocked = !(CombatHpLockedBox.IsChecked ?? false);
        settings.CombatCastOverlayLocked = !(CombatCastLockedBox.IsChecked ?? false);
        settings.CombatTargetOpacity = CombatTargetOpacity.Fraction;
        settings.CombatHealerHpBar = CombatHealerHpBox.IsChecked ?? false;
        settings.CombatHealerEffects = CombatHealerEffectsBox.IsChecked ?? false;
        settings.CombatCastsOfAllMonsters = CombatAllMonstersBox.IsChecked ?? false;
        settings.CombatCastFilter = Chosen(CombatCastFilterCombo, "all");
        settings.CombatMaxCasts = int.TryParse(Chosen(CombatMaxCastsCombo, "2"), out int most) ? most : 2;
        ReadSpeech(settings);
    }

    /// <summary>"Reset size and position" of the overlay named in the button's tag (its window title).</summary>
    private void OnResetOverlayPlaceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string title })
        {
            OverlayPlaces.Reset(title);
            MeterSettings onDisk = MeterSettings.Load();
            _settings.CombatElements = onDisk.CombatElements;
        }
    }

    /// <summary>A hotkey flipped the pet map or the timetable while this window is open: the switches follow.</summary>
    public void SyncOverlaySwitches(MeterSettings s)
    {
        ShowPetMapBox.IsChecked = s.ShowPetMap;
        ShowTimetableBox.IsChecked = s.ShowTimetable;
        ShowHotkeyOverlayBox.IsChecked = s.ShowHotkeyOverlay;
        ShowPetFarmBox.IsChecked = s.ShowPetFarm;
        ShowBossOverlayBox.IsChecked = s.ShowBossOverlay;
        ShowNetworkOverlayBox.IsChecked = s.ShowNetworkOverlay;
        ShowLootOverlayBox.IsChecked = s.ShowLootOverlay;
        ShowCombatOverlayBox.IsChecked = s.ShowCombatOverlay;
        ShowPetArrowsBox.IsChecked = s.ShowPetArrows;
        ShowGatherArrowsBox.IsChecked = s.ShowGatherArrows;
    }

    private void OnPetMapSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ShowPetMapSliderValues();

    private void ShowPetMapSliderValues()
    {
        if (PetMapRadiusValue is null)
        {
            return;
        }

        PetMapRadiusValue.Text = string.Format(LocalizationManager.Instance["Settings.PetMap.RadiusValue"], (int)PetMapRadiusSlider.Value);
    }

    private HashSet<string> _gatherPicked = new();

    /// <summary>The sound for a Hidden Cube that comes into range: switch, sound, volume and a test button.</summary>
    private void LoadCubeSound(MeterSettings settings)
    {
        var loc = LocalizationManager.Instance;
        CubeSoundBox.IsChecked = settings.CubeSoundEnabled;
        CubeSoundCombo.Items.Clear();
        foreach (string id in NotifySounds.All)
        {
            CubeSoundCombo.Items.Add(new ComboBoxItem { Content = loc["Sound." + id], Tag = id });
        }

        CubeSoundCombo.SelectedItem = CubeSoundCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.CubeSound) ?? CubeSoundCombo.Items[0];
        CubeVolumeSlider.Value = Math.Clamp(settings.CubeSoundVolume, 0, 100);
        CubeSoundTest.Click -= TestCubeSound;
        CubeSoundTest.Click += TestCubeSound;
        CubeArrivalTest.Click -= TestCubeArrival;
        CubeArrivalTest.Click += TestCubeArrival;
        ShowMapOnCubeBox.IsChecked = settings.ShowMapOnCube;
    }

    private void TestCubeArrival(object sender, RoutedEventArgs e) => NotifySounds.PlayCubeArrival((int)CubeVolumeSlider.Value);

    private void TestCubeSound(object sender, RoutedEventArgs e) =>
        NotifySounds.Play((CubeSoundCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "ping", (int)CubeVolumeSlider.Value);

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

    // ---- Region tab of "Shown pets": information only (which pets live where, and how far the player's own pets are); nothing is switched here ----
    private string? _petRegionKey;
    private readonly HashSet<string> _petRegionClosed = new();

    private void OnPetTabClick(object sender, RoutedEventArgs e)
    {
        bool region = sender == PetTabRegion, rare = sender == PetTabRare;
        PetTabType.IsChecked = !region && !rare;
        PetTabRegion.IsChecked = region;
        PetTabRare.IsChecked = rare;
        PetTypePanel.Visibility = region || rare ? Visibility.Collapsed : Visibility.Visible;
        PetRegionPanel.Visibility = region ? Visibility.Visible : Visibility.Collapsed;
        PetRarePanel.Visibility = rare ? Visibility.Visible : Visibility.Collapsed;
        if (rare)
        {
            ShowPetRare();
        }
    }

    /// <summary>For layout checks (render-settings): opens the Region tab.</summary>
    internal void ShowPetRegionTab() => OnPetTabClick(PetTabRegion, new RoutedEventArgs());

    /// <summary>For layout checks (render-settings): opens the Rare tab, with the first line whose key starts with <paramref name="pick"/> chosen.</summary>
    internal void ShowPetRareTab(string? pick = null)
    {
        OnPetTabClick(PetTabRare, new RoutedEventArgs());
        PickPetRare(pick);
    }

    private static string RegionGroupName(string id) => LocalizationManager.Instance["Settings.PetRegion.Group." + id];

    /// <summary>Alphabetical order of the region names by the rules of the interface language (ä, é or ё sort differently per language).</summary>
    private static StringComparer RegionNameComparer(string language)
    {
        try
        {
            return StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(language), ignoreCase: true);
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return StringComparer.CurrentCultureIgnoreCase;
        }
    }

    private void BuildPetRegionTree()
    {
        PetRegionTree.Children.Clear();
        var loc = LocalizationManager.Instance;
        var groups = Aion2PetRegions.Groups;
        // a region is told apart by its group too: "Events" (and the unnamed rest) exist once per map, with the same key
        _petRegionKey ??= groups.Where(g => g.Regions.Count > 0).Select(g => g.Id + "/" + g.Regions[0].Key).FirstOrDefault();
        foreach (var group in groups)
        {
            bool closed = _petRegionClosed.Contains(group.Id);
            var arrow = new TextBlock { Text = closed ? "\u25B8" : "\u25BE", FontSize = 10, Width = 14, VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            var title = new TextBlock { Text = RegionGroupName(group.Id), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 7, 0, 5), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            head.Children.Add(arrow);
            head.Children.Add(title);
            string groupId = group.Id;
            head.MouseLeftButtonDown += (_, _) =>
            {
                if (!_petRegionClosed.Remove(groupId))
                {
                    _petRegionClosed.Add(groupId);
                }

                BuildPetRegionTree();
            };
            PetRegionTree.Children.Add(head);
            if (closed)
            {
                continue;
            }

            foreach (var region in group.Regions.OrderBy(r => r.NameIn(loc.Language), RegionNameComparer(loc.Language)))
            {
                bool selected = group.Id + "/" + region.Key == _petRegionKey;
                int maxCount = region.Pets.Count(IsMax);
                var name = new TextBlock { Text = region.NameIn(loc.Language), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
                name.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Brush.Accent" : "Brush.Text");
                // pets in the region / of them at the top level (blue), the same two numbers as the badges of the Type tab
                var counts = new TextBlock { FontSize = 11 };
                var all = new Run(region.Pets.Count.ToString());
                all.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextSubtle");
                var max = new Run("/" + maxCount) { Foreground = new SolidColorBrush(Color.FromRgb(0x4D, 0x9B, 0xFF)) };
                counts.Inlines.Add(all);
                counts.Inlines.Add(max);
                var badge = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(7, 1, 7, 1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
                    Child = counts, ToolTip = string.Format(loc["Settings.PetRegion.BadgeTip"], region.Pets.Count, maxCount) };
                badge.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                var row = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(badge, Dock.Right);
                row.Children.Add(badge);
                row.Children.Add(name);
                var item = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(14, 0, 0, 2), Cursor = Cursors.Hand, Child = row,
                    BorderThickness = new Thickness(1), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
                if (selected)
                {
                    item.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
                    item.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                }

                string key = group.Id + "/" + region.Key;
                item.MouseLeftButtonDown += (_, _) =>
                {
                    _petRegionKey = key;
                    PetRegionSearchBox.Text = "";
                    BuildPetRegionTree();
                    BuildPetRegionRows();
                };
                PetRegionTree.Children.Add(item);
            }
        }
    }

    /// <summary>"Level 2 (26 / 75)", "Level 3 (max)", or a dash for a pet the player does not have.</summary>
    private string PetLevelText(int petId)
    {
        var loc = LocalizationManager.Instance;
        if (!_petLevels.TryGetValue(petId, out int level))
        {
            return "\u2013";
        }

        return level >= Aion2Pets.TopLevel
            ? string.Format(loc["Settings.PetRegion.LevelMax"], level)
            : string.Format(loc["Settings.PetRegion.Level"], level, _petProgress.GetValueOrDefault(petId), Aion2Pets.ProgressNeeded(level));
    }

    private void BuildPetRegionRows()
    {
        if (PetRegionRows is null)
        {
            return;
        }

        PetRegionRows.Children.Clear();
        var loc = LocalizationManager.Instance;
        string filter = PetRegionSearchBox.Text.Trim();
        // a search looks through every region, otherwise the chosen one is shown
        var regions = Aion2PetRegions.Groups.SelectMany(g => g.Regions.Select(r => (Key: g.Id + "/" + r.Key, Region: r)));
        var ids = (filter.Length > 0 ? regions : regions.Where(r => r.Key == _petRegionKey)).SelectMany(r => r.Region.Pets).Distinct();
        var pets = ids.Select(id => (Id: id, Name: Aion2Pets.PetName(id, loc.Language) ?? $"#{id}"))
            .Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var (id, petName) in pets)
        {
            bool max = IsMax(id);
            var row = new Grid { Margin = new Thickness(0, 2, 18, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = petName, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var level = new TextBlock { Text = PetLevelText(id), VerticalAlignment = VerticalAlignment.Center };
            if (max)
            {
                level.Foreground = new SolidColorBrush(Color.FromRgb(0x4D, 0x9B, 0xFF));
            }
            else
            {
                level.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            }

            Grid.SetColumn(level, 1);
            row.Children.Add(name);
            row.Children.Add(level);
            PetRegionRows.Children.Add(row);
        }
    }

    private void OnPetRegionSearchChanged(object sender, TextChangedEventArgs e) => BuildPetRegionRows();
}
