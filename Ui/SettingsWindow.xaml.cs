using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionSniffer.Ui;

public partial class SettingsWindow : Window
{
    private readonly MeterSettings _settings;
    private string? _aionInstallFolder;
    private readonly List<CharacterProfile> _characters;

    /// <summary>Loaded once, asynchronously, right after the window opens - see LoadServerCatalogAsync.
    /// Empty until that finishes (or if the backend is unreachable), in which case
    /// NewCharacterServerBox is simply empty rather than blocking the whole dialog on a network call.</summary>
    private List<AionSniffer.Server.ServerCatalogEntry> _serverCatalog = new();

    public SettingsWindow(MeterSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        // Faction/server copied along with the rest -- leaving any of these out here silently
        // wipes them on the next Save, since this copy is what gets written back.
        _characters = settings.Characters
            .Select(c => new CharacterProfile
            {
                Name = c.Name,
                ClassName = c.ClassName,
                Faction = c.Faction,
                ServerFingerprint = c.ServerFingerprint,
                ServerDisplayName = c.ServerDisplayName,
                ServerVersion = c.ServerVersion,
            })
            .ToList();

        ShowPlayersBox.IsChecked = settings.ShowPlayers;
        ShowMinionNpcsBox.IsChecked = settings.ShowMinionNpcs;
        ShowCommonNpcsBox.IsChecked = settings.ShowCommonNpcs;
        ShowEliteNpcsBox.IsChecked = settings.ShowEliteNpcs;
        ShowHeroicNpcsBox.IsChecked = settings.ShowHeroicNpcs;
        ShowLegendaryNpcsBox.IsChecked = settings.ShowLegendaryNpcs;
        CheckForUpdatesBox.IsChecked = settings.CheckForUpdates;
        SelectComboItem(ThemeBox, settings.Theme);
        SelectComboItem(FontSizeBox, settings.FontSize);
        AlwaysOnTopBox.IsChecked = settings.AlwaysOnTopOnStartup;

        // Built from LocalizationManager.SupportedLanguages rather than hardcoded in XAML -- see
        // LanguageBox's own remarks. Each item's Content is the language's OWN native name
        // (LocalizationManager.Instance changes what CONTENT="{local:Loc ...}" renders as
        // elsewhere, but this ComboBox's own items are plain strings, not themselves localized --
        // "Deutsch" should read as "Deutsch" regardless of which language is currently active, the
        // same way a real language picker never translates its own entries).
        foreach (var (code, nativeName) in LocalizationManager.SupportedLanguages)
        {
            LanguageBox.Items.Add(new ComboBoxItem { Content = nativeName, Tag = code });
        }

        SelectComboItem(LanguageBox, LocalizationManager.Instance.Language);

        _aionInstallFolder = settings.AionInstallFolder;
        AionInstallFolderBox.Text = _aionInstallFolder ?? "(not set)";
        UpdateAionFolderStatus();
        UpdateServerFingerprint();
        ServerDisplayNameBox.Text = settings.ServerDisplayName ?? "";

        _ = LoadServerCatalogAsync();

        RefreshCharacterLists();
        if (settings.ActiveCharacterName is string activeName && _characters.Any(c => c.Name == activeName))
        {
            ActiveCharacterBox.SelectedItem = activeName;
        }

        // Setting IsChecked here also fires OnAutoDetectActiveCharacterChanged (ToggleButton
        // raises Checked/Unchecked from a property-value change same as from a real click), which
        // is what sets ActiveCharacterBox's initial IsEnabled -- no separate call needed for that.
        AutoDetectActiveCharacterBox.IsChecked = settings.AutoDetectActiveCharacter;
    }

    private void OnAutoDetectActiveCharacterChanged(object sender, RoutedEventArgs e)
    {
        // While auto-detect is on, ActiveCharacterBox just displays whatever the last skill use
        // picked -- disabled so a manual selection here doesn't look meaningful when it would be
        // overwritten again the moment a skill is seen (see MeterSettings.AutoDetectActiveCharacter).
        ActiveCharacterBox.IsEnabled = AutoDetectActiveCharacterBox.IsChecked != true;
    }

    private void RefreshCharacterLists()
    {
        // Bound to the CharacterProfile objects themselves now, not pre-formatted strings -- the
        // ItemTemplate (icon + name, see XAML) needs Name/ClassName as separate bindings.
        CharactersList.ItemsSource = _characters.ToList();

        string? previouslySelected = ActiveCharacterBox.SelectedItem as string;
        ActiveCharacterBox.ItemsSource = _characters.Select(c => c.Name).ToList();
        if (previouslySelected is not null && _characters.Any(c => c.Name == previouslySelected))
        {
            ActiveCharacterBox.SelectedItem = previouslySelected;
        }
    }

    /// <summary>
    /// Fetched once when the window opens. On success, populates NewCharacterServerBox - done here
    /// rather than blocking the constructor, since a slow/unreachable backend must not delay the
    /// whole Settings dialog opening for a picker that only matters when actually adding a
    /// character.
    /// </summary>
    private async Task LoadServerCatalogAsync()
    {
        _serverCatalog = await AionSniffer.Server.ServerCatalogClient.FetchAsync();

        NewCharacterServerBox.Items.Clear();
        foreach (var entry in _serverCatalog)
        {
            NewCharacterServerBox.Items.Add(new ComboBoxItem { Content = entry.ToString(), Tag = entry });
        }
    }

    private void OnAddCharacterClicked(object sender, RoutedEventArgs e)
    {
        string name = NewCharacterNameBox.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }

        // Per the user: which server a character is on is now a required, explicit pick from the
        // backend's curated list, not something silently stamped in the background - refusing the
        // Add here (rather than falling back to "unknown") is what actually makes it required.
        if (NewCharacterServerBox.SelectedItem is not ComboBoxItem { Tag: AionSniffer.Server.ServerCatalogEntry server })
        {
            MessageBox.Show(this, "Please pick which server this character is on first.",
                "Add character", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Content is now the class icon <Image>, not text (see XAML) -- the class name lives in
        // Tag instead, since it's still needed as data even though it's no longer displayed.
        string className = (NewCharacterClassBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        string faction = (NewCharacterFactionBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        _characters.RemoveAll(c => c.Name == name); // re-adding an existing name replaces class+faction
        _characters.Add(new CharacterProfile
        {
            Name = name,
            ClassName = className,
            Faction = faction,
            ServerFingerprint = AionSniffer.Server.ServerIdentity.DetectFingerprint(_aionInstallFolder),
            ServerDisplayName = server.Name,
            ServerVersion = server.Version,
        });
        NewCharacterNameBox.Text = "";
        RefreshCharacterLists();
    }

    /// <summary>
    /// Picking a character loads it into the fields above, so Update has something to work from
    /// and the current class/faction are visible rather than having to be remembered.
    /// </summary>
    private void OnCharacterSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CharactersList.SelectedItem is not CharacterProfile selected)
        {
            return;
        }

        NewCharacterNameBox.Text = selected.Name;
        SelectByTag(NewCharacterClassBox, selected.ClassName);
        SelectByTag(NewCharacterFactionBox, selected.Faction);

        // Best-effort: if the catalog hasn't finished loading yet, or this character predates the
        // picker and has no name/version to match, this simply leaves nothing selected rather than
        // guessing - OnUpdateCharacterClicked already falls back to the character's existing value
        // in that case instead of wiping it out.
        NewCharacterServerBox.SelectedItem = NewCharacterServerBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is AionSniffer.Server.ServerCatalogEntry entry
                && entry.Name == selected.ServerDisplayName && entry.Version == selected.ServerVersion);
    }

    /// <summary>Used only for the Aion Installation section's own install-level display name, not
    /// per-character (see OnAddCharacterClicked/OnUpdateCharacterClicked for those - they read a
    /// pick from NewCharacterServerBox instead).</summary>
    private string? CurrentServerDisplayNameOrNull() =>
        string.IsNullOrWhiteSpace(ServerDisplayNameBox.Text) ? null : ServerDisplayNameBox.Text.Trim();

    private static void SelectByTag(ComboBox box, string tag)
    {
        foreach (object item in box.Items)
        {
            if (item is ComboBoxItem entry && (entry.Tag as string) == tag)
            {
                box.SelectedItem = entry;
                return;
            }
        }
    }

    /// <summary>
    /// Applies the fields to the SELECTED character rather than adding a new one -- which is what
    /// makes renaming possible at all: Add keys on the name, so editing a name there would leave
    /// the old entry behind and create a second one.
    /// </summary>
    private void OnUpdateCharacterClicked(object sender, RoutedEventArgs e)
    {
        int index = CharactersList.SelectedIndex;
        if (index < 0 || index >= _characters.Count)
        {
            return;
        }

        string name = NewCharacterNameBox.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }

        // A rename onto a name that already exists would leave two entries answering to it, and
        // everything downstream (active character, the faction anchors) keys on the name.
        if (_characters.Any(c => c.Name == name) && _characters[index].Name != name)
        {
            MessageBox.Show(this, $"A character named \"{name}\" is already registered.",
                "Update character", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string previousName = _characters[index].Name;
        // The technical fingerprint keeps its existing value rather than always re-stamping from
        // whatever is currently detected: blindly overwriting it here would misattribute an
        // existing character to a different server the moment someone points the Aion Installation
        // section above at a different install to register a second character. The catalog pick
        // (name/version) DOES update if the user picked something in NewCharacterServerBox - see
        // OnCharacterSelected, which pre-selects the character's current pick so editing something
        // else (e.g. fixing a class) doesn't require re-picking the server too, but explicitly
        // choosing a different one here is exactly how a wrong pick gets corrected.
        CharacterProfile previous = _characters[index];
        var pickedServer = (NewCharacterServerBox.SelectedItem as ComboBoxItem)?.Tag as AionSniffer.Server.ServerCatalogEntry;
        _characters[index] = new CharacterProfile
        {
            Name = name,
            ClassName = (NewCharacterClassBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
            Faction = (NewCharacterFactionBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
            ServerFingerprint = previous.ServerFingerprint
                ?? AionSniffer.Server.ServerIdentity.DetectFingerprint(_aionInstallFolder),
            ServerDisplayName = pickedServer?.Name ?? previous.ServerDisplayName,
            ServerVersion = pickedServer?.Version ?? previous.ServerVersion,
        };

        // The active character is stored by name, so a rename has to carry it along or the
        // selection silently falls back to "none".
        if (_settings.ActiveCharacterName == previousName)
        {
            _settings.ActiveCharacterName = name;
        }

        RefreshCharacterLists();
        ActiveCharacterBox.SelectedItem = name;

        // RefreshCharacterLists rebinds the list, which drops the selection -- put it back so the
        // row you just edited stays highlighted and can be edited again without re-picking it.
        CharactersList.SelectedIndex = index;
    }

    private void OnRemoveCharacterClicked(object sender, RoutedEventArgs e)
    {
        int index = CharactersList.SelectedIndex;
        if (index >= 0 && index < _characters.Count)
        {
            _characters.RemoveAt(index);
            RefreshCharacterLists();
        }
    }

    private void OnSelectAionFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select the Aion install folder (containing Chat.log)",
        };
        if (!string.IsNullOrEmpty(_aionInstallFolder) && Directory.Exists(_aionInstallFolder))
        {
            dialog.InitialDirectory = _aionInstallFolder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _aionInstallFolder = dialog.FolderName;
            AionInstallFolderBox.Text = _aionInstallFolder;
            UpdateAionFolderStatus();
            UpdateServerFingerprint();
        }
    }

    /// <summary>See Server/ServerIdentity.cs for why this, and not Chat.log or the client's file
    /// version, is what identifies the server.</summary>
    private void UpdateServerFingerprint()
    {
        string? fingerprint = AionSniffer.Server.ServerIdentity.DetectFingerprint(_aionInstallFolder);
        ServerFingerprintText.Text = fingerprint
            ?? "Not detected (bin64\\config.ini / bin32\\config.ini not found here).";
    }

    /// <summary>
    /// Same idea as the AionRainMeter reference's red "Wrong path selected!" -- checks for
    /// bin64\game.dll or AION.bin (the 64-/32-bit client executables, see README's crypto
    /// investigation for how we know these paths) to confirm this is actually an Aion install
    /// root, not just some folder the user clicked into. Chat.log itself is checked separately and
    /// only as an informational note, not a hard failure: a fresh install with chatlog not yet
    /// enabled (see the client's builder_dev_dialog "Basic Chatlog" toggle, README) would otherwise
    /// look "wrong" even though the folder itself is correct.
    /// </summary>
    private void UpdateAionFolderStatus()
    {
        if (string.IsNullOrEmpty(_aionInstallFolder))
        {
            AionInstallFolderStatus.Text = "No folder selected yet.";
            AionInstallFolderStatus.Foreground = new SolidColorBrush(Colors.Gray);
            return;
        }

        bool looksLikeAionInstall = File.Exists(Path.Combine(_aionInstallFolder, "bin64", "game.dll"))
            || File.Exists(Path.Combine(_aionInstallFolder, "AION.bin"));

        if (!looksLikeAionInstall)
        {
            AionInstallFolderStatus.Text = "Wrong path selected! This doesn't look like an Aion install (no bin64\\game.dll or AION.bin found here).";
            AionInstallFolderStatus.Foreground = new SolidColorBrush(Colors.OrangeRed);
            return;
        }

        bool hasChatLog = File.Exists(Path.Combine(_aionInstallFolder, "Chat.log"));
        AionInstallFolderStatus.Text = hasChatLog
            ? "Aion install found, Chat.log present."
            : "Aion install found, but no Chat.log here yet -- chat logging may still need to be enabled in-game.";
        AionInstallFolderStatus.Foreground = new SolidColorBrush(hasChatLog ? Colors.LightGreen : Colors.Khaki);
    }

    /// <summary>Matches by Tag, not Content: Content is now a {local:Loc ...} binding (so it reads
    /// as "Dunkel"/"Ciemny"/... depending on the current GUI language), while Tag stays the fixed,
    /// language-independent value ("Dark") that MeterSettings actually stores -- see
    /// ThemeBox's/FontSizeBox's XAML.</summary>
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

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        _settings.ShowPlayers = ShowPlayersBox.IsChecked ?? false;
        _settings.ShowMinionNpcs = ShowMinionNpcsBox.IsChecked ?? false;
        _settings.ShowCommonNpcs = ShowCommonNpcsBox.IsChecked ?? false;
        _settings.ShowEliteNpcs = ShowEliteNpcsBox.IsChecked ?? false;
        _settings.ShowHeroicNpcs = ShowHeroicNpcsBox.IsChecked ?? false;
        _settings.ShowLegendaryNpcs = ShowLegendaryNpcsBox.IsChecked ?? false;
        _settings.CheckForUpdates = CheckForUpdatesBox.IsChecked ?? true;
        _settings.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.Theme;
        _settings.FontSize = (FontSizeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.FontSize;
        _settings.Language = LocalizationManager.Instance.Language;
        _settings.AlwaysOnTopOnStartup = AlwaysOnTopBox.IsChecked ?? false;
        _settings.AionInstallFolder = _aionInstallFolder;
        _settings.ServerDisplayName = CurrentServerDisplayNameOrNull();
        _settings.Characters = _characters;
        _settings.ActiveCharacterName = ActiveCharacterBox.SelectedItem as string;
        _settings.AutoDetectActiveCharacter = AutoDetectActiveCharacterBox.IsChecked ?? true;

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
}
